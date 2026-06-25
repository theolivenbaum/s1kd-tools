using System.Xml;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NewpmToolTests
{
    private static (int code, string outText, string errText) Run(string workingDir, params string[] args)
    {
        string prev = Directory.GetCurrentDirectory();
        var tool = new NewpmTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        try
        {
            Directory.SetCurrentDirectory(workingDir);
            int code = tool.Run(args, stdout, stderr);
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
        }
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-newpm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Code_GeneratesExpectedFileName()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, err) = Run(dir, "-#", "EX-12345-00001-00", "-v");
            Assert.Equal(0, code);

            // Default issue 000-01, default und/ZZ, but verbose path prints name.
            string expected = "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML";
            Assert.True(File.Exists(Path.Combine(dir, expected)),
                $"Expected file {expected} not found. stderr: {err}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Code_WritesCorrectPmCodeAndTitle()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run(dir,
                "-#", "EX-12345-00001-00",
                "-t", "My Publication",
                "-L", "en", "-C", "US",
                "-n", "002", "-w", "03");
            Assert.Equal(0, code);

            string path = Path.Combine(dir, "PMC-EX-12345-00001-00_002-03_EN-US.XML");
            Assert.True(File.Exists(path));

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(path);

            var pmCode = (XmlElement)doc.SelectSingleNode("//pmCode")!;
            Assert.Equal("EX", pmCode.GetAttribute("modelIdentCode"));
            Assert.Equal("12345", pmCode.GetAttribute("pmIssuer"));
            Assert.Equal("00001", pmCode.GetAttribute("pmNumber"));
            Assert.Equal("00", pmCode.GetAttribute("pmVolume"));

            var lang = (XmlElement)doc.SelectSingleNode("//language")!;
            Assert.Equal("en", lang.GetAttribute("languageIsoCode"));
            Assert.Equal("US", lang.GetAttribute("countryIsoCode"));

            var issueInfo = (XmlElement)doc.SelectSingleNode("//issueInfo")!;
            Assert.Equal("002", issueInfo.GetAttribute("issueNumber"));
            Assert.Equal("03", issueInfo.GetAttribute("inWork"));

            Assert.Equal("My Publication", doc.SelectSingleNode("//pmTitle")!.InnerText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingPmcComponents_ReturnsExit1()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, err) = Run(dir);
            Assert.Equal(1, code);
            Assert.Contains("Missing required PMC components", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BadPmc_ReturnsExit1()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, err) = Run(dir, "-#", "NOT-A-PMC");
            Assert.Equal(1, code);
            Assert.Contains("Bad publication module code", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExistingFile_WithoutOverwrite_ReturnsExit2()
    {
        string dir = NewTempDir();
        try
        {
            var (code1, _, _) = Run(dir, "-#", "EX-12345-00001-00");
            Assert.Equal(0, code1);

            var (code2, _, err) = Run(dir, "-#", "EX-12345-00001-00");
            Assert.Equal(2, code2);
            Assert.Contains("already exists", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExistingFile_WithQuiet_ReturnsZero()
    {
        string dir = NewTempDir();
        try
        {
            Run(dir, "-#", "EX-12345-00001-00");
            var (code, _, _) = Run(dir, "-#", "EX-12345-00001-00", "-q");
            Assert.Equal(0, code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OmitIssue_DropsIssueFromFileName()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run(dir, "-#", "EX-12345-00001-00", "-N");
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "PMC-EX-12345-00001-00_UND-ZZ.XML")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Date_SetsIssueDateAttributes()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run(dir, "-#", "EX-12345-00001-00", "-I", "2020-04-15");
            Assert.Equal(0, code);

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(Path.Combine(dir, "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML"));
            var issueDate = (XmlElement)doc.SelectSingleNode("//issueDate")!;
            Assert.Equal("2020", issueDate.GetAttribute("year"));
            Assert.Equal("04", issueDate.GetAttribute("month"));
            Assert.Equal("15", issueDate.GetAttribute("day"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Brex_SetsBrexDmCode()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run(dir, "-#", "EX-12345-00001-00",
                "-b", "S1000D-A-04-10-0301-00A-022A-D");
            Assert.Equal(0, code);

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(Path.Combine(dir, "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML"));
            var dmCode = (XmlElement)doc.SelectSingleNode("//brexDmRef/dmRef/dmRefIdent/dmCode")!;
            Assert.Equal("S1000D", dmCode.GetAttribute("modelIdentCode"));
            Assert.Equal("A", dmCode.GetAttribute("systemDiffCode"));
            Assert.Equal("04", dmCode.GetAttribute("systemCode"));
            Assert.Equal("1", dmCode.GetAttribute("subSystemCode"));
            Assert.Equal("0", dmCode.GetAttribute("subSubSystemCode"));
            Assert.Equal("0301", dmCode.GetAttribute("assyCode"));
            Assert.Equal("00", dmCode.GetAttribute("disassyCode"));
            Assert.Equal("A", dmCode.GetAttribute("disassyCodeVariant"));
            Assert.Equal("022", dmCode.GetAttribute("infoCode"));
            Assert.Equal("A", dmCode.GetAttribute("infoCodeVariant"));
            Assert.Equal("D", dmCode.GetAttribute("itemLocationCode"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NoAct_RemovesApplicCrossRefTableRef()
    {
        string dir = NewTempDir();
        try
        {
            Run(dir, "-#", "EX-12345-00001-00");
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(Path.Combine(dir, "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML"));
            Assert.Null(doc.SelectSingleNode("//applicCrossRefTableRef"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Remarks_AddsSimplePara()
    {
        string dir = NewTempDir();
        try
        {
            Run(dir, "-#", "EX-12345-00001-00", "-m", "A remark");
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(Path.Combine(dir, "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML"));
            var sp = doc.SelectSingleNode("//remarks/simplePara");
            Assert.NotNull(sp);
            Assert.Equal("A remark", sp!.InnerText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NoRemarks_RemovesRemarksElement()
    {
        string dir = NewTempDir();
        try
        {
            Run(dir, "-#", "EX-12345-00001-00");
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(Path.Combine(dir, "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML"));
            Assert.Null(doc.SelectSingleNode("//remarks"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ShortTitleAndRpc_AreWritten()
    {
        string dir = NewTempDir();
        try
        {
            Run(dir, "-#", "EX-12345-00001-00",
                "-s", "Short", "-r", "ACME Corp", "-R", "U8025", "-c", "02");
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(Path.Combine(dir, "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML"));

            Assert.Equal("Short", doc.SelectSingleNode("//shortPmTitle")!.InnerText);
            Assert.Equal("ACME Corp", doc.SelectSingleNode("//responsiblePartnerCompany/enterpriseName")!.InnerText);
            var rpc = (XmlElement)doc.SelectSingleNode("//responsiblePartnerCompany")!;
            Assert.Equal("U8025", rpc.GetAttribute("enterpriseCode"));
            var sec = (XmlElement)doc.SelectSingleNode("//security")!;
            Assert.Equal("02", sec.GetAttribute("securityClassification"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DmRef_AddedToPmEntry()
    {
        string dir = NewTempDir();
        try
        {
            // Write a minimal data module to reference.
            string dmPath = Path.Combine(dir, "DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-US.XML");
            File.WriteAllText(dmPath,
                "<?xml version=\"1.0\"?>\n" +
                "<dmodule>\n" +
                "  <identAndStatusSection>\n" +
                "    <dmAddress>\n" +
                "      <dmIdent>\n" +
                "        <dmCode modelIdentCode=\"EX\" systemDiffCode=\"A\" systemCode=\"00\" subSystemCode=\"0\" subSubSystemCode=\"0\" assyCode=\"00\" disassyCode=\"00\" disassyCodeVariant=\"A\" infoCode=\"040\" infoCodeVariant=\"A\" itemLocationCode=\"D\"/>\n" +
                "        <issueInfo issueNumber=\"001\" inWork=\"00\"/>\n" +
                "        <language languageIsoCode=\"en\" countryIsoCode=\"US\"/>\n" +
                "      </dmIdent>\n" +
                "      <dmAddressItems>\n" +
                "        <issueDate year=\"2020\" month=\"01\" day=\"01\"/>\n" +
                "        <dmTitle><techName>Tech</techName><infoName>Info</infoName></dmTitle>\n" +
                "      </dmAddressItems>\n" +
                "    </dmAddress>\n" +
                "  </identAndStatusSection>\n" +
                "</dmodule>\n");

            var (code, _, _) = Run(dir, "-#", "EX-12345-00001-00", "-i", "-l", "-T", dmPath);
            Assert.Equal(0, code);

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(Path.Combine(dir, "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML"));

            var dmRefCode = (XmlElement)doc.SelectSingleNode("//pmEntry/dmRef/dmRefIdent/dmCode")!;
            Assert.Equal("EX", dmRefCode.GetAttribute("modelIdentCode"));
            Assert.NotNull(doc.SelectSingleNode("//pmEntry/dmRef/dmRefIdent/issueInfo"));
            Assert.NotNull(doc.SelectSingleNode("//pmEntry/dmRef/dmRefIdent/language"));
            Assert.NotNull(doc.SelectSingleNode("//pmEntry/dmRef/dmRefAddressItems/dmTitle"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Version_PrintsVersion()
    {
        var tool = new NewpmTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "--version" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("3.0.1", stdout.ToString());
    }

    [Fact]
    public void Out_ToExplicitFileName()
    {
        string dir = NewTempDir();
        try
        {
            string target = Path.Combine(dir, "custom.xml");
            var (code, _, _) = Run(dir, "-#", "EX-12345-00001-00", "-@", target);
            Assert.Equal(0, code);
            Assert.True(File.Exists(target));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Issue41_DownIssuesViaXslt()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, err) = Run(dir, "-#", "EX-12345-00001-00", "-$", "4.1");
            Assert.Equal(0, code);

            string path = Path.Combine(dir, "PMC-EX-12345-00001-00_000-01_UND-ZZ.XML");
            Assert.True(File.Exists(path), $"stderr: {err}");

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(path);
            // The PMC must survive down-conversion.
            var pmCode = (XmlElement)doc.SelectSingleNode("//pmCode")!;
            Assert.Equal("EX", pmCode.GetAttribute("modelIdentCode"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DumpTemplates_WritesPmXml()
    {
        string dir = NewTempDir();
        try
        {
            var (code, _, _) = Run(dir, "-~", dir);
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "pm.xml")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
