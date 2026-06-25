using System.Xml;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NewdmToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new NewdmTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private const string FullCode =
        "DMC-S1KDTOOLS-A-21-00-00-00A-040A-D";

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-newdm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Version_PrintsToolNameAndVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("newdm", outText);
        Assert.Contains("5.0.1", outText);
    }

    [Fact]
    public void Help_ListsKeyOptions()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("--issue", outText);
        Assert.Contains("--code", outText);
        Assert.Contains("--type", outText);
    }

    [Fact]
    public void GeneratesExpectedFileNameAndMetadata()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, err) = Run(
                "-#", FullCode,
                "-T", "descript",
                "-L", "en", "-C", "US",
                "-n", "001", "-w", "02",
                "-t", "My tech name",
                "-i", "My info name",
                "-@", dir);

            Assert.Equal(0, code);
            Assert.True(string.IsNullOrEmpty(err), $"stderr: {err}");

            // Expected file name: DMC-<code stripped of DMC->_NNN-NN_LANG-COUNTRY.XML
            string expected = "DMC-S1KDTOOLS-A-21-00-00-00A-040A-D_001-02_EN-US.XML";
            string path = Path.Combine(dir, expected);
            Assert.True(File.Exists(path), $"expected {path}");

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(path);

            var dmCode = (XmlElement)doc.SelectSingleNode("//dmIdent/dmCode")!;
            Assert.Equal("S1KDTOOLS", dmCode.GetAttribute("modelIdentCode"));
            Assert.Equal("A", dmCode.GetAttribute("systemDiffCode"));
            Assert.Equal("21", dmCode.GetAttribute("systemCode"));
            Assert.Equal("0", dmCode.GetAttribute("subSystemCode"));
            Assert.Equal("0", dmCode.GetAttribute("subSubSystemCode"));
            Assert.Equal("00", dmCode.GetAttribute("assyCode"));
            Assert.Equal("00", dmCode.GetAttribute("disassyCode"));
            Assert.Equal("A", dmCode.GetAttribute("disassyCodeVariant"));
            Assert.Equal("040", dmCode.GetAttribute("infoCode"));
            Assert.Equal("A", dmCode.GetAttribute("infoCodeVariant"));
            Assert.Equal("D", dmCode.GetAttribute("itemLocationCode"));

            var lang = (XmlElement)doc.SelectSingleNode("//dmIdent/language")!;
            Assert.Equal("en", lang.GetAttribute("languageIsoCode"));
            Assert.Equal("US", lang.GetAttribute("countryIsoCode"));

            var issueInfo = (XmlElement)doc.SelectSingleNode("//dmIdent/issueInfo")!;
            Assert.Equal("001", issueInfo.GetAttribute("issueNumber"));
            Assert.Equal("02", issueInfo.GetAttribute("inWork"));

            Assert.Equal("My tech name", doc.SelectSingleNode("//dmTitle/techName")!.InnerText);
            Assert.Equal("My info name", doc.SelectSingleNode("//dmTitle/infoName")!.InnerText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OmitIssue_DropsIssueFromFileName()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run("-#", FullCode, "-T", "descript", "-N",
                "-L", "en", "-C", "US", "-@", dir);
            Assert.Equal(0, code);

            string expected = "DMC-S1KDTOOLS-A-21-00-00-00A-040A-D_EN-US.XML";
            Assert.True(File.Exists(Path.Combine(dir, expected)), $"expected {expected}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NoInfoName_RemovesInfoNameElement()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run("-#", FullCode, "-T", "descript", "-!",
                "-L", "en", "-C", "US", "-@", dir);
            Assert.Equal(0, code);

            string path = Directory.GetFiles(dir, "*.XML").Single();
            var doc = new XmlDocument();
            doc.Load(path);
            Assert.Null(doc.SelectSingleNode("//dmTitle/infoName"));
            // infoNameVariant is also removed when not set.
            Assert.Null(doc.SelectSingleNode("//dmTitle/infoNameVariant"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SecurityAndEnterprises_AreSet()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US",
                "-c", "03",
                "-r", "Some RPC", "-R", "U8025",
                "-o", "Some Orig", "-O", "12345",
                "-@", dir);
            Assert.Equal(0, code);

            string path = Directory.GetFiles(dir, "*.XML").Single();
            var doc = new XmlDocument();
            doc.Load(path);

            Assert.Equal("03", ((XmlElement)doc.SelectSingleNode("//security")!).GetAttribute("securityClassification"));

            var rpc = (XmlElement)doc.SelectSingleNode("//responsiblePartnerCompany")!;
            Assert.Equal("U8025", rpc.GetAttribute("enterpriseCode"));
            Assert.Equal("Some RPC", doc.SelectSingleNode("//responsiblePartnerCompany/enterpriseName")!.InnerText);

            var orig = (XmlElement)doc.SelectSingleNode("//originator")!;
            Assert.Equal("12345", orig.GetAttribute("enterpriseCode"));
            Assert.Equal("Some Orig", doc.SelectSingleNode("//originator/enterpriseName")!.InnerText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IssueDate_IsApplied()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-I", "2020-06-15", "-@", dir);
            Assert.Equal(0, code);

            string path = Directory.GetFiles(dir, "*.XML").Single();
            var doc = new XmlDocument();
            doc.Load(path);
            var issueDate = (XmlElement)doc.SelectSingleNode("//issueDate")!;
            Assert.Equal("2020", issueDate.GetAttribute("year"));
            Assert.Equal("06", issueDate.GetAttribute("month"));
            Assert.Equal("15", issueDate.GetAttribute("day"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DefaultIssueIsSix_DescriptUsesIssue6Schema()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-@", dir);
            Assert.Equal(0, code);

            string path = Directory.GetFiles(dir, "*.XML").Single();
            string text = File.ReadAllText(path);
            Assert.Contains("S1000D_6", text);
            // Issue 6 keeps the modern element names (no downgrade to <idstatus>).
            Assert.Contains("identAndStatusSection", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Downgrade_ToIssue30_RenamesElements()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, err) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-$", "3.0", "-@", dir);
            Assert.Equal(0, code);
            Assert.True(string.IsNullOrEmpty(err), $"stderr: {err}");

            string path = Directory.GetFiles(dir, "*.XML").Single();
            string text = File.ReadAllText(path);
            // The 3.0 downgrade XSLT renames identAndStatusSection -> idstatus.
            Assert.Contains("<idstatus", text);
            Assert.Contains("S1000D_3-0", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DumpDmTypesXml_PrintsBuiltinData()
    {
        var (code, outText, _) = Run("--dump-dmtypes-xml");
        Assert.Equal(0, code);
        Assert.Contains("<dmtypes>", outText);
        Assert.Contains("infoCode=\"000\"", outText);
    }

    [Fact]
    public void DumpTemplates_WritesAllTemplates()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run("-~", dir);
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "descript.xml")));
            Assert.True(File.Exists(Path.Combine(dir, "proced.xml")));
            Assert.True(File.Exists(Path.Combine(dir, "brex.xml")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DmTypesLookup_FillsSchemaAndInfoNameFromInfoCode()
    {
        // No -T: the schema and info name should be resolved from the built-in
        // dmtypes using the info code (040 -> descript / "Function").
        string dir = NewTempDir();
        try
        {
            var (code, _, err) = Run("-#", FullCode,
                "-L", "en", "-C", "US", "-@", dir);
            Assert.Equal(0, code);
            Assert.True(string.IsNullOrEmpty(err), $"stderr: {err}");

            string path = Directory.GetFiles(dir, "*.XML").Single();
            string text = File.ReadAllText(path);
            // descript schema selected via dmtypes lookup.
            Assert.Contains("descript.xsd", text);
            // An info name was filled in from dmtypes for info code 040.
            var doc = new XmlDocument();
            doc.Load(path);
            var infoName = doc.SelectSingleNode("//dmTitle/infoName");
            Assert.NotNull(infoName);
            Assert.False(string.IsNullOrEmpty(infoName!.InnerText));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BadDmCode_ReturnsExit3()
    {
        var (code, _, err) = Run("-#", "DMC-TOO-SHORT", "-T", "descript");
        Assert.Equal(3, code);
        Assert.Contains("Bad data module code", err);
    }

    [Fact]
    public void ExistingFileWithoutForce_ReturnsExit1()
    {
        string dir = NewTempDir();
        try
        {
            var (code1, _, _) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-@", dir);
            Assert.Equal(0, code1);

            var (code2, _, err) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-@", dir);
            Assert.Equal(1, code2);
            Assert.Contains("already exists", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExistingFileWithForce_Overwrites()
    {
        string dir = NewTempDir();
        try
        {
            var (code1, _, _) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-@", dir);
            Assert.Equal(0, code1);

            var (code2, _, _) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-f", "-@", dir);
            Assert.Equal(0, code2);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExistingFileWithQuiet_ReturnsZero()
    {
        string dir = NewTempDir();
        try
        {
            var (code1, _, _) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-@", dir);
            Assert.Equal(0, code1);

            var (code2, _, _) = Run("-#", FullCode, "-T", "descript",
                "-L", "en", "-C", "US", "-q", "-@", dir);
            Assert.Equal(0, code2);
        }
        finally { Directory.Delete(dir, true); }
    }
}
