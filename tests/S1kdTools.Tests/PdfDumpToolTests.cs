using System.Text.Json;
using S1kdTools.Pdf;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

/// <summary>
/// Tests for <c>s1kd-pdfdump</c> and the layout measurements behind it. The measurements
/// are the interesting part: a stylesheet is written in margins, sizes and leading, so
/// the dump has to recover those from the ink rather than merely list what is on the page.
/// </summary>
public class PdfDumpToolTests : IDisposable
{
    private readonly List<string> _paths = new();

    public void Dispose()
    {
        foreach (string path in _paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Not worth failing a test over.
            }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Two pages with a running header, a running footer and a body at a known size.</summary>
    private static string Fo(double marginMm = 25, double fontSize = 10, double leadingPt = 12.5,
        bool furniture = true, int pages = 2)
    {
        string header = furniture
            ? """
              <fo:static-content flow-name="xsl-region-before">
                <fo:block font-size="8pt">MAINTENANCE MANUAL</fo:block>
              </fo:static-content>
              <fo:static-content flow-name="xsl-region-after">
                <fo:block font-size="8pt">Page <fo:page-number/></fo:block>
              </fo:static-content>
              """
            : "";
        string regions = furniture
            ? """
              <fo:region-body margin-top="12mm" margin-bottom="12mm"/>
              <fo:region-before extent="10mm"/><fo:region-after extent="10mm"/>
              """
            : "<fo:region-body/>";

        string sequences = string.Join("", Enumerable.Range(1, pages).Select(p => $"""
            <fo:page-sequence master-reference="p">
              {header}
              <fo:flow flow-name="xsl-region-body">
                <fo:block font-size="{fontSize}pt" line-height="{leadingPt}pt">
                  Alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima
                  mike november oscar papa quebec romeo sierra tango uniform victor whiskey
                  x-ray yankee zulu, on page {p}.
                </fo:block>
              </fo:flow>
            </fo:page-sequence>
            """));

        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <fo:root xmlns:fo="http://www.w3.org/1999/XSL/Format" font-family="serif">
                  <fo:layout-master-set>
                    <fo:simple-page-master master-name="p" page-width="210mm" page-height="297mm"
                      margin="{marginMm}mm">{regions}</fo:simple-page-master>
                  </fo:layout-master-set>
                  {sequences}
                </fo:root>
                """;
    }

    private string RenderPdf(string fo)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-pdfdump-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, RenderTool.Render(fo, RenderTool.RenderFormat.Pdf));
        _paths.Add(path);
        return path;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = new PdfDumpTool().Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Analyser_RecoversPaperMarginsBodyStyleAndLeading()
    {
        DocumentStyleFacts facts = StyleAnalyser.Analyse(
            PdfExtractor.Load(RenderPdf(Fo(marginMm: 25, fontSize: 10, leadingPt: 12.5))));

        Assert.Equal("A4", facts.PaperName);
        // 25mm is 70.87pt. The measurement comes off the ink, so it lands within a point.
        Assert.InRange(facts.MarginLeft, 69, 72);
        Assert.Equal("Liberation Serif", facts.Body!.Font);
        Assert.Equal(10.0, facts.Body.Size, 1);
        Assert.Equal(12.5, facts.Leading, 1);
        Assert.Equal(1.25, facts.LineHeightRatio, 2);
    }

    /// <summary>
    /// Content that overruns the bottom of a region carries onto the next page — both when
    /// the overflow falls between blocks and when it falls inside one.
    ///
    /// <para>
    /// Pinned because it is easy to conclude otherwise from a fixture that merely happens
    /// to fit: 60 single-line 10pt blocks come to 720pt, and an A4 region body with 20mm
    /// margins is 728.5pt, so a 60-block document lands on one page for entirely correct
    /// reasons. Everything the comparison tools measure per page rests on this working, so
    /// it is worth a test rather than an assumption.
    /// </para>
    /// </summary>
    [Fact]
    public void Renderer_PaginatesContentThatOverrunsTheRegion()
    {
        string Document(string flow) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <fo:root xmlns:fo="http://www.w3.org/1999/XSL/Format" font-family="serif">
              <fo:layout-master-set>
                <fo:simple-page-master master-name="p" page-width="210mm" page-height="297mm"
                  margin="20mm"><fo:region-body/></fo:simple-page-master>
              </fo:layout-master-set>
              <fo:page-sequence master-reference="p"><fo:flow flow-name="xsl-region-body">
                {flow}
              </fo:flow></fo:page-sequence>
            </fo:root>
            """;

        // Between blocks: 60 fill the body, so 62 spill onto a second page.
        string manyBlocks = string.Concat(Enumerable.Range(1, 62).Select(i =>
            $"<fo:block font-size=\"10pt\">Line {i} of a flow that overruns the body.</fo:block>"));
        PdfDocumentModel split = PdfExtractor.Load(RenderPdf(Document(manyBlocks)));

        Assert.Equal(2, split.PageCount);
        Assert.Equal(60, split.Pages[0].Lines.Count);
        Assert.Equal(2, split.Pages[1].Lines.Count);
        Assert.StartsWith("Line 61", split.Pages[1].Lines[0].Text);

        // Inside one block: a single paragraph long enough to run over three pages.
        string words = string.Join(" ", Enumerable.Range(1, 1600).Select(i => $"word{i}"));
        PdfDocumentModel wrapped = PdfExtractor.Load(
            RenderPdf(Document($"<fo:block font-size=\"10pt\">{words}</fo:block>")));

        Assert.True(wrapped.PageCount >= 3, $"expected at least 3 pages, got {wrapped.PageCount}");
        // No word is lost or duplicated at a page boundary.
        Assert.Equal(1600, wrapped.Words.Count(w => w.StartsWith("word", StringComparison.Ordinal)));
    }

    [Fact]
    public void Analyser_TakesTheMarginAsTheClosestApproachOfInkToTheEdge()
    {
        // A full first page and a two-line second one — how every document ends. A median
        // over pages would report that last page's white space as the document's bottom
        // margin, which is why the measurement is a minimum. The overflow is natural: at
        // 10pt on A4 with 20mm margins, 60 single-line blocks fill the region body, so 62
        // of them spill two lines onto a second page.
        string paragraphs = string.Concat(Enumerable.Range(1, 62).Select(i =>
            $"<fo:block font-size=\"10pt\">Line {i} of a flow long enough to overrun the "
            + "region body and carry onto a second page.</fo:block>"));
        string fo = $"""
                     <?xml version="1.0" encoding="UTF-8"?>
                     <fo:root xmlns:fo="http://www.w3.org/1999/XSL/Format" font-family="serif">
                       <fo:layout-master-set>
                         <fo:simple-page-master master-name="p" page-width="210mm" page-height="297mm"
                           margin="20mm"><fo:region-body/></fo:simple-page-master>
                       </fo:layout-master-set>
                       <fo:page-sequence master-reference="p"><fo:flow flow-name="xsl-region-body">
                         {paragraphs}
                       </fo:flow></fo:page-sequence>
                     </fo:root>
                     """;

        PdfDocumentModel doc = PdfExtractor.Load(RenderPdf(fo));
        DocumentStyleFacts facts = StyleAnalyser.Analyse(doc);

        Assert.Equal(2, doc.PageCount);
        double first = facts.Pages[0].MarginBottom;
        double last = facts.Pages[1].MarginBottom;
        Assert.True(last > first + 200, $"fixture should end short: {first:F0}pt then {last:F0}pt");
        // The document's margin is the first page's, not the average of the two.
        Assert.Equal(first, facts.MarginBottom, 1);
    }

    [Fact]
    public void Analyser_FindsRunningHeadersAndMasksTheFolio()
    {
        DocumentStyleFacts facts = StyleAnalyser.Analyse(PdfExtractor.Load(RenderPdf(Fo())));

        Assert.Contains("MAINTENANCE MANUAL", facts.RunningHeader);
        // "Page 1" and "Page 2" are one running footer, not two strings.
        Assert.Contains("Page #", facts.RunningFooter);
    }

    [Fact]
    public void Analyser_MasksDigitRunsAsASingleHash()
    {
        Assert.Equal("Page # of #", StyleAnalyser.MaskDigits("Page 1 of 12"));
        Assert.Equal("DMC-S#KD-A-#", StyleAnalyser.MaskDigits("DMC-S1KD-A-00"));
        Assert.Equal("no digits", StyleAnalyser.MaskDigits("no digits"));
    }

    [Fact]
    public void Analyser_NamesKnownPaperSizesAndFallsBackToDimensions()
    {
        Assert.Equal("A4", StyleAnalyser.PaperName(595.28, 841.89));
        Assert.Equal("A4 landscape", StyleAnalyser.PaperName(841.89, 595.28));
        Assert.Equal("US Letter", StyleAnalyser.PaperName(612, 792));
        Assert.Equal("400.0x400.0pt", StyleAnalyser.PaperName(400, 400));
    }

    [Fact]
    public void Cli_DefaultDump_ListsEveryMarkInPageOrder()
    {
        var (code, output, _) = Run(RenderPdf(Fo()));

        Assert.Equal(0, code);
        Assert.Contains("pages          2", output);
        Assert.Contains("## Page 1", output);
        Assert.Contains("## Page 2", output);
        Assert.Contains("MAINTENANCE MANUAL", output);
        // Positions are stated so a difference can be acted on, not just noticed.
        Assert.Contains("baseline", Run("-h").Out + output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_StyleOnly_OmitsThePerPageMarks()
    {
        var (code, output, _) = Run("-s", RenderPdf(Fo()));

        Assert.Equal(0, code);
        Assert.Contains("body style", output);
        Assert.Contains("leading", output);
        Assert.DoesNotContain("## Page 1", output);
    }

    [Fact]
    public void Cli_TextOnly_EmitsThePlainTextOfEachPage()
    {
        var (code, output, _) = Run("-x", RenderPdf(Fo()));

        Assert.Equal(0, code);
        Assert.Contains("=== page 1 ===", output);
        Assert.Contains("Alpha bravo charlie", output);
        Assert.DoesNotContain("body style", output);
    }

    [Fact]
    public void Cli_PageSpec_SelectsPages()
    {
        string pdf = RenderPdf(Fo());

        var (code, output, _) = Run("-p", "2", pdf);

        Assert.Equal(0, code);
        Assert.Contains("## Page 2", output);
        Assert.DoesNotContain("## Page 1 —", output);
        Assert.Equal(2, Run("-p", "1-9", "--nonsense", pdf).Code);
        Assert.Equal(2, Run("-p", "two", pdf).Code);
    }

    [Fact]
    public void Cli_Json_IsWellFormedAndCarriesTheMeasurements()
    {
        var (code, output, _) = Run("-J", RenderPdf(Fo()));

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("s1kd-pdfdump/1", doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("A4", doc.RootElement.GetProperty("paper").GetString());
        Assert.True(doc.RootElement.GetProperty("marginsPt").GetProperty("left").GetDouble() > 60);
        Assert.NotEmpty(doc.RootElement.GetProperty("outlines").EnumerateArray());
    }

    [Fact]
    public void Cli_BadArguments_ExitTwo()
    {
        Assert.Equal(2, Run().Code);
        Assert.Equal(2, Run("no-such-file.pdf").Code);
        Assert.Equal(0, Run("--help").Code);
    }
}
