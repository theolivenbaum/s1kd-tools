using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class BrexCheckToolTests
{
    // A BREX module with a single structure object rule:
    //  - allowedObjectFlag="0" means //prohibited must NOT appear.
    // contextRules has no @rulesContext so it applies to any schema.
    private const string Brex =
        """
        <dmodule>
          <content>
            <brex>
              <contextRules>
                <structureObjectRule>
                  <brDecisionRef brDecisionIdentNumber="BREX-TEST-00001"/>
                  <objectPath allowedObjectFlag="0">//prohibited</objectPath>
                  <objectUse>The prohibited element must not be used.</objectUse>
                </structureObjectRule>
              </contextRules>
            </brex>
          </content>
        </dmodule>
        """;

    // A BREX with a value rule: //para/@status may only be "draft" or "final".
    private const string ValueBrex =
        """
        <dmodule>
          <content>
            <brex>
              <contextRules>
                <structureObjectRule>
                  <brDecisionRef brDecisionIdentNumber="BREX-TEST-00002"/>
                  <objectPath allowedObjectFlag="2">//para/@status</objectPath>
                  <objectUse>The status must be draft or final.</objectUse>
                  <objectValue valueAllowed="draft"/>
                  <objectValue valueAllowed="final"/>
                </structureObjectRule>
              </contextRules>
            </brex>
          </content>
        </dmodule>
        """;

    private const string ConformingObject =
        """
        <dmodule>
          <content>
            <description>
              <para>Hello.</para>
            </description>
          </content>
        </dmodule>
        """;

    private const string ViolatingObject =
        """
        <dmodule>
          <content>
            <description>
              <prohibited>I should not be here.</prohibited>
            </description>
          </content>
        </dmodule>
        """;

    private static XmlDocument Doc(string xml) => XmlUtils.ReadMem(xml);

    [Fact]
    public void Library_ConformingObject_NoErrors()
    {
        int errs = BrexCheck.Check(Doc(ConformingObject), Doc(Brex), BrexCheckOptions.None, out XmlDocument report);

        Assert.Equal(0, errs);
        Assert.Null(report.SelectSingleNode("//brex/error"));
        Assert.NotNull(report.SelectSingleNode("//brex/noErrors"));
    }

    [Fact]
    public void Library_ViolatingObject_OneError()
    {
        int errs = BrexCheck.Check(Doc(ViolatingObject), Doc(Brex), BrexCheckOptions.None, out XmlDocument report);

        Assert.Equal(1, errs);

        var error = report.SelectSingleNode("//brex/error") as XmlElement;
        Assert.NotNull(error);

        // objectPath, allowedObjectFlag, and objectUse must be recorded.
        var objectPath = error!.SelectSingleNode("objectPath") as XmlElement;
        Assert.NotNull(objectPath);
        Assert.Equal("//prohibited", objectPath!.InnerText);
        Assert.Equal("0", objectPath.GetAttribute("allowedObjectFlag"));
        Assert.Equal("The prohibited element must not be used.", error.SelectSingleNode("objectUse")!.InnerText);

        // The brDecisionRef is copied through.
        var brdr = error.SelectSingleNode("brDecisionRef") as XmlElement;
        Assert.NotNull(brdr);
        Assert.Equal("BREX-TEST-00001", brdr!.GetAttribute("brDecisionIdentNumber"));

        // The offending node is dumped with an xpath.
        var obj = error.SelectSingleNode("object") as XmlElement;
        Assert.NotNull(obj);
        Assert.Contains("prohibited", obj!.GetAttribute("xpath"));
    }

    [Fact]
    public void Library_ValueRule_AllowedValuePasses()
    {
        string obj =
            """
            <dmodule><content><description>
              <para status="draft">ok</para>
            </description></content></dmodule>
            """;

        int errs = BrexCheck.Check(Doc(obj), Doc(ValueBrex), BrexCheckOptions.Values, out _);
        Assert.Equal(0, errs);
    }

    [Fact]
    public void Library_ValueRule_DisallowedValueFails()
    {
        string obj =
            """
            <dmodule><content><description>
              <para status="bogus">bad</para>
            </description></content></dmodule>
            """;

        int errs = BrexCheck.Check(Doc(obj), Doc(ValueBrex), BrexCheckOptions.Values, out XmlDocument report);
        Assert.Equal(1, errs);
        Assert.NotNull(report.SelectSingleNode("//brex/error"));
    }

    [Fact]
    public void Tool_ConformingObject_ExitsZero()
    {
        string objPath = WriteTemp(ConformingObject);
        string brexPath = WriteTemp(Brex);
        try
        {
            var (code, _, _) = RunTool("-b", brexPath, "-x", objPath);
            Assert.Equal(0, code);
        }
        finally { File.Delete(objPath); File.Delete(brexPath); }
    }

    [Fact]
    public void Tool_ViolatingObject_ExitsBrexErrorWithReport()
    {
        string objPath = WriteTemp(ViolatingObject);
        string brexPath = WriteTemp(Brex);
        try
        {
            var (code, outText, _) = RunTool("-b", brexPath, "-x", objPath);

            Assert.Equal(1, code); // EXIT_BREX_ERROR
            Assert.Contains("<error", outText);
            Assert.Contains("//prohibited", outText);
            Assert.Contains("BREX-TEST-00001", outText);
        }
        finally { File.Delete(objPath); File.Delete(brexPath); }
    }

    [Fact]
    public void Tool_PrintInvalidFilenames()
    {
        string objPath = WriteTemp(ViolatingObject);
        string brexPath = WriteTemp(Brex);
        try
        {
            var (code, outText, _) = RunTool("-b", brexPath, "-f", objPath);
            Assert.Equal(1, code);
            Assert.Contains(objPath, outText);
        }
        finally { File.Delete(objPath); File.Delete(brexPath); }
    }

    [Fact]
    public void Tool_UnknownOption_Errors()
    {
        var (code, _, errText) = RunTool("--bogus");
        Assert.Equal(2, code); // EXIT_BAD_DMODULE used for option errors
        Assert.Contains("Unknown option", errText);
    }

    [Fact]
    public void Tool_BadXPathVersion_Errors()
    {
        var (code, _, _) = RunTool("-X", "2.0");
        Assert.Equal(4, code); // EXIT_BAD_XPATH_VERSION
    }

    private static (int code, string outText, string errText) RunTool(params string[] args)
    {
        var tool = new BrexCheckTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteTemp(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-brex-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, xml);
        return path;
    }
}
