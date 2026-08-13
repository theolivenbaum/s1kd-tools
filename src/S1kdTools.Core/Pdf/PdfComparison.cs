namespace S1kdTools.Pdf;

/// <summary>Knobs for a whole comparison.</summary>
public sealed class PdfCompareOptions
{
    public InkDiffOptions Ink { get; init; } = new();

    /// <summary>Displacement below this is not called a move.</summary>
    public double MovementTolerancePt { get; init; } = StructureDiff.DefaultToleranceP;

    /// <summary>
    /// How many divergent pages get a detailed teardown. One by default: differences
    /// cascade, so the second divergent page is nearly always a consequence of the first
    /// and detailing it buries the finding that matters. Metrics always cover every page.
    /// </summary>
    public int DetailPages { get; init; } = 1;

    /// <summary>Most line-level entries to report per detailed page.</summary>
    public int MaxLineEntries { get; init; } = 80;

    /// <summary>Write per-page raster and annotated-diff PNGs here.</summary>
    public string? ImageDirectory { get; init; }
}

/// <summary>The comparison of a single page pair.</summary>
public sealed class PageComparison
{
    public required int Number { get; init; }

    public required bool InActual { get; init; }

    public required bool InReference { get; init; }

    public InkPageDiff? Ink { get; init; }

    public PageStructureDiff? Structure { get; init; }

    public required int ActualWords { get; init; }

    public required int ReferenceWords { get; init; }

    /// <summary>Per-page parity, 0-1, on the same scale as the document score.</summary>
    public required double Score { get; init; }

    public required string Verdict { get; init; }

    public required bool HasDifference { get; init; }

    /// <summary>Set when this page was given a detailed teardown in the report.</summary>
    public bool Detailed { get; set; }
}

/// <summary>Everything the report is written from.</summary>
public sealed class PdfComparison
{
    public required PdfDocumentModel Actual { get; init; }

    public required PdfDocumentModel Reference { get; init; }

    public required DocumentStyleFacts ActualStyle { get; init; }

    public required DocumentStyleFacts ReferenceStyle { get; init; }

    public required IReadOnlyList<PageComparison> Pages { get; init; }

    public required IReadOnlyList<StyleFinding> StyleFindings { get; init; }

    /// <summary>1-based number of the first page that differs; null when nothing does.</summary>
    public required int? FirstDivergentPage { get; init; }

    /// <summary>
    /// Word-sequence agreement across the whole document, ignoring page boundaries. The
    /// first metric to drive to 1.0: until the right words are being emitted at all,
    /// nothing about their placement is worth measuring.
    /// </summary>
    public required double TextAgreement { get; init; }

    /// <summary>Mean per-page word agreement — text agreement that also cares which page.</summary>
    public required double PageTextAgreement { get; init; }

    /// <summary>Mean ink-quantity agreement over pages, 0-1.</summary>
    public required double InkAmountAgreement { get; init; }

    /// <summary>Mean ink-placement agreement (Jaccard of inked cells) over pages, 0-1.</summary>
    public required double InkPlacementAgreement { get; init; }

    public required double PageCountAgreement { get; init; }

    /// <summary>
    /// A single 0-100 figure for tracking progress across stylesheet iterations. It is a
    /// weighted sum of the five agreements above, so it moves for the right reasons and
    /// cannot be improved by making the output worse.
    /// </summary>
    public required double ParityScore { get; init; }

    public bool Identical => FirstDivergentPage is null && StyleFindings.Count == 0;

    /// <summary>
    /// One line, stable in shape, for pasting into a build log or a progress table.
    /// </summary>
    public string ProgressLine =>
        $"parity={ParityScore:F1} pages={Actual.PageCount}/{Reference.PageCount} "
        + $"words={Actual.WordCount}/{Reference.WordCount} text={TextAgreement:F3} "
        + $"pagetext={PageTextAgreement:F3} ink={InkAmountAgreement:F3} place={InkPlacementAgreement:F3} "
        + $"firstdiff={(FirstDivergentPage?.ToString() ?? "none")}";
}

