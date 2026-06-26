using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class MetadataToolTests
{
    private static (int code, string outText, string errText) Run(string fixturePath, params string[] args)
    {
        var tool = new MetadataTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var all = new List<string>(args) { fixturePath };
        int code = tool.Run(all, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteFixture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-md-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, Fixtures.DataModule);
        return path;
    }

    [Fact]
    public void GetByName_PrintsValue()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-n", "techName");
            Assert.Equal(0, code);
            Assert.Equal("Example\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GetMissing_ReturnsExitCode4()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, _) = Run(path, "-n", "pmTitle");
            Assert.Equal(4, code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SetWithOverwrite_PersistsChange()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, _) = Run(path, "-n", "techName", "-v", "Renamed", "-f");
            Assert.Equal(0, code);

            var (_, outText, _) = Run(path, "-n", "techName");
            Assert.Equal("Renamed\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShowAll_IncludesKnownKeys()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path);
            Assert.Equal(0, code);
            Assert.Contains("techName", outText);
            Assert.Contains("issueInfo", outText);
            Assert.Contains("Example", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Info_ListsKeys()
    {
        var tool = new MetadataTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "-H" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("securityClassification", stdout.ToString());
    }

    // ---------------------------------------------------------------------
    // -H filtering by -n and -E
    // ---------------------------------------------------------------------

    [Fact]
    public void Info_WithName_ListsOnlyThatKey()
    {
        var tool = new MetadataTool();
        var stdout = new StringWriter();
        int code = tool.Run(new[] { "-H", "-n", "techName" }, stdout, new StringWriter());
        Assert.Equal(0, code);
        string text = stdout.ToString();
        Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("techName", text);
    }

    [Fact]
    public void Info_Editable_ExcludesReadOnlyKeys()
    {
        var tool = new MetadataTool();
        var stdout = new StringWriter();
        tool.Run(new[] { "-H", "-E" }, stdout, new StringWriter());
        string text = stdout.ToString();
        Assert.Contains("techName", text);     // editable
        Assert.DoesNotContain("\ntype", text); // read-only ("type" key)
    }

    // ---------------------------------------------------------------------
    // Delimiters: -t (tab), -0 (null), -T (raw)
    // ---------------------------------------------------------------------

    [Fact]
    public void Tab_DelimitsFieldsWithTab()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-t", "-n", "techName", "-n", "issueType");
            Assert.Equal(0, code);
            // Per-key tab delimiters plus the trailing '\n' the C adds when endl != '\n'.
            Assert.Equal("Example\tchanged\t\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Null_DelimitsFieldsWithNul()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-0", "-n", "techName");
            Assert.Equal(0, code);
            // Null delimiter after the value plus the trailing '\n' (endl != '\n').
            Assert.Equal("Example\0\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShowAll_Raw_UsesTabSeparator()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-T");
            Assert.Equal(0, code);
            Assert.Contains("techName\tExample\n", outText);
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------------
    // -d custom date format
    // ---------------------------------------------------------------------

    [Fact]
    public void DateFormat_AppliesToIssueDate()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-n", "issueDate", "-d", "%Y/%m/%d");
            Assert.Equal(0, code);
            Assert.Equal("2026/06/25\n", outText);
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------------
    // -F format string
    // ---------------------------------------------------------------------

    [Fact]
    public void Format_InterpolatesKeysAndEscapes()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-F", "%techName% - %issueType%\\n");
            Assert.Equal(0, code);
            // The \n in the format plus the trailing '\n' the C adds (endl == -1).
            Assert.Equal("Example - changed\n\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Format_LiteralPercent()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-F", "100%%");
            Assert.Equal(0, code);
            Assert.Equal("100%\n", outText); // trailing '\n' added (endl == -1)
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Format_FormatToken()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-F", "%format%");
            Assert.Equal(0, code);
            Assert.Equal("XML\n", outText); // fixture path ends .XML; trailing '\n' added
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------------
    // -w / -W / -v / -m conditions and EXIT_CONDITION_UNMET (8)
    // ---------------------------------------------------------------------

    [Fact]
    public void Where_Met_ProcessesObject()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-w", "techName", "-v", "Example", "-n", "issueType");
            Assert.Equal(0, code);
            Assert.Equal("changed\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Where_Unmet_ReturnsExit8AndNoOutput()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-w", "techName", "-v", "Nope", "-n", "issueType");
            Assert.Equal(8, code);
            Assert.Equal(string.Empty, outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WhereNot_Met_WhenValueDiffers()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-W", "techName", "-v", "Other", "-n", "issueType");
            Assert.Equal(0, code);
            Assert.Equal("changed\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WhereNot_Unmet_WhenValueEqual()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, _) = Run(path, "-W", "techName", "-v", "Example", "-n", "issueType");
            Assert.Equal(8, code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Where_RegexMatch_WithM()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(path, "-w", "techName", "-m", "Ex.*", "-n", "issueType");
            Assert.Equal(0, code);
            Assert.Equal("changed\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Where_RegexNoMatch_WithM_ReturnsExit8()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, _) = Run(path, "-w", "techName", "-m", "Zz.*", "-n", "issueType");
            Assert.Equal(8, code);
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------------
    // -c set metadata from a file
    // ---------------------------------------------------------------------

    [Fact]
    public void SetFromFile_AppliesAllAndOverwrites()
    {
        string path = WriteFixture();
        string edits = Path.Combine(Path.GetTempPath(), $"s1kd-edits-{Guid.NewGuid():N}.txt");
        File.WriteAllText(edits, "techName Renamed\nissueType new\n");
        try
        {
            var (code, _, _) = Run(path, "-c", edits, "-f");
            Assert.Equal(0, code);

            var (_, t1, _) = Run(path, "-n", "techName");
            var (_, t2, _) = Run(path, "-n", "issueType");
            Assert.Equal("Renamed\n", t1);
            Assert.Equal("new\n", t2);
        }
        finally { File.Delete(path); File.Delete(edits); }
    }

    // ---------------------------------------------------------------------
    // -q quiet suppresses non-fatal error messages
    // ---------------------------------------------------------------------

    [Fact]
    public void Quiet_SuppressesMissingMetadataError()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, errText) = Run(path, "-q", "-n", "pmTitle");
            Assert.Equal(4, code);
            Assert.Equal(string.Empty, errText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NonQuiet_ReportsMissingMetadataError()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, errText) = Run(path, "-n", "pmTitle");
            Assert.Equal(4, code);
            Assert.Contains("Data has no metadata: pmTitle", errText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidMetadataName_ReportsError()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, errText) = Run(path, "-n", "notARealKey");
            Assert.Equal(1, code);
            Assert.Contains("Invalid metadata name: notARealKey", errText);
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------------
    // ICN file metadata (derived from the file name)
    // ---------------------------------------------------------------------

    [Fact]
    public void Icn_ShowAll_DerivesFromFileName()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-icn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "ICN-S1000DBIKE-AAA-D000000-0-U8025-00555-A-001-01.PNG");
        File.WriteAllText(path, "not really an image");
        try
        {
            var (code, outText, _) = Run(path);
            Assert.Equal(0, code);
            Assert.Contains("type", outText);
            Assert.Contains("icn", outText);
            Assert.Contains("001", outText); // issue number
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Icn_GetByName_ReturnsField()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-icn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "ICN-EX-00001-001-01.PNG");
        File.WriteAllText(path, "x");
        try
        {
            var (code, outText, _) = Run(path, "-n", "issueNumber");
            Assert.Equal(0, code);
            Assert.Equal("001\n", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---------------------------------------------------------------------
    // version
    // ---------------------------------------------------------------------

    [Fact]
    public void Version_PrintsToolAndVersion()
    {
        var tool = new MetadataTool();
        var stdout = new StringWriter();
        int code = tool.Run(new[] { "--version" }, stdout, new StringWriter());
        Assert.Equal(0, code);
        Assert.Contains("s1kd-metadata", stdout.ToString());
        Assert.Contains("4.7.0", stdout.ToString());
    }
}
