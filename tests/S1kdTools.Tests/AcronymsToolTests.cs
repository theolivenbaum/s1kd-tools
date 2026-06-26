using System.Xml;
using S1kdTools;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class AcronymsToolTests
{
    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new AcronymsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"s1kd-acronyms-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static XmlDocument Parse(string xml)
    {
        var doc = XmlUtils.NewDocument();
        doc.LoadXml(xml);
        return doc;
    }

    // A data module with two acronyms already marked up, one of them twice.
    private const string MarkedUp =
        "<?xml version=\"1.0\"?>\n" +
        "<dmodule><identAndStatusSection/><content><description>" +
        "<para>The <acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
        "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym> spec " +
        "uses <acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
        "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym> and " +
        "<acronym acronymType=\"at01\"><acronymTerm>SNS</acronymTerm>" +
        "<acronymDefinition>Standard Numbering System</acronymDefinition></acronym>.</para>" +
        "</description></content></dmodule>";

    // A plain data module with no markup, for markup mode.
    private const string Plain =
        "<?xml version=\"1.0\"?>\n" +
        "<dmodule><identAndStatusSection/><content><description>" +
        "<para>The XML spec defines SNS handling for XML data.</para>" +
        "</description></content></dmodule>";

    private const string AcronymsList =
        "<?xml version=\"1.0\"?>\n" +
        "<acronyms>" +
        "<acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
        "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym>" +
        "<acronym acronymType=\"at01\"><acronymTerm>SNS</acronymTerm>" +
        "<acronymDefinition>Standard Numbering System</acronymDefinition></acronym>" +
        "</acronyms>";

    // --------------------------------------------------------------------
    // Find / list mode
    // --------------------------------------------------------------------

    [Fact]
    public void Find_ListsUniqueAcronymsAsText()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, MarkedUp);

            var (code, outText, _) = Run(mod);
            Assert.Equal(0, code);

            // XML appears twice in the module but should be deduped to one row.
            string[] lines = outText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);

            // Tab-separated: term \t type \t definition.
            Assert.Contains(lines, l => l.StartsWith("XML\t") && l.Contains("Extensible Markup Language"));
            Assert.Contains(lines, l => l.StartsWith("SNS\t") && l.Contains("Standard Numbering System"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Find_XmlOutput_HasUniqueAcronymElements()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, MarkedUp);

            var (code, outText, _) = Run("-x", mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal(2, doc.SelectNodes("//acronym")!.Count);
            Assert.NotNull(doc.SelectSingleNode("//acronym/acronymTerm[.='XML']"));
            Assert.NotNull(doc.SelectSingleNode("//acronym/acronymTerm[.='SNS']"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Find_DefinitionListFormat()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, MarkedUp);

            var (code, outText, _) = Run("-d", mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.NotNull(doc.SelectSingleNode("/definitionList"));
            Assert.Equal(2, doc.SelectNodes("//definitionListItem")!.Count);
            Assert.NotNull(doc.SelectSingleNode("//listItemTerm[.='XML']"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Find_TableFormat()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, MarkedUp);

            var (code, outText, _) = Run("-t", mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.NotNull(doc.SelectSingleNode("/table/tgroup/tbody"));
            Assert.Equal(2, doc.SelectNodes("//row")!.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Find_TypesFilter_OnlyKeepsMatchingType()
    {
        string dir = TempDir();
        try
        {
            // Two acronyms with different types.
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description><para>" +
                "<acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
                "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym> " +
                "<acronym acronymType=\"at02\"><acronymTerm>term</acronymTerm>" +
                "<acronymDefinition>A defined word</acronymDefinition></acronym>" +
                "</para></description></content></dmodule>");

            var (code, outText, _) = Run("-x", "-T", "at01", mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal(1, doc.SelectNodes("//acronym")!.Count);
            Assert.NotNull(doc.SelectSingleNode("//acronym[@acronymType='at01']"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Find_PrettyText_PadsColumns()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, MarkedUp);

            var (code, outText, _) = Run("-p", mod);
            Assert.Equal(0, code);

            // No tabs in pretty mode; columns are space-padded.
            Assert.DoesNotContain('\t', outText);
            Assert.Contains("Extensible Markup Language", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    // --------------------------------------------------------------------
    // Markup mode
    // --------------------------------------------------------------------

    [Fact]
    public void Markup_InsertsAcronymElementsAroundTerms()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod, Plain);
            File.WriteAllText(acr, AcronymsList);

            var (code, outText, _) = Run("-M", acr, mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // XML appears twice, SNS once => the first occurrence of each is a
            // full <acronym>; later repeats become <acronymTerm internalRefId>.
            Assert.True(doc.SelectNodes("//acronym")!.Count >= 2);
            Assert.NotNull(doc.SelectSingleNode("//acronym/acronymTerm[.='XML']"));
            Assert.NotNull(doc.SelectSingleNode("//acronym/acronymTerm[.='SNS']"));
            // A generated definition id should be present.
            Assert.NotNull(doc.SelectSingleNode("//acronymDefinition[@id]"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Markup_RepeatedTerm_UsesInternalRefId()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod, Plain);
            File.WriteAllText(acr, AcronymsList);

            var (code, outText, _) = Run("-M", acr, mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // The second XML occurrence references the first definition.
            XmlNode? refNode = doc.SelectSingleNode("//acronymTerm[@internalRefId]");
            Assert.NotNull(refNode);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Markup_Overwrite_WritesBackToFile()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod, Plain);
            File.WriteAllText(acr, AcronymsList);

            var (code, outText, _) = Run("-M", acr, "-f", mod);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, outText);

            Assert.True(XmlUtils.ReadDoc(mod).SelectNodes("//acronym")!.Count >= 2);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Markup_CustomXPath_RestrictsSearch()
    {
        string dir = TempDir();
        try
        {
            // Term appears in <para> and in <title>; restrict to title only.
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description>" +
                "<title>XML title</title>" +
                "<para>XML body</para>" +
                "</description></content></dmodule>");
            File.WriteAllText(acr,
                "<acronyms><acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
                "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym></acronyms>");

            var (code, outText, _) = Run("-M", acr, "-X", "//title/text()", mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // Only the title should have been marked up.
            Assert.NotNull(doc.SelectSingleNode("//title/acronym"));
            Assert.Null(doc.SelectSingleNode("//para/acronym"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Markup_DeferChoice_EmitsChooseAcronym()
    {
        string dir = TempDir();
        try
        {
            // Two definitions for the same term forces a choice.
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description><para>Use XML here.</para></description></content></dmodule>");
            File.WriteAllText(acr,
                "<acronyms>" +
                "<acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
                "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym>" +
                "<acronym acronymType=\"at02\"><acronymTerm>XML</acronymTerm>" +
                "<acronymDefinition>X Markup Lang</acronymDefinition></acronym>" +
                "</acronyms>");

            var (code, outText, _) = Run("-M", acr, "-!", mod);
            Assert.Equal(0, code);
            // delete.xsl is not applied here, so chooseAcronym should survive into output.
            Assert.Contains("chooseAcronym", outText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Markup_MissingList_ExitsWithCodeOne()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, Plain);

            var (code, _, err) = Run("-M", Path.Combine(dir, "nope.xml"), mod);
            Assert.Equal(1, code);
            Assert.Contains("Could not read acronyms list", err);
        }
        finally { Directory.Delete(dir, true); }
    }

    // --------------------------------------------------------------------
    // Delete / preformat modes
    // --------------------------------------------------------------------

    [Fact]
    public void Delete_FlattensMarkupToTerm()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, MarkedUp);

            var (code, outText, _) = Run("-D", mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            // No acronym markup remains.
            Assert.Equal(0, doc.SelectNodes("//acronym")!.Count);
            // The term text survives in the para.
            string para = doc.SelectSingleNode("//para")!.InnerText;
            Assert.Contains("XML", para);
            Assert.Contains("SNS", para);
            Assert.DoesNotContain("Extensible Markup Language", para);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Preformat_FlattensToDefinitionWithTermInBrackets()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, MarkedUp);

            var (code, outText, _) = Run("-P", mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal(0, doc.SelectNodes("//acronym")!.Count);
            string para = doc.SelectSingleNode("//para")!.InnerText;
            Assert.Contains("Extensible Markup Language (XML)", para);
            Assert.Contains("Standard Numbering System (SNS)", para);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Delete_Overwrite_WritesBackToFile()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod, MarkedUp);

            var (code, outText, _) = Run("-D", "-f", mod);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, outText);

            Assert.Equal(0, XmlUtils.ReadDoc(mod).SelectNodes("//acronym")!.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    // --------------------------------------------------------------------
    // List input mode
    // --------------------------------------------------------------------

    [Fact]
    public void List_Find_ReadsFilenamesFromListFile()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string lst = Path.Combine(dir, "list.txt");
            File.WriteAllText(mod, MarkedUp);
            File.WriteAllText(lst, mod + "\n");

            var (code, outText, _) = Run("-l", lst);
            Assert.Equal(0, code);

            string[] lines = outText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    // --------------------------------------------------------------------
    // Misc
    // --------------------------------------------------------------------

    [Fact]
    public void RemoveDeleted_ExcludesDeletedAcronyms()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description><para>" +
                "<acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
                "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym> " +
                "<deletedTerm change=\"delete\"><acronym acronymType=\"at01\"><acronymTerm>OLD</acronymTerm>" +
                "<acronymDefinition>Old Term</acronymDefinition></acronym></deletedTerm>" +
                "</para></description></content></dmodule>");

            var (code, outText, _) = Run("-x", "-^", mod);
            Assert.Equal(0, code);

            var doc = Parse(outText);
            Assert.Equal(1, doc.SelectNodes("//acronym")!.Count);
            Assert.Null(doc.SelectSingleNode("//acronymTerm[.='OLD']"));
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
        Assert.Contains("2.0.0", vOut);
    }

    // --------------------------------------------------------------------
    // Interactive markup (-i / -I) with injected stdin
    // --------------------------------------------------------------------

    private static (int code, string outText, string errText) RunInteractive(
        string stdin, params string[] args)
    {
        var tool = new AcronymsTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var input = new StringReader(stdin);
        int code = tool.Run(args, stdout, stderr, input);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // Two definitions for the same term, forcing an interactive choice.
    private static string TwoDefList() =>
        "<acronyms>" +
        "<acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
        "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym>" +
        "<acronym acronymType=\"at02\"><acronymTerm>XML</acronymTerm>" +
        "<acronymDefinition>X Markup Lang</acronymDefinition></acronym>" +
        "</acronyms>";

    [Fact]
    public void Interactive_PromptsAndChoosesDefinition()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description><para>Use XML here.</para></description></content></dmodule>");
            File.WriteAllText(acr, TwoDefList());

            // Choose definition 2 ("X Markup Lang").
            var (code, outText, _) = RunInteractive("2\n", "-i", "-M", acr, mod);

            Assert.Equal(0, code);
            // The prompt text mirrors the C tool.
            Assert.Contains("Found acronym term XML in the following context:", outText);
            Assert.Contains("Choose definition:", outText);
            Assert.Contains("1) Extensible Markup Language", outText);
            Assert.Contains("2) X Markup Lang", outText);
            Assert.Contains("s) Ignore this one", outText);

            // The resulting document should contain the chosen definition.
            var doc = Parse(outText.Substring(outText.IndexOf("<dmodule", StringComparison.Ordinal)));
            Assert.NotNull(doc.SelectSingleNode("//acronym[acronymDefinition='X Markup Lang']"));
            Assert.Null(doc.SelectSingleNode("//acronym[acronymDefinition='Extensible Markup Language']"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Interactive_IgnoreChoice_InsertsIgnoredAcronym()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description><para>Use XML here.</para></description></content></dmodule>");
            File.WriteAllText(acr, TwoDefList());

            // 's' (or any non-digit) ignores this occurrence.
            var (code, outText, _) = RunInteractive("s\n", "-i", "-M", acr, mod);

            Assert.Equal(0, code);
            string xml = outText.Substring(outText.IndexOf("<dmodule", StringComparison.Ordinal));
            var doc = Parse(xml);
            // No acronym markup should have been inserted.
            Assert.Null(doc.SelectSingleNode("//acronym"));
            Assert.Contains("XML", doc.SelectSingleNode("//para")!.InnerText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Interactive_ApplyToAll_ReusesChoiceForRemainingOccurrences()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            // Two occurrences of XML.
            File.WriteAllText(mod,
                "<dmodule><content><description><para>Use XML and more XML here.</para></description></content></dmodule>");
            File.WriteAllText(acr, TwoDefList());

            // First occurrence: choose 1 and apply to all ("1a"). The second
            // occurrence must reuse the choice without prompting again.
            var (code, outText, _) = RunInteractive("1a\n", "-i", "-M", acr, mod);

            Assert.Equal(0, code);
            // Only one prompt should have been shown (one "Choose definition:");
            // the second occurrence reuses the recorded "apply to all" choice.
            int prompts = CountOccurrences(outText, "Choose definition:");
            Assert.Equal(1, prompts);

            string xml = outText.Substring(outText.IndexOf("<dmodule", StringComparison.Ordinal));
            var doc = Parse(xml);
            // The chosen definition (at01) is used; the alternate (at02) never is.
            Assert.NotNull(doc.SelectSingleNode("//acronym[acronymDefinition='Extensible Markup Language']"));
            Assert.Null(doc.SelectSingleNode("//acronymDefinition[.='X Markup Lang']"));
            // Both occurrences are marked up: a full acronym for the first, and a
            // back-reference (term/id resolution) for the repeat.
            Assert.NotNull(doc.SelectSingleNode("//acronymTerm[@internalRefId]"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Interactive_AlwaysAsk_PromptsForSingleDefinition()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description><para>Use XML here.</para></description></content></dmodule>");
            // Single definition: -i alone would NOT prompt, but -I (always-ask) does.
            File.WriteAllText(acr,
                "<acronyms>" +
                "<acronym acronymType=\"at01\"><acronymTerm>XML</acronymTerm>" +
                "<acronymDefinition>Extensible Markup Language</acronymDefinition></acronym>" +
                "</acronyms>");

            var (code, outText, _) = RunInteractive("1\n", "-I", "-M", acr, mod);

            Assert.Equal(0, code);
            Assert.Contains("Choose definition:", outText);
            var doc = Parse(outText.Substring(outText.IndexOf("<dmodule", StringComparison.Ordinal)));
            Assert.NotNull(doc.SelectSingleNode("//acronym[acronymDefinition='Extensible Markup Language']"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Interactive_ExhaustedInput_IgnoresOccurrence()
    {
        string dir = TempDir();
        try
        {
            string mod = Path.Combine(dir, "mod.xml");
            string acr = Path.Combine(dir, "acr.xml");
            File.WriteAllText(mod,
                "<dmodule><content><description><para>Use XML here.</para></description></content></dmodule>");
            File.WriteAllText(acr, TwoDefList());

            // Empty stdin: EOF on the first read => occurrence ignored.
            var (code, outText, _) = RunInteractive(string.Empty, "-i", "-M", acr, mod);

            Assert.Equal(0, code);
            var doc = Parse(outText.Substring(outText.IndexOf("<dmodule", StringComparison.Ordinal)));
            Assert.Null(doc.SelectSingleNode("//acronym"));
        }
        finally { Directory.Delete(dir, true); }
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
