using System.Xml;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class MvrefToolTests
{
    // Source data module: DMC EX-A-00-00-00-00A-040A-D
    private const string SourceDm =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="040"
                        infoCodeVariant="A" itemLocationCode="D"/>
                <issueInfo issueNumber="001" inWork="00"/>
                <language languageIsoCode="en" countryIsoCode="CA"/>
              </dmIdent>
              <dmAddressItems>
                <issueDate year="2020" month="01" day="01"/>
                <dmTitle>
                  <techName>Old Source</techName>
                  <infoName>Description</infoName>
                </dmTitle>
              </dmAddressItems>
            </dmAddress>
          </identAndStatusSection>
          <content><description><para>Source.</para></description></content>
        </dmodule>
        """;

    // Target data module: DMC EX-A-00-00-00-00A-040A-E (differs by itemLocationCode)
    private const string TargetDm =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="040"
                        infoCodeVariant="A" itemLocationCode="E"/>
                <issueInfo issueNumber="003" inWork="00"/>
                <language languageIsoCode="fr" countryIsoCode="FR"/>
              </dmIdent>
              <dmAddressItems>
                <issueDate year="2026" month="06" day="25"/>
                <dmTitle>
                  <techName>New Target</techName>
                  <infoName>Description</infoName>
                </dmTitle>
              </dmAddressItems>
            </dmAddress>
          </identAndStatusSection>
          <content><description><para>Target.</para></description></content>
        </dmodule>
        """;

    // An object containing a dmRef to the source DM (matching code, with issue/lang/title).
    private const string ReferringDm =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="11"
                        subSystemCode="1" subSubSystemCode="1" assyCode="11"
                        disassyCode="11" disassyCodeVariant="A" infoCode="000"
                        infoCodeVariant="A" itemLocationCode="A"/>
                <issueInfo issueNumber="001" inWork="00"/>
                <language languageIsoCode="en" countryIsoCode="CA"/>
              </dmIdent>
            </dmAddress>
          </identAndStatusSection>
          <content>
            <description>
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
                    <techName>Old Source</techName>
                    <infoName>Description</infoName>
                  </dmTitle>
                  <issueDate year="2020" month="01" day="01"/>
                </dmRefAddressItems>
              </dmRef>
            </description>
          </content>
        </dmodule>
        """;

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-mvref-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, content);
        return path;
    }

    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new MvrefTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Version_PrintsVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("2.6.0", outText);
        Assert.Contains("s1kd-mvref", outText);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("--source", outText);
        Assert.Contains("--target", outText);
    }

    [Fact]
    public void Recode_ToStdout_RewritesMatchingRef()
    {
        string src = WriteTemp(SourceDm);
        string tgt = WriteTemp(TargetDm);
        string obj = WriteTemp(ReferringDm);
        try
        {
            var (code, outText, _) = Run("-s", src, "-t", tgt, obj);
            Assert.Equal(0, code);

            var doc = new XmlDocument();
            doc.LoadXml(outText);

            // The referenced dmCode should now point at the target (itemLocationCode E).
            var refCode = doc.SelectSingleNode("//dmRef/dmRefIdent/dmCode") as XmlElement;
            Assert.NotNull(refCode);
            Assert.Equal("E", refCode!.GetAttribute("itemLocationCode"));

            // Issue info recoded from the target.
            var issue = doc.SelectSingleNode("//dmRef/dmRefIdent/issueInfo") as XmlElement;
            Assert.NotNull(issue);
            Assert.Equal("003", issue!.GetAttribute("issueNumber"));

            // Language recoded from the target.
            var lang = doc.SelectSingleNode("//dmRef/dmRefIdent/language") as XmlElement;
            Assert.NotNull(lang);
            Assert.Equal("fr", lang!.GetAttribute("languageIsoCode"));

            // Title and issue date recoded.
            Assert.Equal("New Target", doc.SelectSingleNode("//dmRef/dmRefAddressItems/dmTitle/techName")!.InnerText);
            var refDate = doc.SelectSingleNode("//dmRef/dmRefAddressItems/issueDate") as XmlElement;
            Assert.Equal("2026", refDate!.GetAttribute("year"));
        }
        finally
        {
            File.Delete(src);
            File.Delete(tgt);
            File.Delete(obj);
        }
    }

    [Fact]
    public void Recode_WithOverwrite_PersistsChange()
    {
        string src = WriteTemp(SourceDm);
        string tgt = WriteTemp(TargetDm);
        string obj = WriteTemp(ReferringDm);
        try
        {
            var (code, _, _) = Run("-f", "-s", src, "-t", tgt, obj);
            Assert.Equal(0, code);

            var doc = new XmlDocument();
            doc.Load(obj);
            var refCode = doc.SelectSingleNode("//dmRef/dmRefIdent/dmCode") as XmlElement;
            Assert.Equal("E", refCode!.GetAttribute("itemLocationCode"));
        }
        finally
        {
            File.Delete(src);
            File.Delete(tgt);
            File.Delete(obj);
        }
    }

    [Fact]
    public void NonMatchingRef_IsLeftUnchanged()
    {
        // Source whose code matches nothing in the referring DM.
        const string unrelatedSource = """
            <dmodule><identAndStatusSection><dmAddress><dmIdent>
              <dmCode modelIdentCode="ZZ" systemDiffCode="A" systemCode="99"
                      subSystemCode="9" subSubSystemCode="9" assyCode="99"
                      disassyCode="99" disassyCodeVariant="Z" infoCode="999"
                      infoCodeVariant="Z" itemLocationCode="Z"/>
              <issueInfo issueNumber="001" inWork="00"/>
              <language languageIsoCode="en" countryIsoCode="CA"/>
            </dmIdent></dmAddress></identAndStatusSection>
            <content><description><para>X.</para></description></content></dmodule>
            """;
        string src = WriteTemp(unrelatedSource);
        string tgt = WriteTemp(TargetDm);
        string obj = WriteTemp(ReferringDm);
        try
        {
            var (code, outText, _) = Run("-s", src, "-t", tgt, obj);
            Assert.Equal(0, code);

            var doc = new XmlDocument();
            doc.LoadXml(outText);
            var refCode = doc.SelectSingleNode("//dmRef/dmRefIdent/dmCode") as XmlElement;
            // Unchanged: still itemLocationCode D.
            Assert.Equal("D", refCode!.GetAttribute("itemLocationCode"));
        }
        finally
        {
            File.Delete(src);
            File.Delete(tgt);
            File.Delete(obj);
        }
    }

    [Fact]
    public void TargetWithoutSource_ReturnsExit2()
    {
        string tgt = WriteTemp(TargetDm);
        try
        {
            var (code, _, errText) = Run("-t", tgt);
            Assert.Equal(2, code);
            Assert.Contains("Source object must be specified", errText);
        }
        finally
        {
            File.Delete(tgt);
        }
    }

    [Fact]
    public void Directory_DoesNotExist_ReturnsExit2()
    {
        var (code, _, _) = Run("-d", Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}"));
        Assert.Equal(2, code);
    }
}
