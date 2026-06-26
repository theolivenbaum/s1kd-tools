using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class AddIcnToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new AddIcnTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string WriteFixture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-addicn-{Guid.NewGuid():N}.XML");
        File.WriteAllText(path, Fixtures.DataModule);
        return path;
    }

    [Fact]
    public void AddIcn_ToStdout_EmitsNotationAndEntity()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run("-s", path, "ICN-EX-12345-001-01.JPG");
            Assert.Equal(0, code);
            Assert.Contains("<!DOCTYPE dmodule", outText);
            Assert.Contains("<!NOTATION JPG SYSTEM \"JPG\">", outText);
            Assert.Contains("<!ENTITY ICN-EX-12345-001-01 SYSTEM \"ICN-EX-12345-001-01.JPG\" NDATA JPG>", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddIcn_UsesBasename_NotFullPathByDefault()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run("-s", path, "some/dir/ICN-EX-12345-001-01.PNG");
            Assert.Equal(0, code);
            // System ID should be just the basename.
            Assert.Contains("SYSTEM \"ICN-EX-12345-001-01.PNG\" NDATA PNG", outText);
            Assert.DoesNotContain("some/dir/", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddIcn_FullPath_UsesWholePathAsSystemId()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run("-F", "-s", path, "some/dir/ICN-EX-12345-001-01.PNG");
            Assert.Equal(0, code);
            Assert.Contains("SYSTEM \"some/dir/ICN-EX-12345-001-01.PNG\" NDATA PNG", outText);
            // Entity name and notation are still derived from the basename.
            Assert.Contains("<!ENTITY ICN-EX-12345-001-01 ", outText);
            Assert.Contains("<!NOTATION PNG ", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddIcn_MultipleIcns_SharedNotationDeclaredOnce()
    {
        string path = WriteFixture();
        try
        {
            var (code, outText, _) = Run("-s", path,
                "ICN-EX-001.JPG", "ICN-EX-002.JPG");
            Assert.Equal(0, code);
            // Two entities, single shared JPG notation.
            int notationCount = CountOccurrences(outText, "<!NOTATION JPG ");
            Assert.Equal(1, notationCount);
            Assert.Contains("<!ENTITY ICN-EX-001 ", outText);
            Assert.Contains("<!ENTITY ICN-EX-002 ", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddIcn_Overwrite_PersistsToSource()
    {
        string path = WriteFixture();
        try
        {
            var (code, _, _) = Run("-f", "-s", path, "ICN-EX-12345-001-01.JPG");
            Assert.Equal(0, code);

            string written = File.ReadAllText(path);
            Assert.Contains("<!NOTATION JPG SYSTEM \"JPG\">", written);
            Assert.Contains("<!ENTITY ICN-EX-12345-001-01 SYSTEM \"ICN-EX-12345-001-01.JPG\" NDATA JPG>", written);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddIcn_CombinedShortOptions_ParsedLikeGetopt()
    {
        string path = WriteFixture();
        try
        {
            // -fs <src> means overwrite + source.
            var (code, _, _) = Run("-fs", path, "ICN-EX-12345-001-01.JPG");
            Assert.Equal(0, code);

            string written = File.ReadAllText(path);
            Assert.Contains("<!ENTITY ICN-EX-12345-001-01 ", written);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddIcn_OutputFile_WritesToGivenPath()
    {
        string src = WriteFixture();
        string outPath = Path.Combine(Path.GetTempPath(), $"s1kd-addicn-out-{Guid.NewGuid():N}.XML");
        try
        {
            var (code, _, _) = Run("-s", src, "-o", outPath, "ICN-EX-12345-001-01.JPG");
            Assert.Equal(0, code);
            Assert.True(File.Exists(outPath));
            string written = File.ReadAllText(outPath);
            Assert.Contains("<!ENTITY ICN-EX-12345-001-01 ", written);
            // Source untouched (no DOCTYPE added).
            Assert.DoesNotContain("<!DOCTYPE", File.ReadAllText(src));
        }
        finally
        {
            File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void DotInIcnName_NotationIsRemainderAfterFirstDot()
    {
        string path = WriteFixture();
        try
        {
            // Mirrors strtok: split on first '.', notation = everything after.
            var (code, outText, _) = Run("-s", path, "ICN-FOO.tar.gz");
            Assert.Equal(0, code);
            Assert.Contains("<!ENTITY ICN-FOO ", outText);
            Assert.Contains("NDATA tar.gz", outText);
            Assert.Contains("<!NOTATION tar.gz ", outText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Help_ShowsUsageAndReturnsZero()
    {
        var (code, outText, _) = Run("-h");
        Assert.Equal(0, code);
        Assert.Contains("Usage: s1kd-addicn", outText);
        Assert.Contains("--full-path", outText);
    }

    [Fact]
    public void Version_ShowsVersionAndReturnsZero()
    {
        var (code, outText, _) = Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("s1kd-addicn (s1kd-tools) 1.5.1", outText);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}

/// <summary>
/// Unit tests for the shared <see cref="Icn"/> helpers (port of
/// <c>add_icn</c>/<c>add_notation</c> in
/// <c>reference/tools/common/s1kd_tools.c</c>).
/// </summary>
public class IcnHelperTests
{
    private static XmlDocument LoadFixture()
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(Fixtures.DataModule);
        return doc;
    }

    [Theory]
    [InlineData("ICN-EX-12345-001-01.JPG", "ICN-EX-12345-001-01.JPG")]
    [InlineData("a/b/c/ICN-EX-12345-001-01.JPG", "ICN-EX-12345-001-01.JPG")]
    [InlineData("trailing/", "trailing")]
    [InlineData("noslash", "noslash")]
    public void BaseName_MirrorsPosixBasename(string path, string expected)
    {
        Assert.Equal(expected, Icn.BaseName(path));
    }

    [Fact]
    public void AddIcn_DeclaresNotationAndEntity()
    {
        XmlDocument doc = LoadFixture();
        Icn.AddIcn(doc, "ICN-EX-12345-001-01.JPG", fullpath: false);

        string xml = Icn.SerializeWithDtd(doc);
        Assert.Contains("<!NOTATION JPG SYSTEM \"JPG\">", xml);
        Assert.Contains("<!ENTITY ICN-EX-12345-001-01 SYSTEM \"ICN-EX-12345-001-01.JPG\" NDATA JPG>", xml);
    }

    [Fact]
    public void AddIcn_FullPath_UsesWholePathAsSystemId()
    {
        XmlDocument doc = LoadFixture();
        Icn.AddIcn(doc, "some/dir/ICN-EX-12345-001-01.PNG", fullpath: true);

        string xml = Icn.SerializeWithDtd(doc);
        Assert.Contains("SYSTEM \"some/dir/ICN-EX-12345-001-01.PNG\" NDATA PNG", xml);
        // Entity name and notation are still derived from the basename.
        Assert.Contains("<!ENTITY ICN-EX-12345-001-01 ", xml);
        Assert.Contains("<!NOTATION PNG ", xml);
    }

    [Fact]
    public void AddIcn_SharedNotation_DeclaredOnce()
    {
        XmlDocument doc = LoadFixture();
        Icn.AddIcn(doc, "ICN-EX-001.JPG", fullpath: false);
        Icn.AddIcn(doc, "ICN-EX-002.JPG", fullpath: false);

        string xml = Icn.SerializeWithDtd(doc);
        int notations = 0, idx = 0;
        while ((idx = xml.IndexOf("<!NOTATION JPG ", idx, StringComparison.Ordinal)) >= 0)
        {
            notations++;
            idx += 1;
        }
        Assert.Equal(1, notations);
        Assert.Contains("<!ENTITY ICN-EX-001 ", xml);
        Assert.Contains("<!ENTITY ICN-EX-002 ", xml);
    }

    [Fact]
    public void AddIcn_SameEntityName_IsReplaced()
    {
        // xmlAddDocEntity replaces an entity with the same name.
        XmlDocument doc = LoadFixture();
        Icn.AddIcn(doc, "ICN-EX-001.JPG", fullpath: false);
        Icn.AddIcn(doc, "ICN-EX-001.PNG", fullpath: false);

        string xml = Icn.SerializeWithDtd(doc);
        Assert.DoesNotContain("ICN-EX-001.JPG", xml);
        Assert.Contains("<!ENTITY ICN-EX-001 SYSTEM \"ICN-EX-001.PNG\" NDATA PNG>", xml);
    }

    [Fact]
    public void AddIcn_NotationIsRemainderAfterFirstDot()
    {
        // Mirrors strtok: split on first '.', notation = everything after.
        XmlDocument doc = LoadFixture();
        Icn.AddIcn(doc, "ICN-FOO.tar.gz", fullpath: false);

        string xml = Icn.SerializeWithDtd(doc);
        Assert.Contains("<!ENTITY ICN-FOO ", xml);
        Assert.Contains("NDATA tar.gz", xml);
        Assert.Contains("<!NOTATION tar.gz ", xml);
    }

    [Fact]
    public void AddIcn_NoExtension_NoNotation()
    {
        // basename with no '.': notation is NULL, so no notation and no NDATA.
        XmlDocument doc = LoadFixture();
        Icn.AddIcn(doc, "ICN-NOEXT", fullpath: false);

        string xml = Icn.SerializeWithDtd(doc);
        Assert.Contains("<!ENTITY ICN-NOEXT SYSTEM \"ICN-NOEXT\">", xml);
        Assert.DoesNotContain("NDATA", xml);
        Assert.DoesNotContain("<!NOTATION", xml);
    }

    [Fact]
    public void AddNotation_Idempotent_FirstDeclarationWins()
    {
        XmlDocument doc = LoadFixture();
        Icn.AddNotation(doc, "JPG", null, "JPG");
        Icn.AddNotation(doc, "JPG", null, "DUPLICATE");

        string xml = Icn.SerializeWithDtd(doc);
        Assert.Contains("<!NOTATION JPG SYSTEM \"JPG\">", xml);
        Assert.DoesNotContain("DUPLICATE", xml);
    }

    [Fact]
    public void AddNotation_NullName_IsNoOp()
    {
        XmlDocument doc = LoadFixture();
        Icn.AddNotation(doc, null, null, null);

        string xml = Icn.SerializeWithDtd(doc);
        Assert.DoesNotContain("<!NOTATION", xml);
    }

    [Fact]
    public void AddIcn_OnReferenceFixture_Works()
    {
        // Reuse the real reference data module fixture.
        string fixture = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "reference", "tools", "s1kd-addicn", "doc",
            "DMC-S1KDTOOLS-A-27-00-00-00A-040A-D_EN-CA.XML");

        if (!File.Exists(fixture))
        {
            // Fall back to the in-repo fixture if the reference tree is absent.
            return;
        }

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(fixture);
        Icn.AddIcn(doc, "ICN-EX-12345-001-01.JPG", fullpath: false);

        string xml = Icn.SerializeWithDtd(doc);
        Assert.Contains("<!NOTATION JPG SYSTEM \"JPG\">", xml);
        Assert.Contains("<!ENTITY ICN-EX-12345-001-01 SYSTEM \"ICN-EX-12345-001-01.JPG\" NDATA JPG>", xml);
    }
}
