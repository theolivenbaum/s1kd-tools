using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class IndexToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new IndexTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // A minimal issue 4.x data module with a single para.
    private const string Module =
        "<?xml version=\"1.0\"?>\n" +
        "<dmodule>" +
        "<identAndStatusSection/>" +
        "<content><description>" +
        "<para>The s1kd-tools are a set of small tools for manipulating S1000D XML data.</para>" +
        "</description></content>" +
        "</dmodule>";

    private const string IndexFlags =
        "<?xml version=\"1.0\"?>\n" +
        "<indexFlags>" +
        "<indexFlag indexLevelOne=\"data\"/>" +
        "<indexFlag indexLevelOne=\"data\" indexLevelTwo=\"XML\"/>" +
        "<indexFlag indexLevelOne=\"S1000D\"/>" +
        "<indexFlag indexLevelOne=\"S1000D\" indexLevelTwo=\"s1kd-tools\"/>" +
        "</indexFlags>";

    private static XmlDocument Parse(string xml)
    {
        var doc = XmlUtils.NewDocument();
        doc.LoadXml(xml);
        return doc;
    }

    // --------------------------------------------------------------------

    [Fact]
    public void Flags_AreInsertedAfterMatchedTerms()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string idx = Path.Combine(dir, "idx.xml");
            File.WriteAllText(mod, Module);
            File.WriteAllText(idx, IndexFlags);

            var (code, outText, _) = Run("-I", idx, mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            var flags = doc.SelectNodes("//indexFlag")!;

            // Four index terms each appear once in the para => four flags.
            Assert.Equal(4, flags.Count);

            // A flag for each top-level term must be present.
            Assert.NotNull(doc.SelectSingleNode("//indexFlag[@indexLevelOne='data' and not(@indexLevelTwo)]"));
            Assert.NotNull(doc.SelectSingleNode("//indexFlag[@indexLevelOne='data' and @indexLevelTwo='XML']"));
            Assert.NotNull(doc.SelectSingleNode("//indexFlag[@indexLevelOne='S1000D' and not(@indexLevelTwo)]"));
            Assert.NotNull(doc.SelectSingleNode("//indexFlag[@indexLevelOne='S1000D' and @indexLevelTwo='s1kd-tools']"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Flag_IsInsertedImmediatelyAfterTermText()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string idx = Path.Combine(dir, "idx.xml");
            File.WriteAllText(mod, Module);
            // Only flag the single-level "S1000D" term to make positioning easy.
            File.WriteAllText(idx,
                "<indexFlags><indexFlag indexLevelOne=\"S1000D\"/></indexFlags>");

            var (code, outText, _) = Run("-I", idx, mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            var flag = (XmlElement)doc.SelectSingleNode("//indexFlag")!;

            // The text node directly before the flag must end with the term.
            var prev = flag.PreviousSibling!;
            Assert.Equal(XmlNodeType.Text, prev.NodeType);
            Assert.EndsWith("S1000D", prev.Value);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IgnoreCase_MatchesDifferentCasing()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string idx = Path.Combine(dir, "idx.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description>" +
                "<para>The DATA is here .</para>" +
                "</description></content></dmodule>");
            File.WriteAllText(idx,
                "<indexFlags><indexFlag indexLevelOne=\"data\"/></indexFlags>");

            // Without -i: no match (case differs).
            var (_, plain, _) = Run("-I", idx, mod);
            Assert.Null(Parse(plain).SelectSingleNode("//indexFlag"));

            // With -i: match.
            var (code, outText, _) = Run("-i", "-I", idx, mod);
            Assert.Equal(0, code);
            Assert.NotNull(Parse(outText).SelectSingleNode("//indexFlag"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Delete_RemovesExistingFlags()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string idx = Path.Combine(dir, "idx.xml");
            File.WriteAllText(mod, Module);
            File.WriteAllText(idx, IndexFlags);

            // Add flags, overwriting the file.
            var (addCode, _, _) = Run("-f", "-I", idx, mod);
            Assert.Equal(0, addCode);
            Assert.True(XmlUtils.ReadDoc(mod).SelectNodes("//indexFlag")!.Count > 0);

            // Now delete them.
            var (delCode, delOut, _) = Run("-D", mod);
            Assert.Equal(0, delCode);
            Assert.Equal(0, Parse(delOut).SelectNodes("//indexFlag")!.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Overwrite_WritesBackToFile()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string idx = Path.Combine(dir, "idx.xml");
            File.WriteAllText(mod, Module);
            File.WriteAllText(idx, IndexFlags);

            var (code, outText, _) = Run("-f", "-I", idx, mod);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, outText); // nothing printed to stdout

            Assert.Equal(4, XmlUtils.ReadDoc(mod).SelectNodes("//indexFlag")!.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingIndexFlags_ExitsWithCodeOne()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, Module);

            var (code, _, err) = Run("-I", Path.Combine(dir, "nope.xml"), mod);
            Assert.Equal(1, code);
            Assert.Contains("Could not read index flags", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Iss30_Module_ConvertsFlagsToIndxflag()
    {
        string dir = TempDir();
        try
        {
            // An issue 3.0 module: first element child of root is <idstatus>.
            string mod = Path.Combine(dir, "mod.xml");
            string idx = Path.Combine(dir, "idx.xml");
            File.WriteAllText(mod,
                "<dmodule><idstatus/><content><description>" +
                "<para>This is data here .</para>" +
                "</description></content></dmodule>");
            File.WriteAllText(idx,
                "<indexFlags><indexFlag indexLevelOne=\"data\"/></indexFlags>");

            var (code, outText, _) = Run("-I", idx, mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // indexFlag becomes indxflag and indexLevelOne becomes ref1.
            Assert.Equal(0, doc.SelectNodes("//indexFlag")!.Count);
            var flag = (XmlElement)doc.SelectSingleNode("//indxflag")!;
            Assert.NotNull(flag);
            Assert.Equal("data", flag.GetAttribute("ref1"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Help_And_Version_ReturnZero()
    {
        var (hCode, hOut, _) = Run("-h");
        Assert.Equal(0, hCode);
        Assert.Contains("Usage:", hOut);

        var (vCode, vOut, _) = Run("--version");
        Assert.Equal(0, vCode);
        Assert.Contains("1.10.0", vOut);
    }
}
