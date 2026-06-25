using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class DmrlToolTests
{
    /// <summary>A small DMRL with one data-module entry and one PM entry.</summary>
    private const string Dml =
        """
        <dml>
          <dmlContent>
            <dmlEntry>
              <dmRef>
                <dmRefIdent>
                  <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                          subSystemCode="0" subSubSystemCode="0" assyCode="00"
                          disassyCode="00" disassyCodeVariant="A" infoCode="040"
                          infoCodeVariant="A" itemLocationCode="D"/>
                  <issueInfo issueNumber="001" inWork="00"/>
                  <language languageIsoCode="en" countryIsoCode="CA"/>
                </dmRefIdent>
                <dmRefAddressItems>
                  <dmTitle>
                    <techName>Example</techName>
                    <infoName>Description</infoName>
                  </dmTitle>
                  <issueDate year="2026" month="06" day="25"/>
                </dmRefAddressItems>
              </dmRef>
              <security securityClassification="01"/>
              <responsiblePartnerCompany enterpriseCode="ABCDE">
                <enterpriseName>Example Company</enterpriseName>
              </responsiblePartnerCompany>
            </dmlEntry>
            <dmlEntry>
              <pmRef>
                <pmRefIdent>
                  <pmCode modelIdentCode="EX" pmIssuer="12345" pmNumber="00001" pmVolume="00"/>
                </pmRefIdent>
                <pmRefAddressItems>
                  <pmTitle>Example Publication</pmTitle>
                </pmRefAddressItems>
              </pmRef>
            </dmlEntry>
          </dmlContent>
        </dml>
        """;

    private static (int code, string outText, string errText) Run(string dmlContent, params string[] opts)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-dmrl-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, dmlContent);
        try
        {
            var tool = new DmrlTool();
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var args = new List<string>(opts) { path };
            int code = tool.Run(args, stdout, stderr);
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Commands_EmitNewdmAndNewpm()
    {
        var (code, outText, _) = Run(Dml, "-s");
        Assert.Equal(0, code);

        string[] lines = outText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length);

        // First entry -> s1kd-newdm with the assembled code, issue, language, title, security, rpc.
        Assert.StartsWith("s1kd-newdm", lines[0]);
        // Full S1000D DMC: model-sdc-sys-subSys+subSubSys-assy-disassy+variant-info+variant-itemLoc
        Assert.Contains("-# EX-A-00-00-00-00A-040A-D", lines[0]);
        Assert.Contains("-n 001", lines[0]);
        Assert.Contains("-w 00", lines[0]);
        Assert.Contains("-L en", lines[0]);
        Assert.Contains("-C CA", lines[0]);
        Assert.Contains("-t Example", lines[0]);
        Assert.Contains("-i Description", lines[0]);
        Assert.Contains("-I 2026-06-25", lines[0]);
        Assert.Contains("-c 01", lines[0]);
        Assert.Contains("-R ABCDE", lines[0]);
        Assert.Contains("\"Example Company\"", lines[0]);

        // Second entry -> s1kd-newpm.
        Assert.StartsWith("s1kd-newpm", lines[1]);
        Assert.Contains("-# EX-12345-00001-00", lines[1]);
        Assert.Contains("\"Example Publication\"", lines[1]);
    }

    [Fact]
    public void GlobalFlags_PropagateToCommands()
    {
        var (code, outText, _) = Run(Dml, "-s", "-N", "-f", "-q", "-D", "custom.dmtypes");
        Assert.Equal(0, code);

        string firstLine = outText.Split('\n')[0];
        Assert.Contains("-N", firstLine);     // omit-issue (newdm honours it)
        Assert.Contains("-f", firstLine);     // overwrite
        Assert.Contains("-q", firstLine);     // quiet
        Assert.Contains("-D custom.dmtypes", firstLine); // dmtypes (newdm only)
    }

    [Fact]
    public void MissingTitleInfoName_EmitsDashBang()
    {
        const string dml =
            """
            <dml><dmlContent><dmlEntry>
              <dmRef><dmRefIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="040"
                        infoCodeVariant="A" itemLocationCode="D"/>
              </dmRefIdent><dmRefAddressItems>
                <dmTitle><techName>Only Tech</techName></dmTitle>
              </dmRefAddressItems></dmRef>
            </dmlEntry></dmlContent></dml>
            """;
        var (code, outText, _) = Run(dml, "-s");
        Assert.Equal(0, code);
        Assert.Contains("-t \"Only Tech\"", outText);
        Assert.Contains("-!", outText);
    }

    [Fact]
    public void Execute_WhenNewdmAvailable_CreatesObject_OtherwiseReportsGracefully()
    {
        // The behaviour of execute mode depends on whether NewdmTool has been
        // ported and registered. Either way the tool must not crash.
        bool newdmAvailable = ToolRegistry.Resolve("newdm") is not null;

        string workDir = Path.Combine(Path.GetTempPath(), $"s1kd-dmrl-work-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string path = Path.Combine(workDir, "DML.XML");
        File.WriteAllText(path, Dml);

        try
        {
            var tool = new DmrlTool();
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int code = tool.Run(new List<string> { "-f", "-@", workDir, path }, stdout, stderr);

            if (newdmAvailable)
            {
                // At least one CSDB object should have been produced.
                string[] created = Directory.GetFiles(workDir, "DMC-*");
                Assert.NotEmpty(created);
            }
            else
            {
                // Unavailable tools are reported, the run does not throw, and
                // the accumulated error status is non-zero.
                Assert.NotEqual(0, code);
                Assert.Contains("not available", stderr.ToString());
            }
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void Help_And_Version()
    {
        var tool = new DmrlTool();
        var helpOut = new StringWriter();
        Assert.Equal(0, tool.Run(new[] { "-h" }, helpOut, new StringWriter()));
        Assert.Contains("Usage: s1kd-dmrl", helpOut.ToString());

        var verOut = new StringWriter();
        Assert.Equal(0, tool.Run(new[] { "--version" }, verOut, new StringWriter()));
        Assert.Contains("1.12.0", verOut.ToString());
    }
}