/// <summary>Runs a whole comparison: extract, measure, diff, score.</summary>
public static class PdfComparer
{
    // The weights that make up the parity score. Placement is weighted as heavily as
    // content because a stylesheet is judged on both, and page count is kept low: a
    // pagination difference is loud enough in the report without also dominating the score.
    private const double WeightPages = 20;
    private const double WeightText = 30;
    private const double WeightPageText = 10;
    private const double WeightInkAmount = 10;
    private const double WeightInkPlacement = 30;

    public static PdfComparison Compare(string actualPath, string referencePath, PdfCompareOptions? options = null)
        => Compare(PdfExtractor.Load(actualPath), PdfExtractor.Load(referencePath), options);

    public static PdfComparison Compare(
        PdfDocumentModel actual, PdfDocumentModel reference, PdfCompareOptions? options = null)
    {
        options ??= new PdfCompareOptions();

        int pageCount = Math.Max(actual.PageCount, reference.PageCount);
        var pages = new List<PageComparison>(pageCount);

        for (int i = 0; i < pageCount; i++)
        {
            PdfPageModel? a = i < actual.PageCount ? actual.Pages[i] : null;
            PdfPageModel? r = i < reference.PageCount ? reference.Pages[i] : null;
            pages.Add(ComparePage(i + 1, a, r, options));
        }

        // Detail the first divergent pages only, and do the image writing there too, so a
        // 400-page publication does not spend its time rasterising pages nobody will read.
        var divergent = pages.Where(p => p.HasDifference).ToList();
        int detailCount = options.DetailPages <= 0 ? divergent.Count : Math.Min(options.DetailPages, divergent.Count);
        for (int i = 0; i < detailCount; i++)
        {
            divergent[i].Detailed = true;
        }

        if (options.ImageDirectory is { } dir)
        {
            foreach (PageComparison p in pages.Where(p => p.Detailed))
            {
                PdfPageModel? a = p.Number <= actual.PageCount ? actual.Pages[p.Number - 1] : null;
                PdfPageModel? r = p.Number <= reference.PageCount ? reference.Pages[p.Number - 1] : null;
                if (a is not null && r is not null && p.Ink is not null)
                {
                    InkDiff.WriteImages(dir, p.Number, a, r, p.Ink, options.Ink);
                }
            }
        }

        DocumentStyleFacts actualStyle = StyleAnalyser.Analyse(actual);
        DocumentStyleFacts referenceStyle = StyleAnalyser.Analyse(reference);

        double pageAgreement = pageCount == 0
            ? 1.0
            : (double)Math.Min(actual.PageCount, reference.PageCount) / pageCount;

        // Averaged over max(pages), not over compared pages: a missing page must drag the
        // score down, not vanish from the denominator and leave it flattering.
        double denominator = Math.Max(1, pageCount);
        double pageText = pages.Sum(p => p.Structure?.TextSimilarity ?? 0) / denominator;
        double inkAmount = pages.Sum(p => p.Ink is null ? 0 : AmountAgreement(p.Ink.InkRatio)) / denominator;
        double inkPlacement = pages.Sum(p => p.Ink?.InkIoU ?? 0) / denominator;
        double textAgreement = StructureDiff.SequenceSimilarity(actual.Words, reference.Words);

        double score = (WeightPages * pageAgreement)
                       + (WeightText * textAgreement)
                       + (WeightPageText * pageText)
                       + (WeightInkAmount * inkAmount)
                       + (WeightInkPlacement * inkPlacement);

        return new PdfComparison
        {
            Actual = actual,
            Reference = reference,
            ActualStyle = actualStyle,
            ReferenceStyle = referenceStyle,
            Pages = pages,
            StyleFindings = StyleDelta.Compare(actualStyle, referenceStyle),
            FirstDivergentPage = divergent.Count > 0 ? divergent[0].Number : null,
            TextAgreement = textAgreement,
            PageTextAgreement = pageText,
            InkAmountAgreement = inkAmount,
            InkPlacementAgreement = inkPlacement,
            PageCountAgreement = pageAgreement,
            ParityScore = Math.Round(score, 2),
        };
    }

    /// <summary>Ink-quantity agreement: 1.0 at the same amount, falling to 0 at double or none.</summary>
    private static double AmountAgreement(double ratio)
    {
        if (double.IsInfinity(ratio) || double.IsNaN(ratio))
        {
            return 0;
        }
        return Math.Clamp(1.0 - Math.Abs(ratio - 1.0), 0, 1);
    }

