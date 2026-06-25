using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NewddnToolTests
{
    private static (int code, string outText, string errText) Run(string workdir, params string[] args)
    {
        string prev = Directory.GetCurrentDirectory();
        var tool = new NewddnTool();
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
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-newddn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static XmlDocument LoadFromDir(string dir, string name) =>
        XmlUtils.ReadDoc(Path.Combine(dir, name));

    private const string ExpectedName = "DDN-EX-12345-54321-2018-00001.XML";

    [Fact]
    public void Code_ProducesExpectedFilenameAndDdnCode()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir, "-#", "EX-12345-54321-2018-00001");

            Assert.Equal(0, code);
            Assert.Equal("", err);
            Assert.True(File.Exists(Path.Combine(dir, ExpectedName)), "expected DDN file not created");

            var doc = LoadFromDir(dir, ExpectedName);
            var ddnCode = (XmlElement)doc.SelectSingleNode("//ddnIdent/ddnCode")!;
            Assert.Equal("EX", ddnCode.GetAttribute("modelIdentCode"));
            Assert.Equal("12345", ddnCode.GetAttribute("senderIdent"));
            Assert.Equal("54321", ddnCode.GetAttribute("receiverIdent"));
            Assert.Equal("2018", ddnCode.GetAttribute("yearOfDataIssue"));
            Assert.Equal("00001", ddnCode.GetAttribute("seqNumber"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CodeWithDdnPrefix_IsAccepted()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir, "-#", "DDN-EX-12345-54321-2018-00001");
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, ExpectedName)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Metadata_IsWrittenIntoDocument()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir,
                "-#", "EX-12345-54321-2018-00001",
                "-o", "Sender Co", "-t", "Senderville", "-n", "SX",
                "-r", "Receiver Co", "-T", "Receiverton", "-N", "RX",
                "-a", "AUTH-1", "-I", "2020-03-04", "-m", "Some remarks");

            Assert.Equal(0, code);
            var doc = LoadFromDir(dir, ExpectedName);

            Assert.Equal("Sender Co", doc.SelectSingleNode("//dispatchFrom/dispatchAddress/enterprise/enterpriseName")!.InnerText);
            Assert.Equal("Senderville", doc.SelectSingleNode("//dispatchFrom/dispatchAddress/address/city")!.InnerText);
            Assert.Equal("SX", doc.SelectSingleNode("//dispatchFrom/dispatchAddress/address/country")!.InnerText);
            Assert.Equal("Receiver Co", doc.SelectSingleNode("//dispatchTo/dispatchAddress/enterprise/enterpriseName")!.InnerText);
            Assert.Equal("Receiverton", doc.SelectSingleNode("//dispatchTo/dispatchAddress/address/city")!.InnerText);
            Assert.Equal("RX", doc.SelectSingleNode("//dispatchTo/dispatchAddress/address/country")!.InnerText);
            Assert.Equal("AUTH-1", doc.SelectSingleNode("//ddnStatus/authorization")!.InnerText);
            Assert.Equal("Some remarks", doc.SelectSingleNode("//remarks/simplePara")!.InnerText);

            var issueDate = (XmlElement)doc.SelectSingleNode("//issueDate")!;
            Assert.Equal("2020", issueDate.GetAttribute("year"));
            Assert.Equal("03", issueDate.GetAttribute("month"));
            Assert.Equal("04", issueDate.GetAttribute("day"));

            var security = (XmlElement)doc.SelectSingleNode("//ddnStatus/security")!;
            Assert.Equal("01", security.GetAttribute("securityClassification"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DeliveryList_PopulatedWithBasenames()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir,
                "-#", "EX-12345-54321-2018-00001",
                "/some/path/DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML",
                "another.XML");

            Assert.Equal(0, code);
            var doc = LoadFromDir(dir, ExpectedName);

            var items = doc.SelectNodes("//deliveryList/deliveryListItem/dispatchFileName")!;
            Assert.Equal(2, items.Count);
            Assert.Equal("DMC-EX-A-00-00-00-00A-040A-D_001-00_EN-CA.XML", items[0]!.InnerText);
            Assert.Equal("another.XML", items[1]!.InnerText);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void NoFiles_RemovesDeliveryList()
    {
        string dir = TempDir();
        try
        {
            Run(dir, "-#", "EX-12345-54321-2018-00001");
            var doc = LoadFromDir(dir, ExpectedName);
            Assert.Null(doc.SelectSingleNode("//deliveryList"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Brex_SetsDmCodeAttributes()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir,
                "-#", "EX-12345-54321-2018-00001",
                "-b", "S1000D-A-04-10-0301-00A-022A-D");

            Assert.Equal(0, code);
            var doc = LoadFromDir(dir, ExpectedName);
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
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BadBrex_ReturnsExit3()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir,
                "-#", "EX-12345-54321-2018-00001",
                "-b", "not-a-valid-brex");
            Assert.Equal(3, code);
            Assert.Contains("Bad BREX", err);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MissingCodeComponents_ReturnsExit2()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir);
            Assert.Equal(2, code);
            Assert.Contains("Missing required DDN code components", err);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MalformedCode_ReturnsExit2()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir, "-#", "EX-12345-54321");
            Assert.Equal(2, code);
            Assert.Contains("Bad DDN code", err);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BadIssue_ReturnsExit5()
    {
        string dir = TempDir();
        try
        {
            var (code, _, err) = Run(dir, "-#", "EX-12345-54321-2018-00001", "-$", "9.9");
            Assert.Equal(5, code);
            Assert.Contains("Unsupported issue", err);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ExistingFile_ReturnsExit1_UnlessOverwriteOrQuiet()
    {
        string dir = TempDir();
        try
        {
            Assert.Equal(0, Run(dir, "-#", "EX-12345-54321-2018-00001").code);

            var (code, _, err) = Run(dir, "-#", "EX-12345-54321-2018-00001");
            Assert.Equal(1, code);
            Assert.Contains("already exists", err);

            // -q suppresses the error and returns 0.
            Assert.Equal(0, Run(dir, "-q", "-#", "EX-12345-54321-2018-00001").code);

            // -f overwrites.
            Assert.Equal(0, Run(dir, "-f", "-#", "EX-12345-54321-2018-00001").code);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Out_DirectoryTarget_PlacesFileInside()
    {
        string dir = TempDir();
        string sub = Path.Combine(dir, "subdir");
        Directory.CreateDirectory(sub);
        try
        {
            var (code, _, _) = Run(dir, "-@", sub, "-#", "EX-12345-54321-2018-00001");
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(sub, ExpectedName)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Out_FileTarget_UsesGivenName()
    {
        string dir = TempDir();
        try
        {
            var (code, outText, _) = Run(dir, "-v", "-@", "custom.XML", "-#", "EX-12345-54321-2018-00001");
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(dir, "custom.XML")));
            Assert.Contains("custom.XML", outText);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Defaults_FileProvidesCodeComponents()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".defaults"),
                "<defaults>\n" +
                "  <default ident=\"modelIdentCode\" value=\"EX\"/>\n" +
                "  <default ident=\"senderIdent\" value=\"12345\"/>\n" +
                "  <default ident=\"receiverIdent\" value=\"54321\"/>\n" +
                "  <default ident=\"yearOfDataIssue\" value=\"2018\"/>\n" +
                "  <default ident=\"seqNumber\" value=\"00001\"/>\n" +
                "  <default ident=\"originator\" value=\"Default Sender\"/>\n" +
                "</defaults>\n");

            var (code, _, _) = Run(dir);
            Assert.Equal(0, code);
            var doc = LoadFromDir(dir, ExpectedName);
            Assert.Equal("Default Sender",
                doc.SelectSingleNode("//dispatchFrom/dispatchAddress/enterprise/enterpriseName")!.InnerText);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DumpTemplates_WritesDdnXml()
    {
        string dir = TempDir();
        try
        {
            var (code, _, _) = Run(dir, "-~", dir);
            Assert.Equal(0, code);
            string dumped = Path.Combine(dir, "ddn.xml");
            Assert.True(File.Exists(dumped));
            var doc = XmlUtils.ReadDoc(dumped);
            Assert.NotNull(doc.SelectSingleNode("//ddnIdent/ddnCode"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Version_PrintsToolName()
    {
        var (code, outText, _) = Run(Path.GetTempPath(), "--version");
        Assert.Equal(0, code);
        Assert.Contains("s1kd-newddn", outText);
        Assert.Contains("3.0.1", outText);
    }

    [Fact]
    public void RegisteredInToolRegistry()
    {
        Assert.NotNull(ToolRegistry.Resolve("newddn"));
        Assert.NotNull(ToolRegistry.Resolve("s1kd-newddn"));
    }
}
