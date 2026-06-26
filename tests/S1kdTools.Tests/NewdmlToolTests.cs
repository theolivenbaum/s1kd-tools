using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NewdmlToolTests
{
    private static (int code, string outText, string errText) Run(string workdir, params string[] args)
    {
        string prev = Directory.GetCurrentDirectory();
        var tool = new NewdmlTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        try
        {
            Directory.SetCurrentDirectory(workdir);
            int code = tool.Run(args, stdout, stderr);
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
        }
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-newdml-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static XmlDocument Load(string path)
    {
        var doc = XmlUtils.ReadDoc(path);
        return doc;
    }

    [Fact]
    public void CreatesDmlWithCodeAndMetadata()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir,
                "-#", "DML-EX-12345-C-2026-00001",
                "-n", "001", "-w", "02", "-c", "03",
                "-I", "2026-06-25");

            Assert.Equal(0, code);
            Assert.Equal("", err);

            string expected = Path.Combine(dir, "DML-EX-12345-C-2026-00001_001-02.XML");
            Assert.True(File.Exists(expected), $"expected {expected}");

            var doc = Load(expected);
            var dmlCode = (XmlElement)doc.SelectSingleNode("//dmlIdent/dmlCode")!;
            Assert.Equal("EX", dmlCode.GetAttribute("modelIdentCode"));
            Assert.Equal("12345", dmlCode.GetAttribute("senderIdent"));
            Assert.Equal("c", dmlCode.GetAttribute("dmlType")); // lowercase in metadata
            Assert.Equal("2026", dmlCode.GetAttribute("yearOfDataIssue"));
            Assert.Equal("00001", dmlCode.GetAttribute("seqNumber"));

            var issueInfo = (XmlElement)doc.SelectSingleNode("//dmlIdent/issueInfo")!;
            Assert.Equal("001", issueInfo.GetAttribute("issueNumber"));
            Assert.Equal("02", issueInfo.GetAttribute("inWork"));

            var security = (XmlElement)doc.SelectSingleNode("//dmlStatus/security")!;
            Assert.Equal("03", security.GetAttribute("securityClassification"));

            var issueDate = (XmlElement)doc.SelectSingleNode("//dmlAddressItems/issueDate")!;
            Assert.Equal("2026", issueDate.GetAttribute("year"));
            Assert.Equal("06", issueDate.GetAttribute("month"));
            Assert.Equal("25", issueDate.GetAttribute("day"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OmitIssueProducesShortFilename()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir, "-#", "DML-EX-12345-C-2026-00001", "-N");
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "DML-EX-12345-C-2026-00001.XML")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingCodeComponentsFails()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir); // no defaults, no -#
            Assert.Equal(3, code); // EXIT_BAD_CODE
            Assert.Contains("Missing required DML code components", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BadDmlCodeFails()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir, "-#", "DML-EX-12345");
            Assert.Equal(3, code);
            Assert.Contains("Bad DML code", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BadDateFails()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir, "-#", "DML-EX-12345-C-2026-00001", "-I", "notadate");
            Assert.Equal(5, code); // EXIT_BAD_DATE
            Assert.Contains("Bad issue date", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExistingFileNotOverwrittenWithoutForce()
    {
        string dir = TempDir();
        try
        {
            var (c1, _, _) = Run(dir, "-#", "DML-EX-12345-C-2026-00001");
            Assert.Equal(0, c1);

            var (c2, _, err) = Run(dir, "-#", "DML-EX-12345-C-2026-00001");
            Assert.Equal(1, c2); // EXIT_DML_EXISTS
            Assert.Contains("already exists", err);

            var (c3, _, _) = Run(dir, "-#", "DML-EX-12345-C-2026-00001", "-q");
            Assert.Equal(0, c3); // quiet: no error

            var (c4, _, _) = Run(dir, "-#", "DML-EX-12345-C-2026-00001", "-f");
            Assert.Equal(0, c4); // overwrite
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BrexDmCodeIsApplied()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir,
                "-#", "DML-EX-12345-C-2026-00001",
                "-b", "S1000D-A-04-10-0301-00A-022A-D");
            Assert.Equal(0, code);

            var doc = Load(Path.Combine(dir, "DML-EX-12345-C-2026-00001_000-01.XML"));
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
    public void RemarksAddedWhenSpecified()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir, "-#", "DML-EX-12345-C-2026-00001", "-m", "Test remark");
            Assert.Equal(0, code);
            var doc = Load(Path.Combine(dir, "DML-EX-12345-C-2026-00001_000-01.XML"));
            var sp = doc.SelectSingleNode("//remarks/simplePara");
            Assert.NotNull(sp);
            Assert.Equal("Test remark", sp!.InnerText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RemarksRemovedWhenAbsent()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir, "-#", "DML-EX-12345-C-2026-00001");
            Assert.Equal(0, code);
            var doc = Load(Path.Combine(dir, "DML-EX-12345-C-2026-00001_000-01.XML"));
            Assert.Null(doc.SelectSingleNode("//remarks"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AddsDmRefEntryFromDataModule()
    {
        string dir = TempDir();
        try
        {
            string dmPath = Path.Combine(dir, "DMC-EX-A-00-00-00-00A-040A-D_002-01_EN-CA.XML");
            File.WriteAllText(dmPath, Fixtures.DataModule);

            var (code, _, _) = Run(dir, "-#", "DML-EX-12345-C-2026-00001", dmPath);
            Assert.Equal(0, code);

            var doc = Load(Path.Combine(dir, "DML-EX-12345-C-2026-00001_000-01.XML"));
            var entry = doc.SelectSingleNode("//dmlContent/dmlEntry");
            Assert.NotNull(entry);

            var refCode = (XmlElement)doc.SelectSingleNode("//dmlEntry/dmRef/dmRefIdent/dmCode")!;
            Assert.Equal("EX", refCode.GetAttribute("modelIdentCode"));
            Assert.Equal("040", refCode.GetAttribute("infoCode"));

            var title = doc.SelectSingleNode("//dmlEntry/dmRef/dmRefAddressItems/dmTitle/techName");
            Assert.NotNull(title);
            Assert.Equal("Example", title!.InnerText);

            // Non-CSL (dmlType != S): issueInfo and issueDate are omitted.
            Assert.Null(doc.SelectSingleNode("//dmlEntry/dmRef/dmRefIdent/issueInfo"));
            Assert.Null(doc.SelectSingleNode("//dmlEntry/dmRef/dmRefAddressItems/issueDate"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StatusListIncludesIssueInfoAndIssueDate()
    {
        string dir = TempDir();
        try
        {
            string dmPath = Path.Combine(dir, "DMC-EX-A-00-00-00-00A-040A-D_002-01_EN-CA.XML");
            File.WriteAllText(dmPath, Fixtures.DataModule);

            // dmlType "S" => status list => CSL behaviour.
            var (code, _, _) = Run(dir, "-#", "DML-EX-12345-S-2026-00001", dmPath);
            Assert.Equal(0, code);

            var doc = Load(Path.Combine(dir, "DML-EX-12345-S-2026-00001_000-01.XML"));
            Assert.NotNull(doc.SelectSingleNode("//dmlEntry/dmRef/dmRefIdent/issueInfo"));
            Assert.NotNull(doc.SelectSingleNode("//dmlEntry/dmRef/dmRefAddressItems/issueDate"));
            var entry = (XmlElement)doc.SelectSingleNode("//dmlEntry")!;
            Assert.Equal("changed", entry.GetAttribute("issueType"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AddsIcnRefFromFilename()
    {
        string dir = TempDir();
        try
        {
            // Non-XML file named ICN-...-secclass.ext
            string icn = "ICN-EX-12345-001-01.JPG";
            string icnPath = Path.Combine(dir, icn);
            File.WriteAllText(icnPath, "not xml");

            var (code, _, _) = Run(dir, "-#", "DML-EX-12345-C-2026-00001", icnPath);
            Assert.Equal(0, code);

            var doc = Load(Path.Combine(dir, "DML-EX-12345-C-2026-00001_000-01.XML"));
            var ier = (XmlElement)doc.SelectSingleNode("//dmlEntry/infoEntityRef")!;
            Assert.Equal("ICN-EX-12345-001-01", ier.GetAttribute("infoEntityRefIdent"));
            var sec = (XmlElement)doc.SelectSingleNode("//dmlEntry/security")!;
            Assert.Equal("01", sec.GetAttribute("securityClassification"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DumpTemplateWritesDmlXml()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir, "-~", dir);
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "dml.xml")));
            var doc = Load(Path.Combine(dir, "dml.xml"));
            Assert.Equal("dml", doc.DocumentElement!.Name);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void VersionPrints()
    {
        string dir = TempDir();
        try
        {
            var (code, outText, _) = Run(dir, "--version");
            Assert.Equal(0, code);
            Assert.Contains("s1kd-newdml", outText);
            Assert.Contains("3.0.1", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Issue50_DownConvertsViaXslt()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir,
                "-#", "DML-EX-12345-C-2026-00001",
                "-n", "001", "-w", "02", "-$", "5.0");
            Assert.Equal(0, code);
            Assert.Equal("", err);

            string path = Directory.GetFiles(dir, "*.XML").Single();
            string text = File.ReadAllText(path);
            // The down-issue stylesheet rewrites the schema location to the
            // selected issue's directory; the document is no longer issue 6.
            Assert.Contains("S1000D_5-0", text);
            Assert.DoesNotContain("S1000D_6", text);

            // The root DML element must survive down-conversion.
            var doc = XmlUtils.ReadDoc(path);
            Assert.Equal("dml", doc.DocumentElement!.Name);
        }
        finally { Directory.Delete(dir, true); }
    }
}
