using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NewimfToolTests
{
    private static (int code, string outText, string errText) Run(string workDir, params string[] args)
    {
        // The tool resolves output relative to the current directory; run each
        // case in its own temp directory to keep generated files isolated.
        string prev = Directory.GetCurrentDirectory();
        var tool = new NewimfTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        try
        {
            Directory.SetCurrentDirectory(workDir);
            int code = tool.Run(args, stdout, stderr);
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
        }
    }

    private static string NewWorkDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-imf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static XmlDocument Load(string path)
    {
        var doc = XmlUtils.ReadDoc(path);
        return doc;
    }

    [Fact]
    public void GeneratesExpectedFileNameAndMetadata()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, err) = Run(dir,
                "-n", "002", "-w", "03", "-c", "02",
                "-t", "My ICN title", "-I", "2026-06-25",
                "ICN-S1000DBIKE-AAA-D000000-0-U8025-00555-A-04-1.JPG");

            Assert.Equal(0, code);
            Assert.Equal("", err);

            string expected = Path.Combine(dir,
                "IMF-S1000DBIKE-AAA-D000000-0-U8025-00555-A-04-1_002-03.XML");
            Assert.True(File.Exists(expected), "IMF file should be created with issue/inwork in name");

            var doc = Load(expected);

            var imfCode = (XmlElement)doc.SelectSingleNode("//imfIdent/imfCode")!;
            Assert.Equal("S1000DBIKE-AAA-D000000-0-U8025-00555-A-04-1", imfCode.GetAttribute("imfIdentIcn"));

            var issueInfo = (XmlElement)doc.SelectSingleNode("//imfIdent/issueInfo")!;
            Assert.Equal("002", issueInfo.GetAttribute("issueNumber"));
            Assert.Equal("03", issueInfo.GetAttribute("inWork"));

            var title = (XmlElement)doc.SelectSingleNode("//imfAddressItems/icnTitle")!;
            Assert.Equal("My ICN title", title.InnerText);

            var issueDate = (XmlElement)doc.SelectSingleNode("//imfAddressItems/issueDate")!;
            Assert.Equal("2026", issueDate.GetAttribute("year"));
            Assert.Equal("06", issueDate.GetAttribute("month"));
            Assert.Equal("25", issueDate.GetAttribute("day"));

            var security = (XmlElement)doc.SelectSingleNode("//imfStatus/security")!;
            Assert.Equal("02", security.GetAttribute("securityClassification"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DefaultsAreAppliedWhenOptionsOmitted()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, _) = Run(dir, "ICN-TEST-1.PNG");
            Assert.Equal(0, code);

            // Defaults: issue 000, inwork 01, security 01.
            string expected = Path.Combine(dir, "IMF-TEST-1_000-01.XML");
            Assert.True(File.Exists(expected));

            var doc = Load(expected);
            var issueInfo = (XmlElement)doc.SelectSingleNode("//imfIdent/issueInfo")!;
            Assert.Equal("000", issueInfo.GetAttribute("issueNumber"));
            Assert.Equal("01", issueInfo.GetAttribute("inWork"));
            var security = (XmlElement)doc.SelectSingleNode("//imfStatus/security")!;
            Assert.Equal("01", security.GetAttribute("securityClassification"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OmitIssueProducesShortFileName()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, _) = Run(dir, "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "IMF-TEST-1.XML")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RpcAndOriginatorMetadataAreSet()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, _) = Run(dir,
                "-R", "12345", "-r", "Some RPC",
                "-O", "67890", "-o", "Some Originator",
                "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code);

            var doc = Load(Path.Combine(dir, "IMF-TEST-1.XML"));

            var rpc = (XmlElement)doc.SelectSingleNode("//imfStatus/responsiblePartnerCompany")!;
            Assert.Equal("12345", rpc.GetAttribute("enterpriseCode"));
            Assert.Equal("Some RPC", rpc.SelectSingleNode("enterpriseName")!.InnerText);

            var orig = (XmlElement)doc.SelectSingleNode("//imfStatus/originator")!;
            Assert.Equal("67890", orig.GetAttribute("enterpriseCode"));
            Assert.Equal("Some Originator", orig.SelectSingleNode("enterpriseName")!.InnerText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BrexCodeIsParsedIntoDmCodeAttributes()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, err) = Run(dir,
                "-b", "S1000D-F-04-10-0301-00A-022A-D",
                "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code);
            Assert.Equal("", err);

            var doc = Load(Path.Combine(dir, "IMF-TEST-1.XML"));
            var dmCode = (XmlElement)doc.SelectSingleNode("//brexDmRef/dmRef/dmRefIdent/dmCode")!;
            Assert.Equal("S1000D", dmCode.GetAttribute("modelIdentCode"));
            Assert.Equal("F", dmCode.GetAttribute("systemDiffCode"));
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
    public void BadBrexCodeReturnsExitCode2()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, err) = Run(dir,
                "-b", "not-a-valid-code",
                "-N", "ICN-TEST-1.PNG");
            Assert.Equal(2, code); // EXIT_BAD_BREX_DMC
            Assert.Contains("Bad BREX", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BadDateReturnsExitCode3()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, err) = Run(dir, "-I", "2026", "-N", "ICN-TEST-1.PNG");
            Assert.Equal(3, code); // EXIT_BAD_DATE
            Assert.Contains("Bad issue date", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnsupportedIssueReturnsExitCode8()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, err) = Run(dir, "-$", "3.0", "ICN-TEST-1.PNG");
            Assert.Equal(8, code); // EXIT_BAD_ISSUE
            Assert.Contains("Unsupported issue", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExistingFileReturnsExitCode1_AndOverwriteSucceeds()
    {
        string dir = NewWorkDir();
        try
        {
            var (code1, _, _) = Run(dir, "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code1);

            var (code2, _, err2) = Run(dir, "-N", "ICN-TEST-1.PNG");
            Assert.Equal(1, code2); // EXIT_IMF_EXISTS
            Assert.Contains("already exists", err2);

            var (code3, _, _) = Run(dir, "-f", "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code3);

            // -q suppresses the error and exits 0.
            var (code4, _, _) = Run(dir, "-q", "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code4);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RemarksProducesSimplePara()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, _) = Run(dir, "-m", "A remark", "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code);
            var doc = Load(Path.Combine(dir, "IMF-TEST-1.XML"));
            var sp = doc.SelectSingleNode("//remarks/simplePara");
            Assert.NotNull(sp);
            Assert.Equal("A remark", sp!.InnerText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NoRemarksRemovesRemarksElement()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, _) = Run(dir, "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code);
            var doc = Load(Path.Combine(dir, "IMF-TEST-1.XML"));
            Assert.Null(doc.SelectSingleNode("//remarks"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NonIcnArgumentIsSkipped()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, _) = Run(dir, "-N", "not-an-icn.png");
            Assert.Equal(0, code);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void VersionAndHelpReturnZero()
    {
        string dir = NewWorkDir();
        try
        {
            var (vcode, vout, _) = Run(dir, "--version");
            Assert.Equal(0, vcode);
            Assert.Contains("3.0.1", vout);

            var (hcode, hout, _) = Run(dir, "--help");
            Assert.Equal(0, hcode);
            Assert.Contains("Usage", hout);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void VerbosePrintsFileName()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, outText, _) = Run(dir, "-v", "-N", "ICN-TEST-1.PNG");
            Assert.Equal(0, code);
            Assert.Contains("IMF-TEST-1.XML", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Issue42_DownConvertsViaXslt()
    {
        string dir = NewWorkDir();
        try
        {
            var (code, _, err) = Run(dir, "-N", "-$", "4.2", "ICN-TEST-1.PNG");
            Assert.Equal(0, code);
            Assert.Equal("", err);

            string path = Directory.GetFiles(dir, "*.XML").Single();
            string text = File.ReadAllText(path);
            // The down-issue stylesheet rewrites the schema location to the
            // selected issue's directory; the document is no longer issue 6.
            Assert.Contains("S1000D_4-2", text);
            Assert.DoesNotContain("S1000D_6", text);

            // The root element must survive down-conversion.
            var doc = Load(path);
            Assert.Equal("icnMetadataFile", doc.DocumentElement!.Name);
        }
        finally { Directory.Delete(dir, true); }
    }
}
