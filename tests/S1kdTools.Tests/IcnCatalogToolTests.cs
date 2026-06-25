using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class IcnCatalogToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new IcnCatalogTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private const string ObjectXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE dmodule [\n" +
        "<!NOTATION png SYSTEM \"png\">\n" +
        "<!ENTITY ICN-12345-00001-001-01 SYSTEM \"ICN-12345-00001-001-01.PNG\" NDATA png>\n" +
        "]>\n" +
        "<dmodule>\n" +
        "  <content>\n" +
        "    <graphic infoEntityIdent=\"ICN-12345-00001-001-01\"/>\n" +
        "  </content>\n" +
        "</dmodule>";

    private static string WriteTemp(string contents, string suffix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-icncat-{Guid.NewGuid():N}{suffix}");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Resolve_SingleIcn_RewritesSystemUri()
    {
        string catalog = WriteTemp(
            "<icnCatalog>\n" +
            "<icn infoEntityIdent=\"ICN-12345-00001-001-01\" uri=\"graphics/ICN-12345-00001-001-01.PNG\"/>\n" +
            "</icnCatalog>", ".xml");
        string obj = WriteTemp(ObjectXml, ".XML");
        try
        {
            var (code, outText, _) = Run("-c", catalog, obj);
            Assert.Equal(0, code);
            // The SYSTEM URI is replaced; the original NDATA notation is kept.
            Assert.Contains(
                "<!ENTITY ICN-12345-00001-001-01 SYSTEM \"graphics/ICN-12345-00001-001-01.PNG\" NDATA png>",
                outText);
            Assert.DoesNotContain("SYSTEM \"ICN-12345-00001-001-01.PNG\"", outText);
        }
        finally { File.Delete(catalog); File.Delete(obj); }
    }

    [Fact]
    public void Resolve_WithMediaAndNotation_AddsNotationAndSwitchesNdata()
    {
        string catalog = WriteTemp(
            "<icnCatalog>\n" +
            "<notation name=\"jpg\" systemId=\"jpg\"/>\n" +
            "<media name=\"pdf\">\n" +
            "<icn infoEntityIdent=\"ICN-12345-00001-001-01\" uri=\"ICN-12345-00001-001-01.JPG\" notation=\"jpg\"/>\n" +
            "</media>\n" +
            "</icnCatalog>", ".xml");
        string obj = WriteTemp(ObjectXml, ".XML");
        try
        {
            var (code, outText, _) = Run("-c", catalog, "-m", "pdf", obj);
            Assert.Equal(0, code);
            Assert.Contains("<!NOTATION jpg SYSTEM \"jpg\">", outText);
            Assert.Contains(
                "<!ENTITY ICN-12345-00001-001-01 SYSTEM \"ICN-12345-00001-001-01.JPG\" NDATA jpg>",
                outText);
        }
        finally { File.Delete(catalog); File.Delete(obj); }
    }

    [Fact]
    public void Resolve_PatternRule_UsesBackreferences()
    {
        string patternObj =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE dmodule [\n" +
            "<!NOTATION PNG SYSTEM \"PNG\">\n" +
            "<!ENTITY ICN-12345-00001-001-01 SYSTEM \"ICN-12345-00001-001-01\" NDATA PNG>\n" +
            "]>\n" +
            "<dmodule>\n" +
            "  <graphic infoEntityIdent=\"ICN-12345-00001-001-01\"/>\n" +
            "</dmodule>";
        string catalog = WriteTemp(
            "<icnCatalog>\n" +
            "<icn type=\"pattern\" infoEntityIdent=\"ICN-(.{5})-(.*)\" uri=\"graphics/\\1/ICN-\\1-\\2.PNG\" notation=\"PNG\"/>\n" +
            "</icnCatalog>", ".xml");
        string obj = WriteTemp(patternObj, ".XML");
        try
        {
            var (code, outText, _) = Run("-c", catalog, obj);
            Assert.Equal(0, code);
            Assert.Contains(
                "SYSTEM \"graphics/12345/ICN-12345-00001-001-01.PNG\"",
                outText);
        }
        finally { File.Delete(catalog); File.Delete(obj); }
    }

    [Fact]
    public void Add_AppendsIcnEntryToCatalog()
    {
        string catalog = WriteTemp("<?xml version=\"1.0\"?>\n<icnCatalog/>", ".xml");
        try
        {
            var (code, outText, _) = Run(
                "-c", catalog,
                "-a", "ICN-12345-00009-001-01",
                "-u", "graphics/ICN-12345-00009-001-01.PNG",
                "-n", "png");
            Assert.Equal(0, code);
            Assert.Contains("infoEntityIdent=\"ICN-12345-00009-001-01\"", outText);
            Assert.Contains("uri=\"graphics/ICN-12345-00009-001-01.PNG\"", outText);
            Assert.Contains("notation=\"png\"", outText);
        }
        finally { File.Delete(catalog); }
    }

    [Fact]
    public void Delete_RemovesIcnEntryFromCatalog()
    {
        string catalog = WriteTemp(
            "<?xml version=\"1.0\"?>\n" +
            "<icnCatalog>" +
            "<icn infoEntityIdent=\"ICN-12345-00001-001-01\" uri=\"a.png\"/>" +
            "<icn infoEntityIdent=\"ICN-12345-00002-001-01\" uri=\"b.png\"/>" +
            "</icnCatalog>", ".xml");
        try
        {
            var (code, outText, _) = Run("-c", catalog, "-d", "ICN-12345-00001-001-01");
            Assert.Equal(0, code);
            Assert.DoesNotContain("ICN-12345-00001-001-01", outText);
            Assert.Contains("ICN-12345-00002-001-01", outText);
        }
        finally { File.Delete(catalog); }
    }

    [Fact]
    public void Add_WithOverwrite_WritesCatalogFile()
    {
        string catalog = WriteTemp("<?xml version=\"1.0\"?>\n<icnCatalog/>", ".xml");
        try
        {
            var (code, _, _) = Run(
                "-c", catalog, "-f",
                "-a", "ICN-99999-00001-001-01",
                "-u", "x.png");
            Assert.Equal(0, code);
            string written = File.ReadAllText(catalog);
            Assert.Contains("ICN-99999-00001-001-01", written);
        }
        finally { File.Delete(catalog); }
    }

    [Fact]
    public void Resolve_Overwrite_UpdatesObjectInPlace()
    {
        string catalog = WriteTemp(
            "<icnCatalog>" +
            "<icn infoEntityIdent=\"ICN-12345-00001-001-01\" uri=\"graphics/ICN-12345-00001-001-01.PNG\"/>" +
            "</icnCatalog>", ".xml");
        string obj = WriteTemp(ObjectXml, ".XML");
        try
        {
            var (code, _, _) = Run("-c", catalog, "-f", obj);
            Assert.Equal(0, code);
            string written = File.ReadAllText(obj);
            Assert.Contains("SYSTEM \"graphics/ICN-12345-00001-001-01.PNG\"", written);
        }
        finally { File.Delete(catalog); File.Delete(obj); }
    }

    [Fact]
    public void Version_PrintsToolVersion()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("3.3.2", outText);
        Assert.Contains("icncatalog", outText);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("Usage: s1kd-icncatalog", outText);
    }
}
