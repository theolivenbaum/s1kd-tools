using System.Xml;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class RefToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new RefTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static XmlElement ParseFirstElement(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml.Trim());
        return doc.DocumentElement!;
    }

    [Fact]
    public void Version_PrintsNameAndVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("ref", outText);
        Assert.Contains("3.8.1", outText);
    }

    [Fact]
    public void Help_ReturnsZero()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("Usage:", outText);
    }

    [Fact]
    public void DataModuleCode_ProducesDmRef()
    {
        var (code, outText, _) = Run("DMC-EX-A-00-00-00-00A-040A-D");
        Assert.Equal(0, code);

        var el = ParseFirstElement(outText);
        Assert.Equal("dmRef", el.Name);
        var dmCode = (XmlElement)el.SelectSingleNode("dmRefIdent/dmCode")!;
        Assert.Equal("EX", dmCode.GetAttribute("modelIdentCode"));
        Assert.Equal("A", dmCode.GetAttribute("systemDiffCode"));
        Assert.Equal("00", dmCode.GetAttribute("systemCode"));
        Assert.Equal("0", dmCode.GetAttribute("subSystemCode"));
        Assert.Equal("0", dmCode.GetAttribute("subSubSystemCode"));
        Assert.Equal("00", dmCode.GetAttribute("assyCode"));
        Assert.Equal("00", dmCode.GetAttribute("disassyCode"));
        Assert.Equal("A", dmCode.GetAttribute("disassyCodeVariant"));
        Assert.Equal("040", dmCode.GetAttribute("infoCode"));
        Assert.Equal("A", dmCode.GetAttribute("infoCodeVariant"));
        Assert.Equal("D", dmCode.GetAttribute("itemLocationCode"));
        Assert.Empty(dmCode.GetAttribute("learnCode"));
    }

    [Fact]
    public void DataModuleCode_WithLearn_ProducesLearnAttributes()
    {
        var (code, outText, _) = Run("DMC-EX-A-00-00-00-00A-040A-D-A-01");
        Assert.Equal(0, code);
        var dmCode = (XmlElement)ParseFirstElement(outText).SelectSingleNode("dmRefIdent/dmCode")!;
        Assert.Equal("A", dmCode.GetAttribute("learnCode"));
        Assert.Equal("01", dmCode.GetAttribute("learnEventCode"));
    }

    [Fact]
    public void DataModuleCode_WithIssueAndLang_FromCode()
    {
        var (code, outText, _) = Run("-il", "DMC-EX-A-00-00-00-00A-040A-D_001-03_EN-CA");
        Assert.Equal(0, code);
        var el = ParseFirstElement(outText);
        var issueInfo = (XmlElement)el.SelectSingleNode("dmRefIdent/issueInfo")!;
        Assert.Equal("001", issueInfo.GetAttribute("issueNumber"));
        Assert.Equal("03", issueInfo.GetAttribute("inWork"));
        var language = (XmlElement)el.SelectSingleNode("dmRefIdent/language")!;
        Assert.Equal("en", language.GetAttribute("languageIsoCode"));
        Assert.Equal("CA", language.GetAttribute("countryIsoCode"));
    }

    [Fact]
    public void ExtendedDataModuleCode_ProducesIdentExtension()
    {
        var (code, outText, _) = Run("DME-PROD-CODE-EX-A-00-00-00-00A-040A-D");
        Assert.Equal(0, code);
        var ext = (XmlElement)ParseFirstElement(outText).SelectSingleNode("dmRefIdent/identExtension")!;
        Assert.Equal("PROD", ext.GetAttribute("extensionProducer"));
        Assert.Equal("CODE", ext.GetAttribute("extensionCode"));
    }

    [Fact]
    public void CatalogSeqNumber_ProducesCatalogSeqNumberRef()
    {
        var (code, outText, _) = Run("CSN-EX-A-00-00-00-01A-004A-D");
        Assert.Equal(0, code);
        var el = ParseFirstElement(outText);
        Assert.Equal("catalogSeqNumberRef", el.Name);
        Assert.Equal("01", el.GetAttribute("figureNumber"));
        Assert.Equal("A", el.GetAttribute("figureNumberVariant"));
        Assert.Equal("004", el.GetAttribute("item"));
        Assert.Equal("A", el.GetAttribute("itemVariant"));
        Assert.Equal("D", el.GetAttribute("itemLocationCode"));
    }

    [Fact]
    public void Comment_ProducesCommentRef()
    {
        var (code, outText, _) = Run("COM-EX-12345-2018-00001-Q");
        Assert.Equal(0, code);
        var el = ParseFirstElement(outText);
        Assert.Equal("commentRef", el.Name);
        var cc = (XmlElement)el.SelectSingleNode("commentRefIdent/commentCode")!;
        Assert.Equal("EX", cc.GetAttribute("modelIdentCode"));
        Assert.Equal("12345", cc.GetAttribute("senderIdent"));
        Assert.Equal("2018", cc.GetAttribute("yearOfDataIssue"));
        Assert.Equal("00001", cc.GetAttribute("seqNumber"));
        Assert.Equal("q", cc.GetAttribute("commentType"));
    }

    [Fact]
    public void Dml_ProducesDmlRef()
    {
        var (code, outText, _) = Run("DML-EX-12345-C-2018-00001");
        Assert.Equal(0, code);
        var dc = (XmlElement)ParseFirstElement(outText).SelectSingleNode("dmlRefIdent/dmlCode")!;
        Assert.Equal("c", dc.GetAttribute("dmlType"));
        Assert.Equal("2018", dc.GetAttribute("yearOfDataIssue"));
        Assert.Equal("00001", dc.GetAttribute("seqNumber"));
    }

    [Fact]
    public void Icn_ProducesInfoEntityRef()
    {
        var (code, outText, _) = Run("ICN-EX-A-000000-A-00001-A-001-01");
        Assert.Equal(0, code);
        var el = ParseFirstElement(outText);
        Assert.Equal("infoEntityRef", el.Name);
        Assert.Equal("ICN-EX-A-000000-A-00001-A-001-01", el.GetAttribute("infoEntityRefIdent"));
    }

    [Fact]
    public void PublicationModuleCode_ProducesPmRef()
    {
        var (code, outText, _) = Run("PMC-EX-12345-00001-00");
        Assert.Equal(0, code);
        var pc = (XmlElement)ParseFirstElement(outText).SelectSingleNode("pmRefIdent/pmCode")!;
        Assert.Equal("EX", pc.GetAttribute("modelIdentCode"));
        Assert.Equal("12345", pc.GetAttribute("pmIssuer"));
        Assert.Equal("00001", pc.GetAttribute("pmNumber"));
        Assert.Equal("00", pc.GetAttribute("pmVolume"));
    }

    [Fact]
    public void ScormContentPackageCode_ProducesScormRef()
    {
        var (code, outText, _) = Run("SMC-EX-12345-00001-00");
        Assert.Equal(0, code);
        var el = ParseFirstElement(outText);
        Assert.Equal("scormContentPackageRef", el.Name);
        var sc = (XmlElement)el.SelectSingleNode("scormContentPackageRefIdent/scormContentPackageCode")!;
        Assert.Equal("EX", sc.GetAttribute("modelIdentCode"));
        Assert.Equal("12345", sc.GetAttribute("scormContentPackageIssuer"));
    }

    [Fact]
    public void UnknownCode_ProducesExternalPubRef()
    {
        var (code, outText, _) = Run("ABC");
        Assert.Equal(0, code);
        var el = ParseFirstElement(outText);
        Assert.Equal("externalPubRef", el.Name);
        Assert.Equal("ABC", el.SelectSingleNode("externalPubRefIdent/externalPubCode")!.InnerText);
    }

    [Fact]
    public void GuessPrefix_AddsDmcPrefix()
    {
        var (code, outText, _) = Run("-g", "EX-A-00-00-00-00A-040A-D");
        Assert.Equal(0, code);
        Assert.Equal("dmRef", ParseFirstElement(outText).Name);
    }

    [Fact]
    public void SourceId_FromFile_ProducesSourceDmIdent()
    {
        string path = WriteDataModule();
        try
        {
            var (code, outText, _) = Run("-S", path);
            Assert.Equal(0, code);
            var el = ParseFirstElement(outText);
            Assert.Equal("sourceDmIdent", el.Name);
            // dmCode, then language, then issueInfo (matches C ordering).
            Assert.NotNull(el.SelectSingleNode("dmCode"));
            var lang = (XmlElement)el.SelectSingleNode("language")!;
            Assert.Equal("en", lang.GetAttribute("languageIsoCode"));
            var ii = (XmlElement)el.SelectSingleNode("issueInfo")!;
            Assert.Equal("002", ii.GetAttribute("issueNumber"));
            Assert.Equal("01", ii.GetAttribute("inWork"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RepositoryId_FromFile_ProducesRepositorySourceDmIdent()
    {
        string path = WriteDataModule();
        try
        {
            var (code, outText, _) = Run("-R", path);
            Assert.Equal(0, code);
            Assert.Equal("repositorySourceDmIdent", ParseFirstElement(outText).Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TitleAndDate_FromFile()
    {
        string path = WriteDataModule();
        try
        {
            var (code, outText, _) = Run("-dt", path);
            Assert.Equal(0, code);
            var el = ParseFirstElement(outText);
            var items = el.SelectSingleNode("dmRefAddressItems")!;
            Assert.Equal("Example", items.SelectSingleNode("dmTitle/techName")!.InnerText);
            Assert.Equal("Description", items.SelectSingleNode("dmTitle/infoName")!.InnerText);
            var date = (XmlElement)items.SelectSingleNode("issueDate")!;
            Assert.Equal("2026", date.GetAttribute("year"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Insert_AddsRefToContentRefsTable()
    {
        string path = WriteDataModule();
        try
        {
            var (code, outText, _) = Run("-r", "-s", path, "DMC-EX-A-00-00-00-00A-040A-D");
            Assert.Equal(0, code);
            var doc = new XmlDocument();
            doc.LoadXml(outText.Trim());
            var refNode = doc.SelectSingleNode("//content/refs/dmRef");
            Assert.NotNull(refNode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IssueDowngrade_To40_RemovesInfoNameVariant()
    {
        // to40.xsl folds infoNameVariant into infoName; smoke-test that downgrade runs.
        var (code, outText, _) = Run("-$", "4.0", "DMC-EX-A-00-00-00-00A-040A-D");
        Assert.Equal(0, code);
        Assert.Equal("dmRef", ParseFirstElement(outText).Name);
    }

    [Fact]
    public void Transform_ConvertsTextualDmRef()
    {
        string xml =
            "<dmodule><content><description><para>See DMC-EX-A-00-00-00-00A-040A-D for details.</para></description></content></dmodule>";
        string path = WriteTemp(xml);
        try
        {
            var (code, outText, _) = Run("-T", "D", path);
            Assert.Equal(0, code);
            var doc = new XmlDocument();
            doc.LoadXml(outText.Trim());
            var dmRef = doc.SelectSingleNode("//para/dmRef");
            Assert.NotNull(dmRef);
            Assert.Contains("See ", doc.SelectSingleNode("//para")!.InnerXml);
            Assert.Contains(" for details.", doc.SelectSingleNode("//para")!.InnerText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BadInput_ReturnsExitCode2()
    {
        var (code, _, _) = Run("DMC-INVALID");
        Assert.Equal(2, code);
    }

    [Fact]
    public void BadIssue_ReturnsExitCode3()
    {
        var (code, _, _) = Run("-$", "9.9", "DMC-EX-A-00-00-00-00A-040A-D");
        Assert.Equal(3, code);
    }

    [Fact]
    public void MissingSource_ReturnsExitCode1()
    {
        var (code, _, _) = Run("-r", "-s", "/no/such/file.XML", "DMC-EX-A-00-00-00-00A-040A-D");
        Assert.Equal(1, code);
    }

    private static string WriteDataModule() => WriteTemp(Fixtures.DataModule);

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-ref-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, content);
        return path;
    }
}
