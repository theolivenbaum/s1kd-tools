using System.Xml;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class SyncrefsToolTests
{
    // A data module whose content references two data modules (one of them twice)
    // and one publication module. The dmRefs are intentionally out of code order
    // so we can assert the generated <refs> is sorted.
    private const string DmWithRefs =
        """
        <dmodule>
          <identAndStatusSection/>
          <content>
            <description>
              <para>See <dmRef id="r1"><dmRefIdent><dmCode modelIdentCode="EX" systemDiffCode="A"
                systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00"
                disassyCode="00" disassyCodeVariant="A" infoCode="041" infoCodeVariant="A"
                itemLocationCode="D"/></dmRefIdent></dmRef> and
              <pmRef><pmRefIdent><pmCode modelIdentCode="EX" pmIssuer="12345" pmNumber="00000"
                pmVolume="00"/></pmRefIdent></pmRef> and
              <dmRef><dmRefIdent><dmCode modelIdentCode="EX" systemDiffCode="A"
                systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00"
                disassyCode="00" disassyCodeVariant="A" infoCode="040" infoCodeVariant="A"
                itemLocationCode="D"/></dmRefIdent></dmRef> and
              <dmRef><dmRefIdent><dmCode modelIdentCode="EX" systemDiffCode="A"
                systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00"
                disassyCode="00" disassyCodeVariant="A" infoCode="040" infoCodeVariant="A"
                itemLocationCode="D"/></dmRefIdent></dmRef>.</para>
            </description>
          </content>
        </dmodule>
        """;

    private const string DmWithExistingRefs =
        """
        <dmodule>
          <content>
            <refs>
              <dmRef id="stale"><dmRefIdent><dmCode modelIdentCode="OLD" systemDiffCode="A"
                systemCode="99" subSystemCode="9" subSubSystemCode="9" assyCode="99"
                disassyCode="99" disassyCodeVariant="A" infoCode="999" infoCodeVariant="A"
                itemLocationCode="D"/></dmRefIdent></dmRef>
            </refs>
            <description>
              <para>See <dmRef><dmRefIdent><dmCode modelIdentCode="EX" systemDiffCode="A"
                systemCode="00" subSystemCode="0" subSubSystemCode="0" assyCode="00"
                disassyCode="00" disassyCodeVariant="A" infoCode="040" infoCodeVariant="A"
                itemLocationCode="D"/></dmRefIdent></dmRef>.</para>
            </description>
          </content>
        </dmodule>
        """;

    private static (int code, string outText, string errText) Run(string fixturePath, params string[] args)
    {
        var tool = new SyncrefsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var all = new List<string>(args) { fixturePath };
        int code = tool.Run(all, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteFixture(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-syncrefs-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, xml);
        return path;
    }

    private static XmlDocument Parse(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        return doc;
    }

    [Fact]
    public void GeneratesRefsTable_FromContentReferences()
    {
        string path = WriteFixture(DmWithRefs);
        try
        {
            var (code, outText, _) = Run(path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            var refs = doc.SelectSingleNode("/dmodule/content/refs");
            Assert.NotNull(refs);

            // Two unique dmRefs + one pmRef = 3 entries (the duplicate dmRef is removed).
            var dmRefs = doc.SelectNodes("/dmodule/content/refs/dmRef");
            var pmRefs = doc.SelectNodes("/dmodule/content/refs/pmRef");
            Assert.Equal(2, dmRefs!.Count);
            Assert.Equal(1, pmRefs!.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RefsTable_IsSortedByCode()
    {
        string path = WriteFixture(DmWithRefs);
        try
        {
            var (_, outText, _) = Run(path);
            var doc = Parse(outText);

            // dmRefs sort before pmRefs ("0" prefix < "1" prefix), and within
            // dmRefs the 040 info code sorts before 041.
            var children = doc.SelectNodes("/dmodule/content/refs/*")!;
            Assert.Equal("dmRef", children[0]!.LocalName);
            Assert.Equal("dmRef", children[1]!.LocalName);
            Assert.Equal("pmRef", children[2]!.LocalName);

            string firstInfo = ((XmlElement)children[0]!.SelectSingleNode(".//dmCode")!)
                .GetAttribute("infoCode");
            string secondInfo = ((XmlElement)children[1]!.SelectSingleNode(".//dmCode")!)
                .GetAttribute("infoCode");
            Assert.Equal("040", firstInfo);
            Assert.Equal("041", secondInfo);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GeneratedRefs_HaveNoIdAttribute()
    {
        string path = WriteFixture(DmWithRefs);
        try
        {
            var (_, outText, _) = Run(path);
            var doc = Parse(outText);

            foreach (XmlElement r in doc.SelectNodes("/dmodule/content/refs/*")!)
            {
                Assert.False(r.HasAttribute("id"), "Copied references should have their id stripped.");
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExistingRefsTable_IsReplaced()
    {
        string path = WriteFixture(DmWithExistingRefs);
        try
        {
            var (code, outText, _) = Run(path);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // Only one refs element, containing the EX ref, not the stale OLD ref.
            var refsNodes = doc.SelectNodes("/dmodule/content/refs")!;
            Assert.Equal(1, refsNodes.Count);

            var codes = doc.SelectNodes("/dmodule/content/refs//dmCode")!;
            Assert.Equal(1, codes.Count);
            Assert.Equal("EX", ((XmlElement)codes[0]!).GetAttribute("modelIdentCode"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RefsTable_IsPlacedBeforeOtherContent()
    {
        string path = WriteFixture(DmWithRefs);
        try
        {
            var (_, outText, _) = Run(path);
            var doc = Parse(outText);
            var content = doc.SelectSingleNode("/dmodule/content")!;

            XmlElement? firstElem = null;
            foreach (XmlNode n in content.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Element) { firstElem = (XmlElement)n; break; }
            }
            Assert.NotNull(firstElem);
            Assert.Equal("refs", firstElem!.LocalName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DeleteOption_RemovesRefsTable()
    {
        string path = WriteFixture(DmWithExistingRefs);
        try
        {
            var (code, outText, _) = Run(path, "-d");
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Null(doc.SelectSingleNode("/dmodule/content/refs"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Overwrite_PersistsChangesToFile()
    {
        string path = WriteFixture(DmWithRefs);
        try
        {
            var (code, outText, _) = Run(path, "-f");
            Assert.Equal(0, code);
            // With -f the result is written to the file, not stdout.
            Assert.Equal(string.Empty, outText);

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(path);
            Assert.NotNull(doc.SelectSingleNode("/dmodule/content/refs"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NoReferences_LeavesNoRefsTable()
    {
        string xml =
            """
            <dmodule>
              <content>
                <description><para>Nothing to see here.</para></description>
              </content>
            </dmodule>
            """;
        string path = WriteFixture(xml);
        try
        {
            var (code, outText, _) = Run(path);
            Assert.Equal(0, code);
            var doc = Parse(outText);
            Assert.Null(doc.SelectSingleNode("/dmodule/content/refs"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Version_PrintsVersionString()
    {
        var tool = new SyncrefsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "--version" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("1.9.0", stdout.ToString());
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var tool = new SyncrefsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(new[] { "-h" }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("Usage:", stdout.ToString());
        Assert.Contains("--delete", stdout.ToString());
    }
}
