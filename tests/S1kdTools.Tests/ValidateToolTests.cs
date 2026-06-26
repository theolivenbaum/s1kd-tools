using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class ValidateToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new ValidateTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-val-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, content);
        return path;
    }

    // A well-formed data module that references a remote schema (unresolvable
    // offline, so XSD validation degrades gracefully) and has no IDREF errors.
    private const string CleanDm =
        """
        <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.s1000d.org/S1000D_5-0/xml_schema_flat/descript.xsd">
          <content>
            <description>
              <para id="par-0001">First.</para>
              <para internalRefId="par-0001">References the first.</para>
            </description>
          </content>
        </dmodule>
        """;

    // Same, but with an internalRefId (xs:IDREF) pointing at a non-existent id.
    private const string BadIdrefDm =
        """
        <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.s1000d.org/S1000D_5-0/xml_schema_flat/descript.xsd">
          <content>
            <description>
              <para id="par-0001">First.</para>
              <para internalRefId="par-9999">Dangling reference.</para>
            </description>
          </content>
        </dmodule>
        """;

    // An xs:IDREFS attribute (warningRefs) with one valid and one dangling id.
    private const string BadIdrefsDm =
        """
        <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.s1000d.org/S1000D_5-0/xml_schema_flat/descript.xsd">
          <content>
            <warning id="wrn-0001"><warningAndCautionPara>Be careful.</warningAndCautionPara></warning>
            <description>
              <para warningRefs="wrn-0001 wrn-0002">Refers to warnings.</para>
            </description>
          </content>
        </dmodule>
        """;

    [Fact]
    public void Version_PrintsNameAndVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("validate", outText);
        Assert.Contains("4.3.3", outText);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("Usage: s1kd-validate", outText);
    }

    [Fact]
    public void CleanDm_IsValid_ExitsZero()
    {
        string path = TempFile(CleanDm);
        try
        {
            var (code, _, _) = Run(path);
            Assert.Equal(0, code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BadIdref_IsReportedInvalid_ExitsOne()
    {
        string path = TempFile(BadIdrefDm);
        try
        {
            var (code, _, errText) = Run(path);
            Assert.Equal(1, code);
            Assert.Contains("No matching ID for 'par-9999'.", errText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BadIdrefs_DanglingMemberReportedInvalid_ExitsOne()
    {
        string path = TempFile(BadIdrefsDm);
        try
        {
            var (code, _, errText) = Run(path);
            Assert.Equal(1, code);
            Assert.Contains("No matching ID for 'wrn-0002'.", errText);
            // The valid member must not be flagged.
            Assert.DoesNotContain("'wrn-0001'", errText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Quiet_SuppressesErrorOutputButKeepsExitCode()
    {
        string path = TempFile(BadIdrefDm);
        try
        {
            var (code, _, errText) = Run("-q", path);
            Assert.Equal(1, code);
            Assert.Equal(string.Empty, errText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShowInvalidFilenames_PrintsInvalidFile()
    {
        string path = TempFile(BadIdrefDm);
        try
        {
            var (code, outText, _) = Run("-f", "-q", path);
            Assert.Equal(1, code);
            Assert.Contains(path, outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShowValidFilenames_PrintsValidFile()
    {
        string path = TempFile(CleanDm);
        try
        {
            var (code, outText, _) = Run("-F", "-q", path);
            Assert.Equal(0, code);
            Assert.Contains(path, outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XmlReport_ContainsErrorForBadIdref()
    {
        string path = TempFile(BadIdrefDm);
        try
        {
            var (code, outText, _) = Run("-x", "-q", path);
            Assert.Equal(1, code);
            Assert.Contains("<s1kdValidateReport>", outText);
            Assert.Contains("<document", outText);
            Assert.Contains("No matching ID for 'par-9999'.", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verbose_ReportsSuccessForCleanDm()
    {
        string path = TempFile(CleanDm);
        try
        {
            var (code, _, errText) = Run("-v", path);
            Assert.Equal(0, code);
            Assert.Contains("SUCCESS", errText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Summary_PrintsCounts()
    {
        string clean = TempFile(CleanDm);
        string bad = TempFile(BadIdrefDm);
        try
        {
            var (code, _, errText) = Run("-T", "-q", clean, bad);
            Assert.Equal(1, code);
            Assert.Contains("Validated 2 files", errText);
            Assert.Contains("1 valid", errText);
            Assert.Contains("1 invalid", errText);
        }
        finally { File.Delete(clean); File.Delete(bad); }
    }

    [Fact]
    public void List_ValidatesEachFileNamed()
    {
        string clean = TempFile(CleanDm);
        string bad = TempFile(BadIdrefDm);
        string list = Path.Combine(Path.GetTempPath(), $"s1kd-val-list-{Guid.NewGuid():N}.txt");
        File.WriteAllText(list, clean + "\n" + bad + "\n");
        try
        {
            var (code, _, _) = Run("-l", "-q", list);
            // One invalid file -> overall failure.
            Assert.Equal(1, code);
        }
        finally { File.Delete(clean); File.Delete(bad); File.Delete(list); }
    }

    [Fact]
    public void UnknownOption_ReturnsError()
    {
        var (code, _, errText) = Run("--bogus");
        Assert.Equal(1, code);
        Assert.Contains("Unknown option", errText);
    }

    // The dangling internalRefId in BadIdrefDm is on the 5th source line
    // (1:<dmodule>, 2:<content>, 3:<description>, 4:first <para>, 5:second
    // <para>). The C reports the source line of the attribute's owner element
    // (xmlGetLineNo(node->parent)); the port must match.
    [Fact]
    public void BadIdref_ErrorReportsOwnerElementSourceLine()
    {
        string path = TempFile(BadIdrefDm);
        try
        {
            var (code, _, errText) = Run(path);
            Assert.Equal(1, code);
            Assert.Contains($"{path} (5): No matching ID for 'par-9999'.", errText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XmlReport_BadIdref_HasCorrectLineAttribute()
    {
        string path = TempFile(BadIdrefDm);
        try
        {
            var (code, outText, _) = Run("-x", "-q", path);
            Assert.Equal(1, code);
            Assert.Contains("<error line=\"5\">", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XmlReport_BadIdrefs_HasCorrectLineAttribute()
    {
        // The warningRefs (xs:IDREFS) attribute is on the 5th source line:
        // 1:<dmodule>, 2:<content>, 3:<warning>, 4:<description>, 5:<para ...>.
        string path = TempFile(BadIdrefsDm);
        try
        {
            var (code, outText, _) = Run("-x", "-q", path);
            Assert.Equal(1, code);
            Assert.Contains("<error line=\"5\">", outText);
            Assert.Contains("No matching ID for 'wrn-0002'.", outText);
        }
        finally { File.Delete(path); }
    }

    // A start tag spanning multiple source lines: libxml2's xmlGetLineNo (and
    // therefore the port) reports the line on which the start tag terminates.
    [Fact]
    public void BadIdref_MultiLineStartTag_ReportsTagEndLine()
    {
        const string dm =
            """
            <dmodule xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://x/descript.xsd">
              <content>
                <description>
                  <para id="par-0001">First.</para>
                  <para
                     internalRefId="par-9999"
                     >Dangling.</para>
                </description>
              </content>
            </dmodule>
            """;
        string path = TempFile(dm);
        try
        {
            // The <para ...> start tag opens on line 5 and closes on line 7.
            var (code, _, errText) = Run(path);
            Assert.Equal(1, code);
            Assert.Contains($"{path} (7): No matching ID for 'par-9999'.", errText);
        }
        finally { File.Delete(path); }
    }
}
