using S1kdTools.Pdf;
using S1kdTools.Tools;

namespace S1kdTools.Tests;

/// <summary>
/// Tests for the PDF comparison stack. Every fixture is rendered here from XSL-FO by
/// <see cref="RenderTool"/>, so the inputs are real PDFs produced by the same renderer the
/// tools are meant to be pointed at, and each test states the one layout decision it
/// changes between the two sides.
/// </summary>
public class PdfDiffToolTests : IDisposable
{
    private readonly List<string> _paths = new();

    public void Dispose()
    {
        foreach (string path in _paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------------- fixtures

    /// <summary>
    /// A one-page FO document with knobs for everything the comparison is supposed to
    /// notice, so a test can change exactly one of them.
    /// </summary>
    private static string Fo(
        // Long enough to wrap over several lines, so a displacement has more than one
        // matched line to be a median of.
        string body = "Alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo "
                      + "lima mike november oscar papa quebec romeo sierra tango uniform victor "
                      + "whiskey x-ray yankee zulu, and again alpha bravo charlie delta echo "
                      + "foxtrot golf hotel india juliet kilo lima mike november oscar papa.",
        double fontSize = 10,
        double marginMm = 20,
        double padBeforePt = 0,
        bool rule = false,
        bool shading = false,
        string? extraBlock = null,
        int pages = 1)
    {
        // A heading block precedes the body so that `padBeforePt` has something to push
        // against: XSL-FO discards space at the very start of a flow, so putting it on the
        // first block would move nothing and quietly turn a displacement test into a
        // comparison of two identical files.
        string sequences = string.Join("", Enumerable.Range(1, pages).Select(p =>
            $"""
             <fo:page-sequence master-reference="p"><fo:flow flow-name="xsl-region-body">
               <fo:block font-size="{fontSize}pt">Heading (page {p})</fo:block>
               <fo:block font-size="{fontSize}pt" space-before="{padBeforePt}pt"
                 {(rule ? "border-after-style=\"solid\" border-after-width=\"1pt\"" : "")}
                 {(shading ? "background-color=\"#dddddd\"" : "")}>{body}</fo:block>
               {extraBlock ?? ""}
             </fo:flow></fo:page-sequence>
             """));

        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <fo:root xmlns:fo="http://www.w3.org/1999/XSL/Format" font-family="serif">
                  <fo:layout-master-set>
                    <fo:simple-page-master master-name="p" page-width="210mm" page-height="297mm"
                      margin="{marginMm}mm"><fo:region-body/></fo:simple-page-master>
                  </fo:layout-master-set>
                  {sequences}
                </fo:root>
                """;
    }

    private string RenderPdf(string fo)
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-pdfdiff-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, RenderTool.Render(fo, RenderTool.RenderFormat.Pdf));
        _paths.Add(path);
        return path;
    }

    private string TempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s1kd-pdfdiff-{Guid.NewGuid():N}");
        _paths.Add(path);
        return path;
    }

    private static (int Code, string Out, string Err) Run(ITool tool, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = tool.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // ------------------------------------------------------------------------ extraction

    [Fact]
    public void Extractor_ReadsLinesWithPositionsAndStyle()
    {
        PdfDocumentModel doc = PdfExtractor.Load(RenderPdf(Fo(fontSize: 12, marginMm: 20)));

        PdfPageModel page = Assert.Single(doc.Pages);
        Assert.Equal(1, page.Number);
        Assert.InRange(page.Width, 594, 596);          // A4
        TextLine line = Assert.Single(page.Lines, l => l.Text.Contains("Alpha"));
        Assert.Equal(12.0, line.FontSize, 1);
        Assert.Equal("#000000", line.Color);
        // 20mm is 56.7pt, and coordinates are measured from the top-left, not PDF's
        // bottom-left origin.
        Assert.InRange(line.Bounds.Left, 56, 58);
        // Near the top of the page: the heading block above it, and nothing else.
        Assert.InRange(line.Baseline, 56, 110);
    }

    [Fact]
    public void Extractor_StripsSubsetTagFromFontName()
    {
        // Producers prefix embedded subsets with a random six-letter tag; leaving it in
        // would make the same font compare unequal between two renderings.
        Assert.Equal("Liberation Serif", PdfExtractor.NormaliseFontName("VDMHZV+Liberation Serif"));
        Assert.Equal("Times", PdfExtractor.NormaliseFontName("Times"));
        Assert.Equal("(unknown)", PdfExtractor.NormaliseFontName(null));
    }

    [Fact]
    public void Extractor_SplitsColumnsOnTheSameBaseline()
    {
        // Two blocks side by side on one line must not come back as one run of text: a
        // table row's cells are separately positioned and separately styled.
        string fo = """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <fo:root xmlns:fo="http://www.w3.org/1999/XSL/Format" font-family="serif">
                      <fo:layout-master-set>
                        <fo:simple-page-master master-name="p" page-width="210mm" page-height="297mm"
                          margin="20mm"><fo:region-body/></fo:simple-page-master>
                      </fo:layout-master-set>
                      <fo:page-sequence master-reference="p"><fo:flow flow-name="xsl-region-body">
                        <fo:table table-layout="fixed" width="100%">
                          <fo:table-column column-width="70mm"/><fo:table-column column-width="70mm"/>
                          <fo:table-body><fo:table-row>
                            <fo:table-cell><fo:block font-size="10pt">LeftCell</fo:block></fo:table-cell>
                            <fo:table-cell><fo:block font-size="10pt">RightCell</fo:block></fo:table-cell>
                          </fo:table-row></fo:table-body>
                        </fo:table>
                      </fo:flow></fo:page-sequence>
                    </fo:root>
                    """;
        PdfDocumentModel doc = PdfExtractor.Load(RenderPdf(fo));

        var lines = doc.Pages[0].Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal("LeftCell", lines[0].Text);
        Assert.Equal("RightCell", lines[1].Text);
        Assert.True(lines[1].Bounds.Left > lines[0].Bounds.Left + 100,
            "the second cell should be measurably further across the page");
    }

    // -------------------------------------------------------------------------- identity

    [Fact]
    public void Compare_IdenticalDocuments_ScoresOneHundred()
    {
        string a = RenderPdf(Fo());
        string b = RenderPdf(Fo());

        PdfComparison c = PdfComparer.Compare(a, b);

        Assert.True(c.Identical);
        Assert.Null(c.FirstDivergentPage);
        Assert.Equal(100.0, c.ParityScore, 1);
        Assert.Equal(1.0, c.InkPlacementAgreement, 3);
        Assert.Contains("MATCH", c.Pages[0].Verdict);
    }

    [Fact]
    public void Cli_IdenticalDocuments_ExitZero()
    {
        string a = RenderPdf(Fo());
        string b = RenderPdf(Fo());

        var (code, output, _) = Run(new PdfDiffTool(), a, b);

        Assert.Equal(0, code);
        Assert.Contains("agree on every page", output);
    }

    // -------------------------------------------------------------------- what it detects

    [Fact]
    public void Compare_DifferentBodySize_IsReportedAsAFontSizeFinding()
    {
        string a = RenderPdf(Fo(fontSize: 12));
        string b = RenderPdf(Fo(fontSize: 10));

        PdfComparison c = PdfComparer.Compare(a, b);

        StyleFinding finding = Assert.Single(c.StyleFindings, f => f.Property == "body.font-size");
        Assert.Equal(StyleSeverity.Structural, finding.Severity);
        Assert.Contains("12.0pt", finding.Actual);
        Assert.Contains("10.0pt", finding.Reference);
        Assert.Contains("font-size", finding.FoHint);

        // The words are all still there, so text agreement stays high while placement drops.
        Assert.Equal(1.0, c.TextAgreement, 2);
        Assert.True(c.InkPlacementAgreement < 0.95);
    }

    [Fact]
    public void Compare_DifferentMargins_IsReportedAsAPageMasterFinding()
    {
        string a = RenderPdf(Fo(marginMm: 30));
        string b = RenderPdf(Fo(marginMm: 20));

        PdfComparison c = PdfComparer.Compare(a, b);

        StyleFinding left = Assert.Single(c.StyleFindings, f => f.Property == "page.margin-left");
        Assert.Contains("mm", left.Actual);
        Assert.Contains("simple-page-master", left.FoHint);
        // 10mm is 28.3pt, and the sign says which way to move it.
        Assert.Contains("+28", left.Delta);
    }

    [Fact]
    public void Compare_ContentPushedDownThePage_IsDiagnosedAsAReflowCascade()
    {
        // Padding rather than space-before: FO discards space at the start of a flow, so a
        // space-before fixture would produce two identical PDFs and prove nothing.
        string a = RenderPdf(Fo(padBeforePt: 40));
        string b = RenderPdf(Fo(padBeforePt: 0));

        PdfComparison c = PdfComparer.Compare(a, b);
        PageComparison page = c.Pages[0];

        Assert.Contains("REFLOW CASCADE", page.Verdict);
        Assert.NotNull(page.Structure);
        Assert.InRange(page.Structure!.PageShiftPt, 38, 42);
        // Every line is present and unedited; it is only in the wrong place.
        Assert.Equal(0, page.Structure.Missing);
        Assert.Equal(0, page.Structure.Extra);
        Assert.True(page.Structure.Moved > 0);
    }

    [Fact]
    public void Compare_MissingLine_IsListedWithItsStyleAndPosition()
    {
        string extra = "<fo:block font-size=\"14pt\" font-weight=\"bold\">Kilo Lima Mike</fo:block>";
        string a = RenderPdf(Fo());
        string b = RenderPdf(Fo(extraBlock: extra));

        PdfComparison c = PdfComparer.Compare(a, b);
        PageStructureDiff structure = c.Pages[0].Structure!;

        LineDiffEntry missing = Assert.Single(
            structure.Entries, e => e.Change == LineChange.Missing);
        Assert.Equal("Kilo Lima Mike", missing.Reference!.Text);
        Assert.Equal(14.0, missing.Reference.FontSize, 1);
        Assert.True(missing.Reference.Bold);
    }

    [Fact]
    public void Compare_MissingRule_IsClusteredAsARuleRegion()
    {
        string a = RenderPdf(Fo(rule: false));
        string b = RenderPdf(Fo(rule: true));

        PdfComparison c = PdfComparer.Compare(a, b);
        InkPageDiff ink = c.Pages[0].Ink!;

        InkRegion region = Assert.Single(ink.Regions, r => r.Kind == InkRegionKind.MissingRule);
        Assert.Contains("rule or border", region.Summary);
        // A rule under a block near the top of a page is wide and shallow, and the box is
        // reported in points so it can be found on the page.
        Assert.True(region.Bounds.Width > region.Bounds.Height * 10);
    }

    [Fact]
    public void Compare_MissingShading_IsClusteredAsAFillRegion()
    {
        string a = RenderPdf(Fo(shading: false));
        string b = RenderPdf(Fo(shading: true));

        PdfComparison c = PdfComparer.Compare(a, b);
        InkPageDiff ink = c.Pages[0].Ink!;

        Assert.Contains(ink.Regions, r => r.Kind == InkRegionKind.MissingFill);
        // The reference lays down more ink than we do, because the panel is not painted.
        Assert.True(ink.InkRatio < 1.0);
    }

    [Fact]
    public void Compare_RegionsCarryTheTextTheyCover()
    {
        string extra = "<fo:block font-size=\"10pt\">November Oscar Papa Quebec</fo:block>";
        string a = RenderPdf(Fo());
        string b = RenderPdf(Fo(extraBlock: extra));

        PdfComparison c = PdfComparer.Compare(a, b);

        // A box on the page is only actionable if you can tell what is in it.
        Assert.Contains(c.Pages[0].Ink!.Regions,
            r => r.ReferenceText.Any(t => t.Contains("November Oscar")));
    }

    [Fact]
    public void Compare_FewerPages_PenalisesThePageCountComponentAndMarksThePageMissing()
    {
        string a = RenderPdf(Fo(pages: 2));
        string b = RenderPdf(Fo(pages: 3));

        PdfComparison c = PdfComparer.Compare(a, b);

        Assert.Equal(3, c.Pages.Count);
        Assert.Equal(2.0 / 3.0, c.PageCountAgreement, 3);
        PageComparison last = c.Pages[2];
        Assert.False(last.InActual);
        Assert.Contains("PAGE MISSING", last.Verdict);
        Assert.Equal(0, last.Score);
    }

    // -------------------------------------------------------------------------- reporting

    [Fact]
    public void Report_DetailsOnlyTheFirstDivergentPageByDefault()
    {
        // Both pages differ; only the first should be taken apart.
        string a = RenderPdf(Fo(fontSize: 12, pages: 2));
        string b = RenderPdf(Fo(fontSize: 10, pages: 2));

        var options = new PdfCompareOptions();
        PdfComparison c = PdfComparer.Compare(a, b, options);

        Assert.Equal(2, c.Pages.Count(p => p.HasDifference));
        Assert.Single(c.Pages, p => p.Detailed);
        Assert.Equal(1, c.FirstDivergentPage);

        string report = MarkdownReport.Write(c, options);
        Assert.Contains("## Page 1 — first divergence, in detail", report);
        Assert.DoesNotContain("## Page 2 — first divergence", report);
        Assert.Contains("further page(s) differ", report);
        // Metrics still cover the whole document, which is the point of stopping at one.
        Assert.Contains("| 2 |", report);
    }

    [Fact]
    public void Report_AllPagesOptionDetailsEveryDivergentPage()
    {
        string a = RenderPdf(Fo(fontSize: 12, pages: 2));
        string b = RenderPdf(Fo(fontSize: 10, pages: 2));

        PdfComparison c = PdfComparer.Compare(a, b, new PdfCompareOptions { DetailPages = 0 });

        Assert.Equal(2, c.Pages.Count(p => p.Detailed));
    }

    [Fact]
    public void Report_DumpsTheStructureOfTheDivergentPage()
    {
        string a = RenderPdf(Fo());
        string b = RenderPdf(Fo(extraBlock: "<fo:block font-size=\"9pt\">Romeo Sierra</fo:block>"));

        var options = new PdfCompareOptions();
        string report = MarkdownReport.Write(PdfComparer.Compare(a, b, options), options);

        Assert.Contains("### Structure of the reference", report);
        Assert.Contains("### Structure of this rendering", report);
        Assert.Contains("Romeo Sierra", report);
        Assert.Contains("## What to change next", report);
    }

    [Fact]
    public void Report_NumericDeltasCarryASign()
    {
        string a = RenderPdf(Fo(pages: 2));
        string b = RenderPdf(Fo(pages: 3));

        var options = new PdfCompareOptions();
        string report = MarkdownReport.Write(PdfComparer.Compare(a, b, options), options);

        // A sectioned custom format would render this as "-F1"; the sign has to be real.
        Assert.Contains("| pages | 2 | 3 | -1 |", report);
    }

    [Fact]
    public void Report_JsonCarriesTheScoreAndTheFindings()
    {
        string a = RenderPdf(Fo(fontSize: 12));
        string b = RenderPdf(Fo(fontSize: 10));

        string json = JsonReport.Write(PdfComparer.Compare(a, b));

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("s1kd-pdfdiff/1", doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.GetProperty("score").GetProperty("parity").GetDouble() < 100);
        Assert.Contains(doc.RootElement.GetProperty("styleFindings").EnumerateArray(),
            f => f.GetProperty("property").GetString() == "body.font-size");
        Assert.Equal(1, doc.RootElement.GetProperty("firstDivergentPage").GetInt32());
    }

    // -------------------------------------------------------------------------- the score

    [Fact]
    public void Score_RisesAsTheRenderingGetsCloser()
    {
        // The whole justification for a single number: it has to move the right way.
        string reference = RenderPdf(Fo(fontSize: 10, marginMm: 25));
        string far = RenderPdf(Fo(fontSize: 14, marginMm: 15));
        string near = RenderPdf(Fo(fontSize: 10, marginMm: 22));
        string exact = RenderPdf(Fo(fontSize: 10, marginMm: 25));

        double farScore = PdfComparer.Compare(far, reference).ParityScore;
        double nearScore = PdfComparer.Compare(near, reference).ParityScore;
        double exactScore = PdfComparer.Compare(exact, reference).ParityScore;

        Assert.True(farScore < nearScore, $"expected {farScore:F1} < {nearScore:F1}");
        Assert.True(nearScore < exactScore, $"expected {nearScore:F1} < {exactScore:F1}");
        Assert.Equal(100.0, exactScore, 1);
    }

    [Fact]
    public void Score_ProgressLineIsStableAndParsable()
    {
        string a = RenderPdf(Fo(fontSize: 12));
        string b = RenderPdf(Fo(fontSize: 10));

        string line = PdfComparer.Compare(a, b).ProgressLine;

        Assert.StartsWith("parity=", line);
        foreach (string key in new[] { "pages=", "words=", "text=", "pagetext=", "ink=", "place=", "firstdiff=" })
        {
            Assert.Contains(key, line);
        }
    }

    [Fact]
    public void SequenceSimilarity_IsOrderSensitiveAndBounded()
    {
        string[] words = { "alpha", "bravo", "charlie", "delta" };

        Assert.Equal(1.0, StructureDiff.SequenceSimilarity(words, words), 3);
        Assert.Equal(0.0, StructureDiff.SequenceSimilarity(Array.Empty<string>(), words), 3);
        Assert.Equal(0.5, StructureDiff.SequenceSimilarity(new[] { "alpha", "charlie" }, words), 3);
        // Reversing keeps every word but destroys the order, so it must not score 1.
        Assert.True(StructureDiff.SequenceSimilarity(words.Reverse().ToArray(), words) < 1.0);
    }

    // ---------------------------------------------------------------------------- the CLI

    [Fact]
    public void Cli_DifferingDocuments_ExitOneAndWriteTheReport()
    {
        string a = RenderPdf(Fo(fontSize: 12));
        string b = RenderPdf(Fo(fontSize: 10));
        string dir = TempDir();
        string report = Path.Combine(dir, "report.md");
        string json = Path.Combine(dir, "report.json");

        var (code, _, _) = Run(new PdfDiffTool(), "-o", report, "-j", json, a, b);

        Assert.Equal(1, code);
        Assert.Contains("# PDF comparison report", File.ReadAllText(report));
        Assert.Contains("s1kd-pdfdiff/1", File.ReadAllText(json));
    }

    [Fact]
    public void Cli_SummaryFormat_PrintsOnlyTheProgressLine()
    {
        string a = RenderPdf(Fo(fontSize: 12));
        string b = RenderPdf(Fo(fontSize: 10));

        var (code, output, _) = Run(new PdfDiffTool(), "-f", "summary", a, b);

        Assert.Equal(1, code);
        Assert.Single(output.Trim().Split('\n'));
        Assert.StartsWith("parity=", output.Trim());
    }

    [Fact]
    public void Cli_FailUnder_GatesOnTheScoreRatherThanOnAnyDifference()
    {
        string a = RenderPdf(Fo(fontSize: 10, marginMm: 21));
        string b = RenderPdf(Fo(fontSize: 10, marginMm: 20));

        // The two differ, so the default gate fails...
        Assert.Equal(1, Run(new PdfDiffTool(), "-q", a, b).Code);
        // ...but a build that only cares about staying close should pass.
        Assert.Equal(0, Run(new PdfDiffTool(), "-q", "-F", "50", a, b).Code);
        Assert.Equal(1, Run(new PdfDiffTool(), "-q", "-F", "99.9", a, b).Code);
    }

    [Fact]
    public void Cli_Images_WritesTheThreePngsForTheDetailedPage()
    {
        string a = RenderPdf(Fo(fontSize: 12));
        string b = RenderPdf(Fo(fontSize: 10));
        string dir = TempDir();

        Run(new PdfDiffTool(), "-q", "-I", dir, a, b);

        foreach (string suffix in new[] { "actual", "reference", "diff" })
        {
            string png = Path.Combine(dir, $"page-001-{suffix}.png");
            Assert.True(File.Exists(png), $"expected {png}");
            // The PNG magic, so a truncated or mislabelled file fails here rather than in
            // whatever eventually tries to display it.
            byte[] head = File.ReadAllBytes(png)[..8];
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, head);
        }
    }

    [Fact]
    public void Cli_BadArguments_ExitTwo()
    {
        string a = RenderPdf(Fo());

        Assert.Equal(2, Run(new PdfDiffTool(), a).Code);                        // one input
        Assert.Equal(2, Run(new PdfDiffTool(), a, a, a).Code);                  // three
        Assert.Equal(2, Run(new PdfDiffTool(), a, "no-such-file.pdf").Code);
        Assert.Equal(2, Run(new PdfDiffTool(), "--nonsense", a, a).Code);
        Assert.Equal(2, Run(new PdfDiffTool(), "-d", "twelve", a, a).Code);
        Assert.Equal(0, Run(new PdfDiffTool(), "--help").Code);
    }
}
