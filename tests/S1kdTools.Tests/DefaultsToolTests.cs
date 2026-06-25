using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class DefaultsToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new DefaultsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempFile(string content, string ext = ".txt")
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-def-{Guid.NewGuid():N}{ext}");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Version_PrintsNameAndVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("defaults", outText);
        Assert.Contains("3.0.0", outText);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("Usage: s1kd-defaults", outText);
    }

    [Fact]
    public void DumpBrexmap_OutputsDefaultMapping()
    {
        var (code, outText, _) = Run("-J");
        Assert.Equal(0, code);
        Assert.Contains("<brexMap>", outText);
        Assert.Contains("ident=\"languageIsoCode\"", outText);
    }

    [Fact]
    public void DumpDefaultsXml_ContainsBuiltinEntries()
    {
        var (code, outText, _) = Run();
        Assert.Equal(0, code);
        Assert.Contains("<?xml version=\"1.0\"?>", outText);
        Assert.Contains("<defaults>", outText);
        Assert.Contains("ident=\"issue\"", outText);
        Assert.Contains("value=\"6\"", outText);
    }

    [Fact]
    public void DumpDefaultsText_ProducesTabSeparated()
    {
        var (code, outText, _) = Run("-t");
        Assert.Equal(0, code);
        Assert.Contains("issue\t6\n", outText);
        Assert.Contains("inWork\t01\n", outText);
        Assert.DoesNotContain("<defaults>", outText);
    }

    [Fact]
    public void DumpDefaults_AppliesUserOverride()
    {
        var (code, outText, _) = Run("-t", "-n", "issue", "-v", "5");
        Assert.Equal(0, code);
        Assert.Contains("issue\t5\n", outText);
    }

    [Fact]
    public void DumpDefaults_AddsNewUserDefault()
    {
        var (code, outText, _) = Run("-t", "-n", "originator", "-v", "ACME");
        Assert.Equal(0, code);
        Assert.Contains("originator\tACME\n", outText);
    }

    [Fact]
    public void TextDefaultsToXml_ConvertsEntries()
    {
        string path = TempFile("issue\t6\ninWork\t01\n");
        try
        {
            var (code, outText, _) = Run("-d", path);
            Assert.Equal(0, code);
            Assert.Contains("<defaults>", outText);
            Assert.Contains("ident=\"issue\"", outText);
            Assert.Contains("value=\"6\"", outText);
            Assert.Contains("ident=\"inWork\"", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XmlDefaultsToText_ConvertsBack()
    {
        string xml =
            "<?xml version=\"1.0\"?>\n<defaults>\n" +
            "<default ident=\"issue\" value=\"6\"/>\n" +
            "<default ident=\"inWork\" value=\"01\"/>\n</defaults>\n";
        string path = TempFile(xml, ".xml");
        try
        {
            var (code, outText, _) = Run("-d", "-t", path);
            Assert.Equal(0, code);
            Assert.Equal("issue\t6\ninWork\t01\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextDmTypesToXml_ConvertsWithOptionalInfoName()
    {
        string path = TempFile("000\tdescript\tDescription\n520\tcomrep\n");
        try
        {
            var (code, outText, _) = Run("-D", path);
            Assert.Equal(0, code);
            Assert.Contains("<dmtypes>", outText);
            Assert.Contains("infoCode=\"000\"", outText);
            Assert.Contains("schema=\"descript\"", outText);
            Assert.Contains("infoName=\"Description\"", outText);
            Assert.Contains("infoCode=\"520\"", outText);
            // 520 has no infoName.
            Assert.DoesNotContain("infoName=\"\"", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XmlDmTypesToText_RoundTrips()
    {
        string xml =
            "<?xml version=\"1.0\"?>\n<dmtypes>\n" +
            "<type infoCode=\"000\" schema=\"descript\" infoName=\"Description\"/>\n" +
            "<type infoCode=\"520\" schema=\"comrep\"/>\n</dmtypes>\n";
        string path = TempFile(xml, ".xml");
        try
        {
            var (code, outText, _) = Run("-D", "-t", path);
            Assert.Equal(0, code);
            Assert.Equal("000\tdescript\tDescription\n520\tcomrep\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextFmTypesToXml_ConvertsWithOptionalXsl()
    {
        // Real info codes are <= 4 chars (sscanf "%4s"); use realistic values.
        string path = TempFile("001\ttitle\nLOEP\tloedm\tloedm.xsl\n");
        try
        {
            var (code, outText, _) = Run("-F", path);
            Assert.Equal(0, code);
            Assert.Contains("<fmtypes>", outText);
            Assert.Contains("infoCode=\"001\"", outText);
            Assert.Contains("type=\"title\"", outText);
            Assert.Contains("infoCode=\"LOEP\"", outText);
            Assert.Contains("xsl=\"loedm.xsl\"", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextDmTypesToXml_TruncatesInfoCodeToFiveChars()
    {
        // sscanf("%5s ...") truncates the info code to 5 chars; the leftover
        // character resumes as the next field, matching the C behavior.
        string path = TempFile("123456\tdescript\n");
        try
        {
            var (code, outText, _) = Run("-D", path);
            Assert.Equal(0, code);
            Assert.Contains("infoCode=\"12345\"", outText);
            Assert.Contains("schema=\"6\"", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XmlFmTypesToText_RoundTrips()
    {
        string xml =
            "<?xml version=\"1.0\"?>\n<fmtypes>\n" +
            "<fm infoCode=\"TP\" type=\"title\"/>\n" +
            "<fm infoCode=\"LOEDM\" type=\"loedm\" xsl=\"loedm.xsl\"/>\n</fmtypes>\n";
        string path = TempFile(xml, ".xml");
        try
        {
            var (code, outText, _) = Run("-F", "-t", path);
            Assert.Equal(0, code);
            Assert.Equal("TP\ttitle\nLOEDM\tloedm\tloedm.xsl\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Sort_OrdersEntriesByKey()
    {
        string path = TempFile("zulu\t1\nalpha\t2\nmike\t3\n");
        try
        {
            var (code, outText, _) = Run("-d", "-t", "-s", path);
            Assert.Equal(0, code);
            Assert.Equal("alpha\t2\nmike\t3\nzulu\t1\n", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Overwrite_PersistsConversionInPlace()
    {
        string path = TempFile("issue\t6\n");
        try
        {
            var (code, _, _) = Run("-d", "-f", path);
            Assert.Equal(0, code);
            string written = File.ReadAllText(path);
            Assert.Contains("<defaults>", written);
            Assert.Contains("ident=\"issue\"", written);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFile_ReturnsExitCode2()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-def-missing-{Guid.NewGuid():N}.txt");
        var (code, _, errText) = Run("-d", path);
        Assert.Equal(2, code);
        Assert.Contains("Could not open file", errText);
    }

    [Fact]
    public void DefaultsFromBrex_GeneratesEntries()
    {
        // A minimal BREX with a structureObjectRule that the default brexmap
        // maps (objectPath //@languageIsoCode -> languageIsoCode).
        string brex =
            "<?xml version=\"1.0\"?>\n<dmodule><brex><structureObjectRuleGroup>" +
            "<structureObjectRule>" +
            "<objectPath>//@languageIsoCode</objectPath>" +
            "<objectValue valueAllowed=\"en\">English</objectValue>" +
            "</structureObjectRule>" +
            "</structureObjectRuleGroup></brex></dmodule>";
        string brexPath = TempFile(brex, ".xml");
        try
        {
            var (code, outText, _) = Run("-b", brexPath, "-t");
            Assert.Equal(0, code);
            Assert.Contains("languageIsoCode\ten\n", outText);
        }
        finally { File.Delete(brexPath); }
    }

    [Fact]
    public void DmTypesFromBrex_GeneratesTypes()
    {
        // Default brexmap dmtypes path is //@infoCode.
        string brex =
            "<?xml version=\"1.0\"?>\n<dmodule><brex><structureObjectRuleGroup>" +
            "<structureObjectRule>" +
            "<objectPath>//@infoCode</objectPath>" +
            "<objectValue valueAllowed=\"000\">Description</objectValue>" +
            "<objectValue valueAllowed=\"520\">Remove and install</objectValue>" +
            "</structureObjectRule>" +
            "</structureObjectRuleGroup></brex></dmodule>";
        string brexPath = TempFile(brex, ".xml");
        try
        {
            var (code, outText, _) = Run("-b", brexPath, "-D", "-t");
            Assert.Equal(0, code);
            // BREX-generated dmtypes set infoCode + infoName but no schema, so
            // the schema column is empty: infoCode \t schema \t infoName.
            Assert.Contains("000\t\tDescription\n", outText);
            Assert.Contains("520\t\tRemove and install\n", outText);
        }
        finally { File.Delete(brexPath); }
    }

    [Fact]
    public void BundledShortOptions_AreParsed()
    {
        string path = TempFile("zulu\t1\nalpha\t2\n");
        try
        {
            // -dts == -d -t -s
            var (code, outText, _) = Run("-dts", path);
            Assert.Equal(0, code);
            Assert.Equal("alpha\t2\nzulu\t1\n", outText);
        }
        finally { File.Delete(path); }
    }
}
