using System.Text;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

public class RenderToolTests
{
    // A minimal, self-contained XSL-FO document (no DOCTYPE, no external refs).
    private const string Fo =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<fo:root xmlns:fo=\"http://www.w3.org/1999/XSL/Format\">" +
        "<fo:layout-master-set>" +
        "<fo:simple-page-master master-name=\"p\" page-width=\"210mm\" page-height=\"297mm\" margin=\"20mm\">" +
        "<fo:region-body/></fo:simple-page-master></fo:layout-master-set>" +
        "<fo:page-sequence master-reference=\"p\"><fo:flow flow-name=\"xsl-region-body\">" +
        "<fo:block font-size=\"18pt\" font-weight=\"bold\">Hello S1000D</fo:block>" +
        "<fo:block>Rendered via FOP.Sharp.</fo:block>" +
        "</fo:flow></fo:page-sequence></fo:root>";

    // A second FO document, reusing the same master name ("p") so the merge can
    // be checked for master de-duplication.
    private const string Fo2 =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<fo:root xmlns:fo=\"http://www.w3.org/1999/XSL/Format\">" +
        "<fo:layout-master-set>" +
        "<fo:simple-page-master master-name=\"p\" page-width=\"210mm\" page-height=\"297mm\" margin=\"20mm\">" +
        "<fo:region-body/></fo:simple-page-master></fo:layout-master-set>" +
        "<fo:page-sequence master-reference=\"p\"><fo:flow flow-name=\"xsl-region-body\">" +
        "<fo:block font-size=\"18pt\" font-weight=\"bold\">Second document</fo:block>" +
        "</fo:flow></fo:page-sequence></fo:root>";

    // A tiny S1000D-ish source document plus a presentation stylesheet that
    // turns it into XSL-FO, exercising the transform path and a parameter.
    private const string Source =
        "<doc><title>Maintenance</title><para>One.</para><para>Two.</para></doc>";

    private const string Stylesheet =
        "<?xml version=\"1.0\"?>" +
        "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" " +
        "xmlns:fo=\"http://www.w3.org/1999/XSL/Format\">" +
        "<xsl:param name=\"heading\" select=\"'Untitled'\"/>" +
        "<xsl:template match=\"/doc\">" +
        "<fo:root xmlns:fo=\"http://www.w3.org/1999/XSL/Format\">" +
        "<fo:layout-master-set>" +
        "<fo:simple-page-master master-name=\"p\" page-width=\"210mm\" page-height=\"297mm\" margin=\"20mm\">" +
        "<fo:region-body/></fo:simple-page-master></fo:layout-master-set>" +
        "<fo:page-sequence master-reference=\"p\"><fo:flow flow-name=\"xsl-region-body\">" +
        "<fo:block font-size=\"16pt\" font-weight=\"bold\">" +
        "<xsl:value-of select=\"$heading\"/>: <xsl:value-of select=\"title\"/></fo:block>" +
        "<xsl:for-each select=\"para\"><fo:block><xsl:value-of select=\".\"/></fo:block></xsl:for-each>" +
        "</fo:flow></fo:page-sequence></fo:root>" +
        "</xsl:template></xsl:stylesheet>";

    private static (int code, string outText, string errText) Run(params string[] args)
    {
        var tool = new RenderTool();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args.ToList(), stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempFile(string ext, string? content = null)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-render-{Guid.NewGuid():N}{ext}");
        if (content != null)
        {
            File.WriteAllText(path, content);
        }
        return path;
    }

