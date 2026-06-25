using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class AsppToolTests
{
    private static (int code, string outText, string errText) RunStdin(string xml, params string[] args)
    {
        // Drive a temp file through the tool rather than redirecting Console.In,
        // so tests stay isolated and never touch the process's stdin.
        string path = WriteTemp(xml);
        try
        {
            return RunFile(path, args);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (int code, string outText, string errText) RunFile(string path, params string[] args)
    {
        var tool = new AsppTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var all = new List<string>(args) { path };
        int code = tool.Run(all, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteTemp(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-aspp-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, xml);
        return path;
    }

    // A minimal data module with a single assert in dmStatus and a content
    // applic plus annotated steps, exercising both display text and presentation.
    private const string Dm =
        "<dmodule>" +
          "<identAndStatusSection>" +
            "<dmAddress/>" +
            "<dmStatus>" +
              "<applic>" +
                "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"A\"/>" +
              "</applic>" +
            "</dmStatus>" +
          "</identAndStatusSection>" +
          "<content>" +
            "<referencedApplicGroup>" +
              "<applic id=\"app-B\">" +
                "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"B\"/>" +
              "</applic>" +
            "</referencedApplicGroup>" +
            "<procedure><mainProcedure>" +
              "<proceduralStep><para>Applicable to whole DM.</para></proceduralStep>" +
              "<proceduralStep applicRefId=\"app-B\"><para>B only.</para></proceduralStep>" +
              "<proceduralStep applicRefId=\"app-B\"><para>Also B only.</para></proceduralStep>" +
              "<proceduralStep><para>Back to whole DM.</para></proceduralStep>" +
            "</mainProcedure></procedure>" +
          "</content>" +
        "</dmodule>";

    [Fact]
    public void Generate_AddsDisplayText_FromIdent()
    {
        var (code, outText, _) = RunStdin(Dm, "-g");
        Assert.Equal(0, code);
        // Default format: "<name>: <values>" with name falling back to ident.
        Assert.Contains("<displayText><simplePara>version: A</simplePara></displayText>", outText);
        Assert.Contains("<displayText><simplePara>version: B</simplePara></displayText>", outText);
    }

    [Fact]
    public void Generate_WithFormatString_UsesCustomFormat()
    {
        var (code, outText, _) = RunStdin(Dm, "-F", "%name% = %values%", "-g");
        Assert.Equal(0, code);
        Assert.Contains("<simplePara>version = A</simplePara>", outText);
    }

    [Fact]
    public void Generate_Evaluate_UsesOperatorText()
    {
        const string dm =
            "<dmodule><content><referencedApplicGroup>" +
              "<applic id=\"a1\">" +
                "<evaluate andOr=\"or\">" +
                  "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"A\"/>" +
                  "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"B\"/>" +
                "</evaluate>" +
              "</applic>" +
            "</referencedApplicGroup></content></dmodule>";
        var (code, outText, _) = RunStdin(dm, "-g");
        Assert.Equal(0, code);
        Assert.Contains("<simplePara>version: A or version: B</simplePara>", outText);
    }

    [Fact]
    public void Generate_SetValues_UseSetOperator()
    {
        const string dm =
            "<dmodule><content><referencedApplicGroup>" +
              "<applic id=\"a1\">" +
                "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"A|B\"/>" +
              "</applic>" +
            "</referencedApplicGroup></content></dmodule>";
        var (code, outText, _) = RunStdin(dm, "-g");
        Assert.Equal(0, code);
        Assert.Contains("<simplePara>version: A, B</simplePara>", outText);
    }

    [Fact]
    public void Generate_WithAct_UsesDisplayName()
    {
        string actPath = WriteTemp(
            "<dmodule><content><applicCrossRefTable>" +
              "<productAttribute id=\"version\">" +
                "<name>Product version</name>" +
                "<displayName>Version</displayName>" +
                "<enumeration applicPropertyValues=\"A|B|C\"/>" +
              "</productAttribute>" +
            "</applicCrossRefTable></content></dmodule>");
        try
        {
            var (code, outText, _) = RunStdin(Dm, "-g", "-A", actPath);
            Assert.Equal(0, code);
            Assert.Contains("<simplePara>Version: A</simplePara>", outText);
        }
        finally
        {
            File.Delete(actPath);
        }
    }

    [Fact]
    public void Keep_DoesNotOverwriteExistingDisplayText()
    {
        const string dm =
            "<dmodule><content><referencedApplicGroup>" +
              "<applic id=\"a1\">" +
                "<displayText><simplePara>KEEP ME</simplePara></displayText>" +
                "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"A\"/>" +
              "</applic>" +
            "</referencedApplicGroup></content></dmodule>";
        var (code, outText, _) = RunStdin(dm, "-g", "-k");
        Assert.Equal(0, code);
        Assert.Contains("KEEP ME", outText);
        Assert.DoesNotContain("version: A", outText);
    }

    [Fact]
    public void Delete_RemovesDisplayTextWithComputerPart()
    {
        const string dm =
            "<dmodule><content><referencedApplicGroup>" +
              "<applic id=\"a1\">" +
                "<displayText><simplePara>version: A</simplePara></displayText>" +
                "<assert applicPropertyIdent=\"version\" applicPropertyType=\"prodattr\" applicPropertyValues=\"A\"/>" +
              "</applic>" +
            "</referencedApplicGroup></content></dmodule>";
        var (code, outText, _) = RunStdin(dm, "-D");
        Assert.Equal(0, code);
        Assert.DoesNotContain("displayText", outText);
        Assert.Contains("applicPropertyValues=\"A\"", outText);
    }

    [Fact]
    public void Delete_KeepsDisplayTextOnlyAnnotation()
    {
        const string dm =
            "<dmodule><content><referencedApplicGroup>" +
              "<applic id=\"a1\">" +
                "<displayText><simplePara>Manual text</simplePara></displayText>" +
              "</applic>" +
            "</referencedApplicGroup></content></dmodule>";
        var (code, outText, _) = RunStdin(dm, "-D");
        Assert.Equal(0, code);
        Assert.Contains("Manual text", outText);
    }

    [Fact]
    public void Presentation_RemovesRedundantAndAddsDmApplic()
    {
        var (code, outText, _) = RunStdin(Dm, "-p");
        Assert.Equal(0, code);

        // The third step (also app-B) loses its redundant applicRefId.
        // Easiest robust check: there is exactly one "Also B only" step and it
        // has no applicRefId attribute on its proceduralStep.
        int alsoIdx = outText.IndexOf("Also B only", StringComparison.Ordinal);
        Assert.True(alsoIdx > 0);
        int stepStart = outText.LastIndexOf("<proceduralStep", alsoIdx, StringComparison.Ordinal);
        int stepTagEnd = outText.IndexOf('>', stepStart);
        string stepTag = outText.Substring(stepStart, stepTagEnd - stepStart);
        Assert.DoesNotContain("applicRefId", stepTag);

        // The fourth step (back to whole DM) gains applicRefId="app-0000".
        Assert.Contains("applicRefId=\"app-0000\"", outText);

        // An inline applic for the whole DM is added with the default id.
        Assert.Contains("<applic id=\"app-0000\">", outText);
    }

    [Fact]
    public void Presentation_CustomDmApplicId()
    {
        var (code, outText, _) = RunStdin(Dm, "-p", "-a", "whole-dm");
        Assert.Equal(0, code);
        Assert.Contains("<applic id=\"whole-dm\">", outText);
        Assert.Contains("applicRefId=\"whole-dm\"", outText);
    }

    [Fact]
    public void Tags_PiMode_InsertsProcessingInstructions()
    {
        const string dm =
            "<dmodule><content>" +
              "<referencedApplicGroup>" +
                "<applic id=\"app-B\">" +
                  "<displayText><simplePara>B</simplePara></displayText>" +
                "</applic>" +
              "</referencedApplicGroup>" +
              "<procedure><mainProcedure>" +
                "<proceduralStep applicRefId=\"app-B\"><para>B only.</para></proceduralStep>" +
              "</mainProcedure></procedure>" +
            "</content></dmodule>";
        var (code, outText, _) = RunStdin(dm, "-t", "pi");
        Assert.Equal(0, code);
        Assert.Contains("<?s1kd-aspp Applicable to: B?>", outText);
    }

    [Fact]
    public void Tags_RemoveMode_DropsExistingPis()
    {
        const string dm =
            "<dmodule><content>" +
              "<referencedApplicGroup>" +
                "<applic id=\"app-B\"><displayText><simplePara>B</simplePara></displayText></applic>" +
              "</referencedApplicGroup>" +
              "<procedure><mainProcedure>" +
                "<?s1kd-aspp Applicable to: B?>" +
                "<proceduralStep applicRefId=\"app-B\"><para>B only.</para></proceduralStep>" +
              "</mainProcedure></procedure>" +
            "</content></dmodule>";
        var (code, outText, _) = RunStdin(dm, "-t", "remove");
        Assert.Equal(0, code);
        Assert.DoesNotContain("s1kd-aspp", outText);
    }

    [Fact]
    public void DumpDisptext_PrintsBuiltIn()
    {
        var tool = new AsppTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "-." }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("<disptext>", stdout.ToString());
        Assert.Contains("<operators>", stdout.ToString());
    }

    [Fact]
    public void Version_PrintsNameAndVersion()
    {
        var tool = new AsppTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "--version" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("aspp", stdout.ToString());
        Assert.Contains("5.1.0", stdout.ToString());
    }

    [Fact]
    public void Overwrite_WritesBackToFile()
    {
        string path = WriteTemp(Dm);
        string cwd = Directory.GetCurrentDirectory();
        try
        {
            var (code, _, _) = RunFile(path, "-g", "-f");
            Assert.Equal(0, code);
            string written = File.ReadAllText(path);
            Assert.Contains("version: A", written);
        }
        finally
        {
            // Restore cwd before deleting, per the in-process safety rules.
            Directory.SetCurrentDirectory(cwd);
            File.Delete(path);
        }
    }
}