    private static PageComparison ComparePage(
        int number, PdfPageModel? actual, PdfPageModel? reference, PdfCompareOptions options)
    {
        if (actual is null || reference is null)
        {
            return new PageComparison
            {
                Number = number,
                InActual = actual is not null,
                InReference = reference is not null,
                ActualWords = actual?.WordCount ?? 0,
                ReferenceWords = reference?.WordCount ?? 0,
                Score = 0,
                HasDifference = true,
                Verdict = actual is null
                    ? "PAGE MISSING — the reference has this page and this rendering stops earlier"
                    : "PAGE EXTRA — this rendering produces a page the reference does not have",
            };
        }

        InkPageDiff ink = InkDiff.Compare(actual, reference, options.Ink);
        PageStructureDiff structure = StructureDiff.Compare(actual, reference, options.MovementTolerancePt);

        bool differs = ink.HasDifference || structure.HasDifference;
        double score = (0.5 * ink.InkIoU)
                       + (0.3 * structure.TextSimilarity)
                       + (0.2 * AmountAgreement(ink.InkRatio));

        return new PageComparison
        {
            Number = number,
            InActual = true,
            InReference = true,
            Ink = ink,
            Structure = structure,
            ActualWords = actual.WordCount,
            ReferenceWords = reference.WordCount,
            Score = score,
            HasDifference = differs,
            Verdict = Diagnose(ink, structure),
        };
    }

    /// <summary>
    /// Turns the numbers into the one sentence that says which investigation to start.
    /// Order matters: the checks that explain the most are asked first, so a page whose
    /// content is simply absent is not reported as a reflow because the blank rows
    /// happened to align.
    /// </summary>
    private static string Diagnose(InkPageDiff ink, PageStructureDiff structure)
    {
        if (!ink.GeometryMatches)
        {
            return $"PAGE GEOMETRY DIFFERS — {ink.ActualWidth:F0}x{ink.ActualHeight:F0}pt against "
                   + $"{ink.ReferenceWidth:F0}x{ink.ReferenceHeight:F0}pt. Fix the page master first; "
                   + "every other measurement on this page is downstream of it.";
        }
        if (!ink.HasDifference && !structure.HasDifference)
        {
            return "MATCH — the same ink in the same places, set the same way.";
        }
        if (structure.TextSimilarity < 0.5)
        {
            return $"DIFFERENT CONTENT — only {structure.TextSimilarity:P0} of the words agree. This is "
                   + "probably not the same page: check pagination before reading anything below.";
        }
        if (structure.Rewrapped)
        {
            return "REWRAPPED — the same words, broken into different lines. Suspect the measure "
                   + "(region width, indents) or the font metrics, not the content.";
        }
        if (ink.InkRatio < 0.92)
        {
            return $"CONTENT MISSING — {(1 - ink.InkRatio) * 100:F0}% less ink than the reference. "
                   + "Look for something the stylesheet never emits.";
        }
        if (ink.InkRatio > 1.08)
        {
            return $"EXTRA CONTENT — {(ink.InkRatio - 1) * 100:F0}% more ink than the reference. "
                   + "Something is visible that should not be.";
        }
        if (Math.Abs(structure.PageShiftPt) > 1.0)
        {
            return $"REFLOW CASCADE — the content is present but the whole page sits "
                   + $"{Math.Abs(structure.PageShiftPt):F1}pt {(structure.PageShiftPt > 0 ? "lower" : "higher")}. "
                   + "One upstream measurement (a margin, a leading, a space-before) moved everything.";
        }
        if (structure.Restyled > 0 && structure.Moved == 0 && structure.Missing == 0)
        {
            return $"RESTYLED — {structure.Restyled} line(s) in the right place, set differently.";
        }
        if (structure.Missing > 0 || structure.Extra > 0)
        {
            return $"CONTENT DIFFERS — {structure.Missing} line(s) missing, {structure.Extra} extra.";
        }
        return "DIFFERS — no single signature dominates; read the regions and the line table below.";
    }
}
