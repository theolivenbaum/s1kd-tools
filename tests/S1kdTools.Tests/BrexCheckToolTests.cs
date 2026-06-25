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

    // ---- SNS rules ----------------------------------------------------------

    // A BREX whose SNS rules allow only systemCode "42".
    private const string SnsBrex =
        """
        <dmodule>
          <content>
            <brex>
              <snsRules>
                <snsSystem>
                  <snsCode>42</snsCode>
                  <snsTitle>Answer</snsTitle>
                </snsSystem>
              </snsRules>
            </brex>
          </content>
        </dmodule>
        """;

    private static string DmWithCode(string systemCode, string subSystem = "0",
        string subSubSystem = "0", string assy = "00") =>
        $"""
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="X" systemDiffCode="A" systemCode="{systemCode}"
                        subSystemCode="{subSystem}" subSubSystemCode="{subSubSystem}"
                        assyCode="{assy}" disassyCode="00" disassyCodeVariant="A"
                        infoCode="040" infoCodeVariant="A" itemLocationCode="D"/>
              </dmIdent>
            </dmAddress>
          </identAndStatusSection>
          <content><description><para>x</para></description></content>
        </dmodule>
        """;

    [Fact]
    public void Library_Sns_ConformingSystemCode_NoError()
    {
        int errs = BrexCheck.Check(Doc(DmWithCode("42")), Doc(SnsBrex), BrexCheckOptions.Sns, out XmlDocument report);

        Assert.Equal(0, errs);
        Assert.NotNull(report.SelectSingleNode("//sns/noErrors"));
        Assert.Null(report.SelectSingleNode("//sns/error"));
    }

    [Fact]
    public void Library_Sns_ViolatingSystemCode_Error()
    {
        int errs = BrexCheck.Check(Doc(DmWithCode("99")), Doc(SnsBrex), BrexCheckOptions.Sns, out XmlDocument report);

        Assert.Equal(1, errs);
        var snsError = report.SelectSingleNode("//sns/error") as XmlElement;
        Assert.NotNull(snsError);
        Assert.Equal("systemCode", snsError!.SelectSingleNode("code")!.InnerText);
        Assert.Equal("99", snsError.SelectSingleNode("invalidValue")!.InnerText);
    }

    [Fact]
    public void Tool_Sns_ViolatingSystemCode_ExitsBrexError()
    {
        string objPath = WriteTemp(DmWithCode("99"));
        string brexPath = WriteTemp(SnsBrex);
        try
        {
            var (code, outText, _) = RunTool("-b", brexPath, "-S", "-x", objPath);
            Assert.Equal(1, code);
            Assert.Contains("systemCode", outText);
        }
        finally { File.Delete(objPath); File.Delete(brexPath); }
    }

    // ---- Notation rules -----------------------------------------------------

    // A BREX that disallows the "png" notation (allowedNotationFlag="0").
    private const string NotationBrex =
        """
        <dmodule>
          <content>
            <brex>
              <notationRuleList>
                <notationRule>
                  <notationName allowedNotationFlag="0">png</notationName>
                  <objectUse>The png notation is not allowed.</objectUse>
                </notationRule>
              </notationRuleList>
            </brex>
          </content>
        </dmodule>
        """;

    // A data module that declares and uses an unparsed entity with NDATA png.
    private const string DmUsingPngNotation =
        """
        <?xml version="1.0"?>
        <!DOCTYPE dmodule [
          <!NOTATION png SYSTEM "image/png">
          <!ENTITY icn-001 SYSTEM "icn-001.png" NDATA png>
        ]>
        <dmodule>
          <content><description><para>x</para></description></content>
        </dmodule>
        """;

    [Fact]
    public void Library_Notation_DisallowedNotation_Error()
    {
        int errs = BrexCheck.Check(Doc(DmUsingPngNotation), Doc(NotationBrex), BrexCheckOptions.Notations, out XmlDocument report);

        Assert.Equal(1, errs);
        var notationError = report.SelectSingleNode("//notations/error") as XmlElement;
        Assert.NotNull(notationError);
        Assert.Equal("png", notationError!.SelectSingleNode("invalidNotation")!.InnerText);
        Assert.Equal("The png notation is not allowed.", notationError.SelectSingleNode("objectUse")!.InnerText);
    }

    [Fact]
    public void Library_Notation_NoDtd_NoNotationsElement()
    {
        // A document with no internal DTD subset emits no <notations> element.
        int errs = BrexCheck.Check(Doc(ConformingObject), Doc(NotationBrex), BrexCheckOptions.Notations, out XmlDocument report);

        Assert.Equal(0, errs);
        Assert.Null(report.SelectSingleNode("//notations"));
    }

    [Fact]
    public void Tool_Notation_DisallowedNotation_ExitsBrexError()
    {
        string objPath = WriteTemp(DmUsingPngNotation);
        string brexPath = WriteTemp(NotationBrex);
        try
        {
            var (code, outText, _) = RunTool("-b", brexPath, "-n", "-x", objPath);
            Assert.Equal(1, code);
            Assert.Contains("invalidNotation", outText);
            Assert.Contains("png", outText);
        }
        finally { File.Delete(objPath); File.Delete(brexPath); }
    }

    // ---- Layered BREX -------------------------------------------------------

    [Fact]
    public void Tool_Layered_AppliesRulesFromReferencedBrex()
    {
        // Set up a temp dir with: an object referencing brexA, brexA referencing
        // brexB, and brexB containing the prohibiting rule. With -l, brexB's rule
        // must be applied to the object.
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-brex-layer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string cwd = Directory.GetCurrentDirectory();
        try
        {
            // brexB: DMC-LAYER-A-00-00-00-00A-022A-D, prohibits //prohibited.
            string brexBCode = "DMC-LAYER-A-00-00-00-00A-022A-D";
            string brexB =
                $"""
                <dmodule>
                  <identAndStatusSection><dmAddress><dmIdent>
                    <dmCode modelIdentCode="LAYER" systemDiffCode="A" systemCode="00"
                            subSystemCode="0" subSubSystemCode="0" assyCode="00"
                            disassyCode="00" disassyCodeVariant="A" infoCode="022"
                            infoCodeVariant="A" itemLocationCode="D"/>
                  </dmIdent></dmAddress></identAndStatusSection>
                  <content><brex>
                    <contextRules>
                      <structureObjectRule>
                        <brDecisionRef brDecisionIdentNumber="BREX-LAYER-B"/>
                        <objectPath allowedObjectFlag="0">//prohibited</objectPath>
                        <objectUse>No prohibited (layer B).</objectUse>
                      </structureObjectRule>
                    </contextRules>
                  </brex></content>
                </dmodule>
                """;
            File.WriteAllText(Path.Combine(dir, brexBCode + "_001-00_EN-US.XML"), brexB);

            // brexA: references brexB, no rules of its own.
            string brexACode = "DMC-LAYER-A-00-00-00-00A-022B-D";
            string brexA =
                $"""
                <dmodule>
                  <identAndStatusSection><dmAddress><dmIdent>
                    <dmCode modelIdentCode="LAYER" systemDiffCode="A" systemCode="00"
                            subSystemCode="0" subSubSystemCode="0" assyCode="00"
                            disassyCode="00" disassyCodeVariant="A" infoCode="022"
                            infoCodeVariant="B" itemLocationCode="D"/>
                  </dmIdent></dmAddress></identAndStatusSection>
                  <content><brex>
                    <brexDmRef><dmRef><dmRefIdent>
                      <dmCode modelIdentCode="LAYER" systemDiffCode="A" systemCode="00"
                              subSystemCode="0" subSubSystemCode="0" assyCode="00"
                              disassyCode="00" disassyCodeVariant="A" infoCode="022"
                              infoCodeVariant="A" itemLocationCode="D"/>
                    </dmRefIdent></dmRef></brexDmRef>
                    <contextRules/>
                  </brex></content>
                </dmodule>
                """;
            File.WriteAllText(Path.Combine(dir, brexACode + "_001-00_EN-US.XML"), brexA);

            // Object: violates the rule, references brexA.
            string obj =
                """
                <dmodule>
                  <identAndStatusSection><dmAddress><dmIdent>
                    <dmCode modelIdentCode="OBJ" systemDiffCode="A" systemCode="00"
                            subSystemCode="0" subSubSystemCode="0" assyCode="00"
                            disassyCode="00" disassyCodeVariant="A" infoCode="040"
                            infoCodeVariant="A" itemLocationCode="D"/>
                    <brexDmRef><dmRef><dmRefIdent>
                      <dmCode modelIdentCode="LAYER" systemDiffCode="A" systemCode="00"
                              subSystemCode="0" subSubSystemCode="0" assyCode="00"
                              disassyCode="00" disassyCodeVariant="A" infoCode="022"
                              infoCodeVariant="B" itemLocationCode="D"/>
                    </dmRefIdent></dmRef></brexDmRef>
                  </dmIdent></dmAddress></identAndStatusSection>
                  <content><description><prohibited>nope</prohibited></description></content>
                </dmodule>
                """;
            string objFile = Path.Combine(dir, "obj.XML");
            File.WriteAllText(objFile, obj);

            Directory.SetCurrentDirectory(dir);
            // Without -l, brexA has no rules, so no error.
            var (codeNoLayer, _, _) = RunTool("-d", dir, "-x", objFile);
            Assert.Equal(0, codeNoLayer);

            // With -l, brexB's rule is applied and the object fails.
            var (codeLayered, outText, _) = RunTool("-d", dir, "-l", "-x", objFile);
            Assert.Equal(1, codeLayered);
            Assert.Contains("BREX-LAYER-B", outText);
            Assert.Contains("layered=\"yes\"", outText);
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
            Directory.Delete(dir, recursive: true);
        }
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
