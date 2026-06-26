using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NewcomToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new NewcomTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-newcom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private const string Code = "EX-12345-2018-00001-Q";

    [Fact]
    public void GeneratesExpectedFileNameAndCommentCode()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca");
            Assert.Equal(0, code);
            Assert.Equal("", err);

            // The lowercased commentType is upper-cased again for the filename.
            string expected = "COM-EX-12345-2018-00001-Q_EN-CA.XML";
            string path = Path.Combine(dir, expected);
            Assert.True(File.Exists(path), $"expected {path} to exist");

            var doc = XmlUtils.ReadDoc(path);
            var cc = (XmlElement)doc.SelectSingleNode("//commentCode")!;
            Assert.Equal("EX", cc.GetAttribute("modelIdentCode"));
            Assert.Equal("12345", cc.GetAttribute("senderIdent"));
            Assert.Equal("2018", cc.GetAttribute("yearOfDataIssue"));
            Assert.Equal("00001", cc.GetAttribute("seqNumber"));
            // commentType is lower-cased in the metadata.
            Assert.Equal("q", cc.GetAttribute("commentType"));

            var lang = (XmlElement)doc.SelectSingleNode("//language")!;
            Assert.Equal("en", lang.GetAttribute("languageIsoCode"));
            Assert.Equal("CA", lang.GetAttribute("countryIsoCode"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AcceptsComPrefixInCode()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run("-#", "COM-" + Code, "-@", dir, "-L", "en", "-C", "ca");
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "COM-EX-12345-2018-00001-Q_EN-CA.XML")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SetsMetadataDefaultsAndProvidedValues()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(
                "-#", Code, "-@", dir, "-L", "en", "-C", "ca",
                "-o", "ACME Inc", "-t", "My title", "-I", "2018-03-15",
                "-c", "02", "-P", "cp03", "-r", "rt01", "-z", "rfc",
                "-m", "Some remarks");
            Assert.Equal(0, code);

            string path = Path.Combine(dir, "COM-EX-12345-2018-00001-Q_EN-CA.XML");
            var doc = XmlUtils.ReadDoc(path);

            var issueDate = (XmlElement)doc.SelectSingleNode("//issueDate")!;
            Assert.Equal("2018", issueDate.GetAttribute("year"));
            Assert.Equal("03", issueDate.GetAttribute("month"));
            Assert.Equal("15", issueDate.GetAttribute("day"));

            Assert.Equal("ACME Inc", doc.SelectSingleNode("//enterpriseName")!.InnerText);
            Assert.Equal("My title", doc.SelectSingleNode("//commentTitle")!.InnerText);

            var security = (XmlElement)doc.SelectSingleNode("//security")!;
            Assert.Equal("02", security.GetAttribute("securityClassification"));

            var priority = (XmlElement)doc.SelectSingleNode("//commentPriority")!;
            Assert.Equal("cp03", priority.GetAttribute("commentPriorityCode"));

            var response = (XmlElement)doc.SelectSingleNode("//commentResponse")!;
            Assert.Equal("rt01", response.GetAttribute("responseType"));

            var status = (XmlElement)doc.SelectSingleNode("//commentStatus")!;
            Assert.Equal("rfc", status.GetAttribute("issueType"));

            Assert.Equal("Some remarks", doc.SelectSingleNode("//remarks/simplePara")!.InnerText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DefaultsAreAppliedWhenNotOverridden()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca");
            Assert.Equal(0, code);

            var doc = XmlUtils.ReadDoc(Path.Combine(dir, "COM-EX-12345-2018-00001-Q_EN-CA.XML"));
            Assert.Equal("01", ((XmlElement)doc.SelectSingleNode("//security")!).GetAttribute("securityClassification"));
            Assert.Equal("cp01", ((XmlElement)doc.SelectSingleNode("//commentPriority")!).GetAttribute("commentPriorityCode"));
            Assert.Equal("rt02", ((XmlElement)doc.SelectSingleNode("//commentResponse")!).GetAttribute("responseType"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RemarksRemovedWhenNotSpecified()
    {
        string dir = TempDir();
        try
        {
            Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca");
            var doc = XmlUtils.ReadDoc(Path.Combine(dir, "COM-EX-12345-2018-00001-Q_EN-CA.XML"));
            Assert.Null(doc.SelectSingleNode("//remarks"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BrexCodeIsParsedIntoDmCode()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca",
                "-b", "AE-A-04-10-0301-00A-022A-D");
            Assert.Equal(0, code);

            var doc = XmlUtils.ReadDoc(Path.Combine(dir, "COM-EX-12345-2018-00001-Q_EN-CA.XML"));
            var dm = (XmlElement)doc.SelectSingleNode("//brexDmRef/dmRef/dmRefIdent/dmCode")!;
            Assert.Equal("AE", dm.GetAttribute("modelIdentCode"));
            Assert.Equal("A", dm.GetAttribute("systemDiffCode"));
            Assert.Equal("04", dm.GetAttribute("systemCode"));
            Assert.Equal("1", dm.GetAttribute("subSystemCode"));
            Assert.Equal("0", dm.GetAttribute("subSubSystemCode"));
            Assert.Equal("0301", dm.GetAttribute("assyCode"));
            Assert.Equal("00", dm.GetAttribute("disassyCode"));
            Assert.Equal("A", dm.GetAttribute("disassyCodeVariant"));
            Assert.Equal("022", dm.GetAttribute("infoCode"));
            Assert.Equal("A", dm.GetAttribute("infoCodeVariant"));
            Assert.Equal("D", dm.GetAttribute("itemLocationCode"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BadBrexCodeReturnsExitCode3()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca", "-b", "garbage");
            Assert.Equal(3, code);
            Assert.Contains("Bad BREX", err);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MissingCodeComponentsReturnsExitCode1()
    {
        // No -# and no defaults available: required components missing.
        string dir = TempDir();
        try
        {
            var prev = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir); // isolate from any .defaults up-tree
            try
            {
                var (code, _, err) = Run("-@", dir, "-L", "en", "-C", "ca");
                Assert.Equal(1, code);
                Assert.Contains("Missing required comment code", err);
            }
            finally
            {
                Directory.SetCurrentDirectory(prev);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BadIssueReturnsExitCode5()
    {
        var (code, _, err) = Run("-#", Code, "-$", "9.9");
        Assert.Equal(5, code);
        Assert.Contains("Unsupported issue", err);
    }

    [Fact]
    public void BadDateReturnsExitCode4()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca", "-I", "not-a-date");
            Assert.Equal(4, code);
            Assert.Contains("Bad issue date", err);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExistingFileWithoutOverwriteReturnsExitCode2()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "COM-EX-12345-2018-00001-Q_EN-CA.XML");
            File.WriteAllText(path, "<comment/>");

            var (code, _, err) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca");
            Assert.Equal(2, code);
            Assert.Contains("already exists", err);

            // -q suppresses the error.
            var (qcode, _, _) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca", "-q");
            Assert.Equal(0, qcode);

            // -f overwrites.
            var (fcode, _, _) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca", "-f");
            Assert.Equal(0, fcode);
            var doc = XmlUtils.ReadDoc(path);
            Assert.NotNull(doc.SelectSingleNode("//commentCode"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerbosePrintsOutputPath()
    {
        string dir = TempDir();
        try
        {
            var (code, outText, _) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca", "-v");
            Assert.Equal(0, code);
            Assert.Contains("COM-EX-12345-2018-00001-Q_EN-CA.XML", outText.Trim());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DumpTemplateWritesCommentXml()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run("-~", dir);
            Assert.Equal(0, code);
            string path = Path.Combine(dir, "comment.xml");
            Assert.True(File.Exists(path));
            var doc = XmlUtils.ReadDoc(path);
            Assert.Equal("comment", doc.DocumentElement!.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VersionAndHelp()
    {
        var (vcode, vout, _) = Run("--version");
        Assert.Equal(0, vcode);
        Assert.Contains("3.0.2", vout);

        var (hcode, hout, _) = Run("-h");
        Assert.Equal(0, hcode);
        Assert.Contains("Usage: s1kd-newcom", hout);
    }

    [Fact]
    public void Issue41_DownConvertsViaXslt()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run("-#", Code, "-@", dir, "-L", "en", "-C", "ca", "-$", "4.1");
            Assert.Equal(0, code);
            Assert.Equal("", err);

            string path = Directory.GetFiles(dir, "*.XML").Single();
            string text = File.ReadAllText(path);
            // The down-issue stylesheet rewrites the schema location to the
            // selected issue's directory.
            Assert.Contains("S1000D_4-1", text);
            Assert.DoesNotContain("S1000D_6", text);

            // The comment code must survive down-conversion.
            var doc = XmlUtils.ReadDoc(path);
            var cc = (XmlElement)doc.SelectSingleNode("//commentCode")!;
            Assert.Equal("EX", cc.GetAttribute("modelIdentCode"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