    [Fact]
    public void Render_Pdf_ProducesPdfBytes()
    {
        byte[] pdf = RenderTool.Render(Fo, RenderTool.RenderFormat.Pdf);

        Assert.True(pdf.Length > 0);
        // PDF files begin with the "%PDF-" magic.
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public void Render_NativePdf_ProducesPdfBytes()
    {
        byte[] pdf = RenderTool.Render(Fo, RenderTool.RenderFormat.Pdf, native: true);

        Assert.True(pdf.Length > 0);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public void Render_Text_ContainsContent()
    {
        string text = Encoding.UTF8.GetString(RenderTool.Render(Fo, RenderTool.RenderFormat.Text));

        Assert.Contains("Hello S1000D", text);
        Assert.Contains("Rendered via FOP.Sharp.", text);
    }

    [Fact]
    public void Render_Markdown_EmitsHeading()
    {
        string md = Encoding.UTF8.GetString(RenderTool.Render(Fo, RenderTool.RenderFormat.Markdown));

        // The bold/large first block becomes a Markdown heading.
        Assert.Contains("Hello S1000D", md);
        Assert.Contains("#", md);
    }

    [Fact]
    public void Render_Html_IsHtmlDocument()
    {
        string html = Encoding.UTF8.GetString(RenderTool.Render(Fo, RenderTool.RenderFormat.Html));

        Assert.Contains("<html>", html);
        Assert.Contains("Hello S1000D", html);
    }

    [Fact]
    public void Cli_FoInput_RendersTextToFile()
    {
        string fo = TempFile(".fo", Fo);
        string outPath = TempFile(".txt");
        try
        {
            var (code, _, err) = Run("-F", "-o", outPath, fo);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, err);
            Assert.Contains("Hello S1000D", File.ReadAllText(outPath));
        }
        finally
        {
            File.Delete(fo);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Cli_FoInput_InfersPdfFromExtension()
    {
        string fo = TempFile(".fo", Fo);
        string outPath = TempFile(".pdf");
        try
        {
            var (code, _, _) = Run("-F", "-o", outPath, fo);

            Assert.Equal(0, code);
            byte[] bytes = File.ReadAllBytes(outPath);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        }
        finally
        {
            File.Delete(fo);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Cli_TransformPath_AppliesStylesheetAndParam()
    {
        string xml = TempFile(".xml", Source);
        string xsl = TempFile(".xsl", Stylesheet);
        string outPath = TempFile(".md");
        try
        {
            var (code, _, err) = Run("-s", xsl, "-p", "heading=DM", "-t", "md", "-o", outPath, xml);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, err);
            string md = File.ReadAllText(outPath);
            Assert.Contains("DM: Maintenance", md);
            Assert.Contains("One.", md);
            Assert.Contains("Two.", md);
        }
        finally
        {
            File.Delete(xml);
            File.Delete(xsl);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Cli_NoStylesheetAndNoFoFlag_IsAnError()
    {
        var (code, _, err) = Run("-o", "out.pdf", "input.xml");

        Assert.Equal(2, code);
        Assert.Contains("stylesheet", err);
    }

    [Fact]
    public void Cli_UnknownFormat_IsAnError()
    {
        var (code, _, err) = Run("-F", "-t", "bogus", "in.fo");

        Assert.Equal(2, code);
        Assert.Contains("Unknown format", err);
    }

    [Fact]
    public void Cli_MultipleInputsWithExplicitOutput_MergeIntoOneDocument()
    {
        string a = TempFile(".fo", Fo);
        string b = TempFile(".fo", Fo2);
        string outPath = TempFile(".txt");
        try
        {
            var (code, _, err) = Run("-F", "-o", outPath, a, b);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, err);
            // Both objects' content ends up in the single merged output.
            string text = File.ReadAllText(outPath);
            Assert.Contains("Hello S1000D", text);
            Assert.Contains("Second document", text);
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Cli_MultipleInputsWithExplicitOutput_ProduceOnePdf()
    {
        string a = TempFile(".fo", Fo);
        string b = TempFile(".fo", Fo2);
        string outPath = TempFile(".pdf");
        try
        {
            var (code, _, _) = Run("-F", "-o", outPath, a, b);

            Assert.Equal(0, code);
            byte[] bytes = File.ReadAllBytes(outPath);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void MergeFo_ConcatenatesPageSequencesAndDedupesMasters()
    {
        var d1 = new System.Xml.XmlDocument { PreserveWhitespace = true };
        d1.LoadXml(Fo);
        var d2 = new System.Xml.XmlDocument { PreserveWhitespace = true };
        d2.LoadXml(Fo2);

        System.Xml.XmlDocument merged = RenderTool.MergeFo(new[] { d1, d2 });
        var ns = new System.Xml.XmlNamespaceManager(merged.NameTable);
        ns.AddNamespace("fo", "http://www.w3.org/1999/XSL/Format");

        // Both page-sequences are present...
        Assert.Equal(2, merged.SelectNodes("/fo:root/fo:page-sequence", ns)!.Count);
        // ...under a single layout-master-set whose identically-named masters
        // (both fixtures use master "p") are deduped to one.
        Assert.Equal(1, merged.SelectNodes("/fo:root/fo:layout-master-set", ns)!.Count);
        Assert.Equal(1, merged.SelectNodes(
            "/fo:root/fo:layout-master-set/fo:simple-page-master", ns)!.Count);
    }

    [Fact]
    public void Render_StreamToStream_WritesOutput()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Fo));
        using var output = new MemoryStream();

        RenderTool.Render(input, output, RenderTool.RenderFormat.Text);

        string text = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("Hello S1000D", text);
    }

    [Fact]
    public void Cli_DerivesOutputNameWhenNoOutputGiven()
    {
        string fo = TempFile(".fo", Fo);
        string derived = Path.ChangeExtension(fo, ".md");
        try
        {
            var (code, _, _) = Run("-F", "-t", "md", fo);

            Assert.Equal(0, code);
            Assert.True(File.Exists(derived));
            Assert.Contains("Hello S1000D", File.ReadAllText(derived));
        }
        finally
        {
            File.Delete(fo);
            File.Delete(derived);
        }
    }

    [Fact]
    public void Cli_HelpAndVersion_Succeed()
    {
        var (helpCode, helpOut, _) = Run("--help");
        Assert.Equal(0, helpCode);
        Assert.Contains("Usage: s1kd-render", helpOut);

        var (verCode, verOut, _) = Run("--version");
        Assert.Equal(0, verCode);
        Assert.Contains("s1kd-render", verOut);
    }

    [Fact]
    public void Tool_IsRegistered()
    {
        ITool? tool = ToolRegistry.Resolve("render");
        Assert.NotNull(tool);
        Assert.IsType<RenderTool>(tool);

        // Multi-call name resolves too.
        Assert.NotNull(ToolRegistry.Resolve("s1kd-render"));
    }
}
