using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class NeutralizeToolTests
{
    // A minimal S1000D 4.x data module with a dmRef in the content and the
    // descriptive metadata the rdf.xsl stylesheet reads. No DOCTYPE so the test
    // does not depend on DTD resolution.
    private const string DataModule =
        "<dmodule xmlns:dc=\"http://www.purl.org/dc/elements/1.1/\" " +
        "xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" " +
        "xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
        "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
        "xsi:noNamespaceSchemaLocation=\"http://www.s1000d.org/S1000D_4-2/xml_schema_flat/descript.xsd\">" +
        "<identAndStatusSection><dmAddress><dmIdent>" +
        "<dmCode modelIdentCode=\"XLINKTEST\" systemDiffCode=\"A\" systemCode=\"00\" " +
        "subSystemCode=\"0\" subSubSystemCode=\"0\" assyCode=\"00\" disassyCode=\"00\" " +
        "disassyCodeVariant=\"A\" infoCode=\"040\" infoCodeVariant=\"A\" itemLocationCode=\"D\"/>" +
        "<language languageIsoCode=\"en\" countryIsoCode=\"CA\"/>" +
        "<issueInfo issueNumber=\"000\" inWork=\"01\"/>" +
        "</dmIdent><dmAddressItems>" +
        "<issueDate year=\"2017\" month=\"08\" day=\"16\"/>" +
        "<dmTitle><techName>XLink test</techName><infoName>Example</infoName></dmTitle>" +
        "</dmAddressItems></dmAddress>" +
        "<dmStatus issueType=\"new\">" +
        "<security securityClassification=\"01\"/>" +
        "<responsiblePartnerCompany><enterpriseName>khzae.net</enterpriseName></responsiblePartnerCompany>" +
        "<originator><enterpriseName>khzae.net</enterpriseName></originator>" +
        "</dmStatus></identAndStatusSection>" +
        "<content><description><para>Refer to " +
        "<dmRef><dmRefIdent>" +
        "<dmCode modelIdentCode=\"XLINKTEST\" systemDiffCode=\"A\" systemCode=\"00\" " +
        "subSystemCode=\"0\" subSubSystemCode=\"0\" assyCode=\"01\" disassyCode=\"00\" " +
        "disassyCodeVariant=\"A\" infoCode=\"040\" infoCodeVariant=\"A\" itemLocationCode=\"D\"/>" +
        "</dmRefIdent><dmRefAddressItems><dmTitle>" +
        "<techName>XLink test</techName><infoName>Referenced data module</infoName>" +
        "</dmTitle></dmRefAddressItems></dmRef>.</para></description></content></dmodule>";

    private static (int code, string outText, string errText) Run(IEnumerable<string> args, string? stdin = null)
    {
        var tool = new NeutralizeTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args.ToList(), stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteFixture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-neut-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, DataModule);
        return path;
    }

    private static XmlDocument Parse(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        return doc;
    }

    private static XmlNamespaceManager Nsmgr(XmlDocument doc)
    {
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("xlink", "http://www.w3.org/1999/xlink");
        ns.AddNamespace("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        ns.AddNamespace("dc", "http://www.purl.org/dc/elements/1.1/");
        ns.AddNamespace("dm", "http://www.s1000d.org/dm");
        return ns;
    }

    [Fact]
    public void Neutralize_AddsXlinkHrefToDmRef()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(new[] { path });
            Assert.Equal(0, code);

            var doc = Parse(outText);
            var dmRef = doc.SelectSingleNode("//dmRef") as XmlElement;
            Assert.NotNull(dmRef);
            string href = dmRef!.GetAttribute("href", "http://www.w3.org/1999/xlink");
            Assert.Equal("URN:S1000D:DMC-XLINKTEST-A-00-00-01-00A-040A-D", href);
            Assert.Equal("simple", dmRef.GetAttribute("type", "http://www.w3.org/1999/xlink"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Neutralize_AddsRdfDublinCoreMetadata()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(new[] { path });
            Assert.Equal(0, code);

            var doc = Parse(outText);
            var ns = Nsmgr(doc);

            var desc = doc.SelectSingleNode("/dmodule/rdf:Description", ns);
            Assert.NotNull(desc);

            // Schema is S1000D_4-2 (not 2-0/2-1/2-2), so the lowercase
            // Dublin Core element names are produced (rdf.xsl xsl:otherwise).
            var title = doc.SelectSingleNode("//dc:title", ns);
            Assert.NotNull(title);
            Assert.Equal("XLink test - Example", title!.InnerText);

            var creator = doc.SelectSingleNode("//dc:creator", ns);
            Assert.Equal("khzae.net", creator!.InnerText);

            var ident = doc.SelectSingleNode("//dc:identifier", ns);
            Assert.Contains("XLINKTEST", ident!.InnerText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Neutralize_WithNamespace_AppliesDmNamespace()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(new[] { "-n", path });
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal("http://www.s1000d.org/dm", doc.DocumentElement!.NamespaceURI);
            Assert.Equal("dmodule", doc.DocumentElement.LocalName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Overwrite_WritesBackToFile()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run(new[] { "-f", path });
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, outText); // overwrite => nothing on stdout

            string written = File.ReadAllText(path);
            var doc = Parse(written);
            var dmRef = doc.SelectSingleNode("//dmRef") as XmlElement;
            Assert.NotNull(dmRef);
            Assert.False(string.IsNullOrEmpty(
                dmRef!.GetAttribute("href", "http://www.w3.org/1999/xlink")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Delete_RemovesNeutralMetadata()
    {
        // Neutralize first, then deneutralize, and assert the neutral artifacts
        // are gone (rdf:Description removed, xlink attributes stripped).
        var neutralized = NeutralizeTool.Neutralize(Parse(DataModule), false);
        string neutralXml = XmlUtils.ToXmlString(neutralized);
        Assert.Contains("xlink:href", neutralXml);
        Assert.Contains("Description", neutralXml);

        var deneutralized = NeutralizeTool.Deneutralize(Parse(neutralXml));
        var ns = Nsmgr(deneutralized);

        Assert.Null(deneutralized.SelectSingleNode("//rdf:Description", ns));
        var dmRef = deneutralized.SelectSingleNode("//dmRef") as XmlElement;
        Assert.NotNull(dmRef);
        Assert.Equal(string.Empty, dmRef!.GetAttribute("href", "http://www.w3.org/1999/xlink"));
    }

    [Fact]
    public void Version_PrintsVersionString()
    {
        var (code, outText, _) = Run(new[] { "--version" });
        Assert.Equal(0, code);
        Assert.Contains("1.11.0", outText);
        Assert.Contains("s1kd-neutralize", outText);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (code, outText, _) = Run(new[] { "--help" });
        Assert.Equal(0, code);
        Assert.Contains("Usage: s1kd-neutralize", outText);
        Assert.Contains("--namespace", outText);
    }

    [Fact]
    public void Registry_DiscoversNeutralizeTool()
    {
        var tool = ToolRegistry.Resolve("neutralize");
        Assert.IsType<NeutralizeTool>(tool);
        Assert.Equal("neutralize", tool!.Name);
    }
}
