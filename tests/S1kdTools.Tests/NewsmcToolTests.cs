using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NewsmcToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new NewsmcTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-newsmc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static XmlDocument Load(string path) => XmlUtils.ReadDoc(path);

    // --------------------------------------------------------------------

    [Fact]
    public void Version_PrintsNameAndVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("s1kd-newsmc", outText);
        Assert.Contains("3.0.1", outText);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("Usage: s1kd-newsmc", outText);
    }

    [Fact]
    public void MissingComponents_ReturnsBadSmc()
    {
        // No model ident code etc. supplied. Point at an empty defaults file so
        // ancestor .defaults files cannot supply the components.
        string dir = TempDir();
        string emptyDefaults = Path.Combine(dir, ".defaults");
        File.WriteAllText(emptyDefaults, "<defaults/>\n");

        var (code, _, errText) = Run("-d", emptyDefaults, "-@", dir);
        Assert.Equal(1, code); // EXIT_BAD_SMC
        Assert.Contains("Missing required SMC components", errText);
    }

    [Fact]
    public void Code_GeneratesExpectedFilenameAndMetadata()
    {
        string dir = TempDir();
        var (code, _, errText) = Run(
            "-#", "TEST-12345-00001-00",
            "-L", "en", "-C", "ca",
            "-n", "001", "-w", "03",
            "-t", "My SCORM Package",
            "-c", "02",
            "-r", "Test Company", "-R", "12345",
            "-@", dir);

        Assert.Equal(0, code);
        Assert.Equal("", errText);

        string expected = Path.Combine(dir, "SMC-TEST-12345-00001-00_001-03_EN-CA.XML");
        Assert.True(File.Exists(expected), $"expected generated file at {expected}");

        var doc = Load(expected);

        var smcCode = (XmlElement)doc.SelectSingleNode("//scormContentPackageCode")!;
        Assert.Equal("TEST", smcCode.GetAttribute("modelIdentCode"));
        Assert.Equal("12345", smcCode.GetAttribute("scormContentPackageIssuer"));
        Assert.Equal("00001", smcCode.GetAttribute("scormContentPackageNumber"));
        Assert.Equal("00", smcCode.GetAttribute("scormContentPackageVolume"));

        var lang = (XmlElement)doc.SelectSingleNode("//scormContentPackageIdent/language")!;
        Assert.Equal("en", lang.GetAttribute("languageIsoCode"));
        Assert.Equal("CA", lang.GetAttribute("countryIsoCode"));

        var issueInfo = (XmlElement)doc.SelectSingleNode("//scormContentPackageIdent/issueInfo")!;
        Assert.Equal("001", issueInfo.GetAttribute("issueNumber"));
        Assert.Equal("03", issueInfo.GetAttribute("inWork"));

        Assert.Equal("My SCORM Package",
            doc.SelectSingleNode("//scormContentPackageTitle")!.InnerText);

        var security = (XmlElement)doc.SelectSingleNode("//security")!;
        Assert.Equal("02", security.GetAttribute("securityClassification"));

        Assert.Equal("Test Company",
            doc.SelectSingleNode("//responsiblePartnerCompany/enterpriseName")!.InnerText);
        var rpc = (XmlElement)doc.SelectSingleNode("//responsiblePartnerCompany")!;
        Assert.Equal("12345", rpc.GetAttribute("enterpriseCode"));

        // By default no ACT is referenced -> applicCrossRefTableRef removed.
        Assert.Null(doc.SelectSingleNode("//applicCrossRefTableRef"));

        // Default skill level is sk01.
        var skill = (XmlElement)doc.SelectSingleNode("//personSkill")!;
        Assert.Equal("sk01", skill.GetAttribute("skillLevelCode"));
    }

    [Fact]
    public void IssueDate_SetExplicitly()
    {
        string dir = TempDir();
        var (code, _, _) = Run(
            "-#", "TEST-12345-00001-00",
            "-I", "2026-06-25",
            "-@", dir);
        Assert.Equal(0, code);

        string file = Path.Combine(dir, "SMC-TEST-12345-00001-00_000-01_UND-ZZ.XML");
        Assert.True(File.Exists(file));

        var issueDate = (XmlElement)Load(file).SelectSingleNode("//issueDate")!;
        Assert.Equal("2026", issueDate.GetAttribute("year"));
        Assert.Equal("06", issueDate.GetAttribute("month"));
        Assert.Equal("25", issueDate.GetAttribute("day"));
    }

    [Fact]
    public void OmitIssue_DropsIssueFieldFromFilename()
    {
        string dir = TempDir();
        var (code, _, _) = Run(
            "-#", "TEST-12345-00001-00",
            "-N",
            "-@", dir);
        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(dir, "SMC-TEST-12345-00001-00_UND-ZZ.XML")));
    }

    [Fact]
    public void BadCode_ReturnsBadSmc()
    {
        var (code, _, errText) = Run("-#", "TEST-12345");
        Assert.Equal(1, code);
        Assert.Contains("Bad SCORM content package code", errText);
    }

    [Fact]
    public void ExistingFile_WithoutOverwrite_ReturnsSmcExists()
    {
        string dir = TempDir();
        string[] args = { "-#", "TEST-12345-00001-00", "-@", dir };

        var (code1, _, _) = Run(args);
        Assert.Equal(0, code1);

        var (code2, _, errText) = Run(args);
        Assert.Equal(2, code2); // EXIT_SMC_EXISTS
        Assert.Contains("already exists", errText);
    }

    [Fact]
    public void ExistingFile_WithQuiet_ReturnsZero()
    {
        string dir = TempDir();
        string[] args = { "-#", "TEST-12345-00001-00", "-@", dir };
        Run(args);
        var (code, _, _) = Run("-#", "TEST-12345-00001-00", "-@", dir, "-q");
        Assert.Equal(0, code);
    }

    [Fact]
    public void Brex_SetsDmCodeAttributes()
    {
        string dir = TempDir();
        var (code, _, _) = Run(
            "-#", "TEST-12345-00001-00",
            "-b", "MYMODEL-A-01-02-0304-05A-060A-D",
            "-@", dir);
        Assert.Equal(0, code);

        string file = Path.Combine(dir, "SMC-TEST-12345-00001-00_000-01_UND-ZZ.XML");
        var dmCode = (XmlElement)Load(file).SelectSingleNode("//brexDmRef/dmRef/dmRefIdent/dmCode")!;
        Assert.Equal("MYMODEL", dmCode.GetAttribute("modelIdentCode"));
        Assert.Equal("A", dmCode.GetAttribute("systemDiffCode"));
        Assert.Equal("01", dmCode.GetAttribute("systemCode"));
        Assert.Equal("0", dmCode.GetAttribute("subSystemCode"));
        Assert.Equal("2", dmCode.GetAttribute("subSubSystemCode"));
        Assert.Equal("0304", dmCode.GetAttribute("assyCode"));
        Assert.Equal("05", dmCode.GetAttribute("disassyCode"));
        Assert.Equal("A", dmCode.GetAttribute("disassyCodeVariant"));
        Assert.Equal("060", dmCode.GetAttribute("infoCode"));
        Assert.Equal("A", dmCode.GetAttribute("infoCodeVariant"));
        Assert.Equal("D", dmCode.GetAttribute("itemLocationCode"));
    }

    [Fact]
    public void Verbose_PrintsOutputPath()
    {
        string dir = TempDir();
        var (code, outText, _) = Run(
            "-#", "TEST-12345-00001-00",
            "-v", "-@", dir);
        Assert.Equal(0, code);
        Assert.Contains("SMC-TEST-12345-00001-00_000-01_UND-ZZ.XML", outText);
    }

    [Fact]
    public void DumpTemplates_WritesTemplateFile()
    {
        string dir = TempDir();
        var (code, _, _) = Run("-~", dir);
        Assert.Equal(0, code);
        string templatePath = Path.Combine(dir, "scormcontentpackage.xml");
        Assert.True(File.Exists(templatePath));
        Assert.Contains("scormContentPackage", File.ReadAllText(templatePath));
    }

    [Fact]
    public void Issue41_DownConvertsViaXslt()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run("-#", "TEST-12345-00001-00", "-@", dir, "-$", "4.1");
            Assert.Equal(0, code);
            Assert.Equal("", err);

            string path = Directory.GetFiles(dir, "*.XML").Single();
            string text = File.ReadAllText(path);
            // The down-issue stylesheet rewrites the schema location to the
            // selected issue's directory; the document is no longer issue 6.
            Assert.Contains("S1000D_4-1", text);
            Assert.DoesNotContain("S1000D_6", text);

            // The root element must survive down-conversion.
            var doc = Load(path);
            Assert.Equal("scormContentPackage", doc.DocumentElement!.Name);
        }
        finally { Directory.Delete(dir, true); }
    }
}
