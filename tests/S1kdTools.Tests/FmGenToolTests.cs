using System.Xml;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class FmGenToolTests
{
    // A minimal publication module referencing two data modules. Enough for the
    // title-page (TITLE) and list-of-effective-data-modules (LOEDM) stylesheets.
    private const string Pm = """
<?xml version="1.0" encoding="UTF-8"?>
<pm>
  <identAndStatusSection>
    <pmAddress>
      <pmIdent>
        <pmCode modelIdentCode="TEST" pmIssuer="12345" pmNumber="00001" pmVolume="00"/>
        <language languageIsoCode="en" countryIsoCode="CA"/>
        <issueInfo issueNumber="001" inWork="00"/>
      </pmIdent>
      <pmAddressItems>
        <issueDate year="2019" month="10" day="21"/>
        <pmTitle>Example PM</pmTitle>
      </pmAddressItems>
    </pmAddress>
    <pmStatus issueType="new">
      <security securityClassification="01"/>
      <responsiblePartnerCompany>
        <enterpriseName>khzae.net</enterpriseName>
      </responsiblePartnerCompany>
      <applic>
        <displayText>
          <simplePara>All</simplePara>
        </displayText>
      </applic>
    </pmStatus>
  </identAndStatusSection>
  <content>
    <pmEntry>
      <dmRef>
        <dmRefIdent>
          <dmCode modelIdentCode="TEST" systemDiffCode="A" systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00" disassyCode="00" disassyCodeVariant="A" infoCode="040" infoCodeVariant="A" itemLocationCode="D"/>
          <issueInfo issueNumber="001" inWork="00"/>
          <language languageIsoCode="en" countryIsoCode="CA"/>
        </dmRefIdent>
        <dmRefAddressItems>
          <dmTitle>
            <techName>DM 1</techName>
          </dmTitle>
          <issueDate year="2019" month="10" day="21"/>
        </dmRefAddressItems>
      </dmRef>
    </pmEntry>
  </content>
</pm>
""";

    // A front matter data module whose <content> will be replaced. Its info code
    // (00S) maps to LOEDM in the built-in .fmtypes table; 001 maps to TITLE.
    private static string FmDm(string infoCode) => $"""
<?xml version="1.0" encoding="UTF-8"?>
<dmodule>
  <identAndStatusSection>
    <dmAddress>
      <dmIdent>
        <dmCode modelIdentCode="TEST" systemDiffCode="A" systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00" disassyCode="00" disassyCodeVariant="A" infoCode="{infoCode}" infoCodeVariant="A" itemLocationCode="D"/>
        <language languageIsoCode="en" countryIsoCode="CA"/>
        <issueInfo issueNumber="001" inWork="00"/>
      </dmIdent>
      <dmAddressItems>
        <issueDate year="2019" month="10" day="21"/>
        <dmTitle>
          <techName>Test</techName>
          <infoName>Front matter</infoName>
        </dmTitle>
      </dmAddressItems>
    </dmAddress>
    <dmStatus issueType="new">
      <security securityClassification="01"/>
    </dmStatus>
  </identAndStatusSection>
  <content>
    <frontMatter/>
  </content>
</dmodule>
""";

    private static (int code, string outText, string errText) Run(string pmPath, params string[] args)
    {
        var tool = new FmGenTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var all = new List<string>(args);
        // -P comes first so positional file args are not confused with it.
        all.Insert(0, pmPath);
        all.Insert(0, "-P");
        int code = tool.Run(all, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteTemp(string content, string suffix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-fmgen-{Guid.NewGuid():N}{suffix}");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Title_GeneratesTitlePageAndMergesContent()
    {
        string pm = WriteTemp(Pm, ".XML");
        string dm = WriteTemp(FmDm("001"), ".XML");
        try
        {
            var (code, outText, errText) = Run(pm, "-t", "TITLE", dm);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, errText);

            var doc = new XmlDocument();
            doc.LoadXml(outText);

            // The DM's <content> was replaced by the generated title page.
            XmlNode? tp = doc.SelectSingleNode("//frontMatterTitlePage");
            Assert.NotNull(tp);
            Assert.Equal("Example PM", doc.SelectSingleNode("//frontMatterTitlePage/pmTitle")?.InnerText);
            Assert.NotNull(doc.SelectSingleNode("//frontMatterTitlePage/pmCode"));
            // Title page must still be under the DM's identAndStatusSection sibling.
            Assert.NotNull(doc.SelectSingleNode("/dmodule/content/frontMatter/frontMatterTitlePage"));
        }
        finally
        {
            File.Delete(pm);
            File.Delete(dm);
        }
    }

    [Fact]
    public void Loedm_GeneratesListOfEffectiveDataModules()
    {
        string pm = WriteTemp(Pm, ".XML");
        string dm = WriteTemp(FmDm("00S"), ".XML");
        try
        {
            // No -t: type is resolved from the DM's info code via built-in .fmtypes.
            var (code, outText, errText) = Run(pm, dm);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, errText);

            var doc = new XmlDocument();
            doc.LoadXml(outText);

            XmlNode? list = doc.SelectSingleNode("//frontMatterList[@frontMatterType='fm02']");
            Assert.NotNull(list);
            // The single referenced DM (040A) appears as an entry.
            XmlNodeList? entries = doc.SelectNodes("//frontMatterDmEntry");
            Assert.NotNull(entries);
            Assert.Equal(1, entries!.Count);
            Assert.Equal("DM 1", doc.SelectSingleNode("//frontMatterDmEntry//techName")?.InnerText);
        }
        finally
        {
            File.Delete(pm);
            File.Delete(dm);
        }
    }

    [Fact]
    public void Overwrite_WritesBackToDataModule()
    {
        string pm = WriteTemp(Pm, ".XML");
        string dm = WriteTemp(FmDm("00S"), ".XML");
        try
        {
            var (code, _, _) = Run(pm, "-f", dm);
            Assert.Equal(0, code);

            var doc = new XmlDocument();
            doc.Load(dm);
            Assert.NotNull(doc.SelectSingleNode("//frontMatterList[@frontMatterType='fm02']"));
        }
        finally
        {
            File.Delete(pm);
            File.Delete(dm);
        }
    }

    [Fact]
    public void IssueDate_OverridesGeneratedDate()
    {
        string pm = WriteTemp(Pm, ".XML");
        string dm = WriteTemp(FmDm("00S"), ".XML");
        try
        {
            var (code, outText, _) = Run(pm, "-I", "2021-01-02", dm);
            Assert.Equal(0, code);

            var doc = new XmlDocument();
            doc.LoadXml(outText);
            var date = (XmlElement?)doc.SelectSingleNode("//issueDate");
            Assert.NotNull(date);
            Assert.Equal("2021", date!.GetAttribute("year"));
            Assert.Equal("01", date.GetAttribute("month"));
            Assert.Equal("02", date.GetAttribute("day"));
        }
        finally
        {
            File.Delete(pm);
            File.Delete(dm);
        }
    }

    [Fact]
    public void BadDate_ReturnsExitCode1()
    {
        string pm = WriteTemp(Pm, ".XML");
        string dm = WriteTemp(FmDm("00S"), ".XML");
        try
        {
            var (code, _, _) = Run(pm, "-I", "notadate", dm);
            Assert.Equal(1, code);
        }
        finally
        {
            File.Delete(pm);
            File.Delete(dm);
        }
    }

    [Fact]
    public void UnknownType_ReturnsExitCode3()
    {
        string pm = WriteTemp(Pm, ".XML");
        string dm = WriteTemp(FmDm("001"), ".XML");
        try
        {
            var (code, _, _) = Run(pm, "-t", "BOGUS", dm);
            Assert.Equal(3, code);
        }
        finally
        {
            File.Delete(pm);
            File.Delete(dm);
        }
    }

    [Fact]
    public void NoType_NoFiles_ReturnsExitCode2()
    {
        string pm = WriteTemp(Pm, ".XML");
        try
        {
            var (code, _, _) = Run(pm);
            Assert.Equal(2, code);
        }
        finally
        {
            File.Delete(pm);
        }
    }

    [Fact]
    public void DumpFmtypesXml_EmitsTable()
    {
        var tool = new FmGenTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "--dump-fmtypes-xml" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("infoCode=\"001\"", stdout.ToString());
        Assert.Contains("TITLE", stdout.ToString());
    }

    [Fact]
    public void DumpXsl_TitleEmitsStylesheet()
    {
        var tool = new FmGenTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "-D", "TITLE" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("frontMatterTitlePage", stdout.ToString());
    }

    [Fact]
    public void XProc_MultiPassPipeline_AppliesEachStepInOrder()
    {
        // A pipeline mirroring the multipass example: pass 1 builds the title page
        // from the PM, pass 2 (a referenced stylesheet) overrides the title via a
        // p:with-param, and pass 3 (an inline stylesheet) inserts a comment.
        string pass1 = """
<?xml version="1.0"?>
<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
  <xsl:template match="@*|node()">
    <xsl:copy><xsl:apply-templates select="@*|node()"/></xsl:copy>
  </xsl:template>
  <xsl:template match="pm">
    <content><frontMatter>
      <frontMatterTitlePage>
        <pmTitle><xsl:value-of select=".//pmTitle"/></pmTitle>
      </frontMatterTitlePage>
    </frontMatter></content>
  </xsl:template>
</xsl:stylesheet>
""";
        string pass2 = """
<?xml version="1.0"?>
<xsl:transform xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
  <xsl:param name="title"/>
  <xsl:template match="@*|node()">
    <xsl:copy><xsl:apply-templates select="@*|node()"/></xsl:copy>
  </xsl:template>
  <xsl:template match="pmTitle">
    <xsl:copy><xsl:value-of select="$title"/></xsl:copy>
  </xsl:template>
</xsl:transform>
""";

        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-fmgen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string pass1Path = Path.Combine(dir, "pass1.xsl");
        string pass2Path = Path.Combine(dir, "pass2.xsl");
        File.WriteAllText(pass1Path, pass1);
        File.WriteAllText(pass2Path, pass2);

        string pipeline = """
<?xml version="1.0"?>
<p:pipeline xmlns:p="http://www.w3.org/ns/xproc"
            xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
  <p:xslt name="Pass 1">
    <p:input port="stylesheet"><p:document href="pass1.xsl"/></p:input>
  </p:xslt>
  <p:xslt name="Pass 2">
    <p:input port="stylesheet"><p:document href="pass2.xsl"/></p:input>
    <p:with-param name="title" select="'Alternative title'"/>
  </p:xslt>
  <p:xslt name="Pass 3">
    <p:input port="stylesheet">
      <p:inline>
        <xsl:stylesheet version="1.0">
          <xsl:template match="@*|node()">
            <xsl:copy><xsl:apply-templates select="@*|node()"/></xsl:copy>
          </xsl:template>
          <xsl:template match="frontMatterTitlePage">
            <xsl:comment>This was inserted by the third pass.</xsl:comment>
            <xsl:copy><xsl:apply-templates select="@*|node()"/></xsl:copy>
          </xsl:template>
        </xsl:stylesheet>
      </p:inline>
    </p:input>
  </p:xslt>
</p:pipeline>
""";
        string pipelinePath = Path.Combine(dir, "titlepage.xpl");
        File.WriteAllText(pipelinePath, pipeline);

        string pm = WriteTemp(Pm, ".XML");
        string dm = WriteTemp(FmDm("001"), ".XML");
        try
        {
            var (code, outText, errText) = Run(pm, "-t", "TITLE", "-x", pipelinePath, dm);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, errText);

            var doc = new XmlDocument();
            doc.LoadXml(outText);

            // Pass 1 built the title page; pass 2 overrode the title; pass 3 added a comment.
            Assert.Equal("Alternative title",
                doc.SelectSingleNode("//frontMatterTitlePage/pmTitle")?.InnerText);
            XmlNode? tp = doc.SelectSingleNode("//frontMatterTitlePage");
            Assert.NotNull(tp);
            XmlNode? comment = tp!.PreviousSibling;
            Assert.NotNull(comment);
            Assert.Equal(XmlNodeType.Comment, comment!.NodeType);
            Assert.Contains("third pass", comment.Value);
        }
        finally
        {
            File.Delete(pm);
            File.Delete(dm);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void XProc_UserParamOverridesPipelineWithParam()
    {
        // The pipeline sets title='Pipeline title' but the user passes -p title; the
        // user value must win (mirrors has_param in apply_xproc_xslt).
        string pipeline = """
<?xml version="1.0"?>
<p:pipeline xmlns:p="http://www.w3.org/ns/xproc"
            xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
  <p:xslt name="Pass 1">
    <p:input port="stylesheet">
      <p:inline>
        <xsl:stylesheet version="1.0">
          <xsl:param name="title"/>
          <xsl:template match="pm">
            <content><frontMatter><frontMatterTitlePage>
              <pmTitle><xsl:value-of select="$title"/></pmTitle>
            </frontMatterTitlePage></frontMatter></content>
          </xsl:template>
        </xsl:stylesheet>
      </p:inline>
    </p:input>
    <p:with-param name="title" select="'Pipeline title'"/>
  </p:xslt>
</p:pipeline>
""";
        string pipelinePath = WriteTemp(pipeline, ".xpl");
        string pm = WriteTemp(Pm, ".XML");
        string dm = WriteTemp(FmDm("001"), ".XML");
        try
        {
            var (code, outText, errText) = Run(pm, "-t", "TITLE", "-x", pipelinePath, "-p", "title=User title", dm);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, errText);

            var doc = new XmlDocument();
            doc.LoadXml(outText);
            Assert.Equal("User title", doc.SelectSingleNode("//frontMatterTitlePage/pmTitle")?.InnerText);
        }
        finally
        {
            File.Delete(pipelinePath);
            File.Delete(pm);
            File.Delete(dm);
        }
    }

    [Fact]
    public void Version_PrintsToolName()
    {
        var tool = new FmGenTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "--version" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("s1kd-fmgen", stdout.ToString());
        Assert.Contains("4.0.0", stdout.ToString());
    }
}
