namespace S1kdTools.Pdf;

/// <summary>One (font, size, weight, colour) combination and how much of the document uses it.</summary>
public sealed class FontUsage
{
    public required string Font { get; init; }

    public required double Size { get; init; }

    public required bool Bold { get; init; }

    public required bool Italic { get; init; }

    public required string Color { get; init; }

    /// <summary>Glyphs set in this style — the measure of how central it is to the document.</summary>
    public required int Glyphs { get; init; }

    public required int Lines { get; init; }

    /// <summary>A line set in this style, so the role (heading, body, caption) is recognisable.</summary>
    public required string Sample { get; init; }

    public string Key => $"{Font} {Size:F1}pt{(Bold ? " bold" : "")}{(Italic ? " italic" : "")}"
                         + (Color == "#000000" ? "" : $" {Color}");

    public override string ToString() => Key;
}

/// <summary>An x position lines start at, and how many start there. The document's indent stops.</summary>
public readonly record struct IndentStop(double X, int Lines);

/// <summary>
/// What a stylesheet would have had to say to produce this page.
///
/// <para>
/// These are measurements, not guesses: margins are read off the ink, leading off
/// consecutive baselines, indents off where lines begin. They exist so the report can
/// say "the reference sets a 28.3pt (10mm) left margin and you set 56.7pt (20mm)"
/// instead of "the text is in the wrong place" — one of those is a stylesheet edit and
/// the other is a puzzle.
/// </para>
/// </summary>
public sealed class PageStyleFacts
{
    public required int PageNumber { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    /// <summary>"A4", "US Letter", or the dimensions when it is neither.</summary>
    public required string PaperName { get; init; }

    public required Rect? ContentBounds { get; init; }

    public double MarginLeft => ContentBounds?.Left ?? 0;

    public double MarginTop => ContentBounds?.Top ?? 0;

    public double MarginRight => ContentBounds is { } c ? Width - c.Right : 0;

    public double MarginBottom => ContentBounds is { } c ? Height - c.Bottom : 0;

    /// <summary>The style most glyphs on the page are set in: the body text.</summary>
    public required FontUsage? Body { get; init; }

    /// <summary>Every style used on the page, most-used first.</summary>
    public required IReadOnlyList<FontUsage> Fonts { get; init; }

    /// <summary>Median baseline-to-baseline distance within the body text, in points.</summary>
    public required double Leading { get; init; }

    /// <summary>Leading as a multiple of the body size — what <c>line-height</c> would be set to.</summary>
    public double LineHeightRatio => Body is { Size: > 0 } b ? Leading / b.Size : 0;

    public required IReadOnlyList<IndentStop> IndentStops { get; init; }

    /// <summary>Lines in the top margin band: running heads, chapter titles, folios.</summary>
    public required IReadOnlyList<TextLine> HeaderLines { get; init; }

    /// <summary>Lines in the bottom margin band: page numbers, data module codes, issue dates.</summary>
    public required IReadOnlyList<TextLine> FooterLines { get; init; }

    public required IReadOnlyList<GraphicMark> Rules { get; init; }

    public required IReadOnlyList<GraphicMark> Fills { get; init; }

    public required IReadOnlyList<GraphicMark> Images { get; init; }

    public required int LineCount { get; init; }

    public required int WordCount { get; init; }
}

/// <summary>The same measurements taken over a whole document.</summary>
public sealed class DocumentStyleFacts
{
    public required IReadOnlyList<PageStyleFacts> Pages { get; init; }

    public required string PaperName { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    /// <summary>Median of the per-page margins; robust against one unusually full page.</summary>
    public required double MarginLeft { get; init; }

    public required double MarginRight { get; init; }

    public required double MarginTop { get; init; }

    public required double MarginBottom { get; init; }

    public required FontUsage? Body { get; init; }

    public required IReadOnlyList<FontUsage> Fonts { get; init; }

    public required double Leading { get; init; }

    public double LineHeightRatio => Body is { Size: > 0 } b ? Leading / b.Size : 0;

    public required IReadOnlyList<IndentStop> IndentStops { get; init; }

    /// <summary>Header text that repeats across pages, with page numbers masked as <c>#</c>.</summary>
    public required IReadOnlyList<string> RunningHeader { get; init; }

    public required IReadOnlyList<string> RunningFooter { get; init; }

    public required int RuleCount { get; init; }

    public required int FillCount { get; init; }

    public required int ImageCount { get; init; }
}

/// <summary>Derives <see cref="PageStyleFacts"/> from an extracted page.</summary>
public static class StyleAnalyser
{
    /// <summary>
    /// Fraction of the page height at each end treated as the header/footer band. Wide
    /// enough to reach page furniture that sits inside a generous paper margin, which a
    /// tighter band misses entirely; what keeps it from swallowing body text is that
    /// running content also has to <i>repeat</i> across pages.
    /// </summary>
    private const double BandFraction = 0.15;

    /// <summary>
    /// The tighter band used on a single-page document, where repetition cannot be
    /// checked and position is the only evidence there is.
    /// </summary>
    private const double SinglePageBandFraction = 0.08;

    private static readonly (string Name, double W, double H)[] KnownPapers =
    {
        ("A3", 841.89, 1190.55),
        ("A4", 595.28, 841.89),
        ("A5", 419.53, 595.28),
        ("US Letter", 612.0, 792.0),
        ("US Legal", 612.0, 1008.0),
        ("US Tabloid", 792.0, 1224.0),
    };

    public static PageStyleFacts Analyse(PdfPageModel page)
    {
        var fonts = page.Lines
            .GroupBy(l => (l.FontName, l.FontSize, l.Bold, l.Italic, l.Color))
            .Select(g => new FontUsage
            {
                Font = g.Key.FontName,
                Size = g.Key.FontSize,
                Bold = g.Key.Bold,
                Italic = g.Key.Italic,
                Color = g.Key.Color,
                Glyphs = g.Sum(l => l.GlyphCount),
                Lines = g.Count(),
                Sample = Truncate(g.OrderByDescending(l => l.Text.Length).First().Text, 60),
            })
            .OrderByDescending(f => f.Glyphs)
            .ToArray();

        FontUsage? body = fonts.FirstOrDefault();

        double headerLimit = page.Height * BandFraction;
        double footerLimit = page.Height * (1 - BandFraction);

        return new PageStyleFacts
        {
            PageNumber = page.Number,
            Width = page.Width,
            Height = page.Height,
            PaperName = PaperName(page.Width, page.Height),
            ContentBounds = page.ContentBounds,
            Body = body,
            Fonts = fonts,
            Leading = Leading(page, body),
            IndentStops = IndentStops(page),
            HeaderLines = page.Lines.Where(l => l.Baseline <= headerLimit).ToArray(),
            FooterLines = page.Lines.Where(l => l.Baseline >= footerLimit).ToArray(),
            Rules = page.Graphics.Where(g => g.Kind == MarkKind.Rule).ToArray(),
            Fills = page.Graphics.Where(g => g.Kind is MarkKind.Fill or MarkKind.Stroke).ToArray(),
            Images = page.Graphics.Where(g => g.Kind == MarkKind.Image).ToArray(),
            LineCount = page.Lines.Count,
            WordCount = page.WordCount,
        };
    }

    public static DocumentStyleFacts Analyse(PdfDocumentModel document)
    {
        var pages = document.Pages.Select(Analyse).ToArray();

        var fonts = document.Pages
            .SelectMany(p => p.Lines)
            .GroupBy(l => (l.FontName, l.FontSize, l.Bold, l.Italic, l.Color))
            .Select(g => new FontUsage
            {
                Font = g.Key.FontName,
                Size = g.Key.FontSize,
                Bold = g.Key.Bold,
                Italic = g.Key.Italic,
                Color = g.Key.Color,
                Glyphs = g.Sum(l => l.GlyphCount),
                Lines = g.Count(),
                Sample = Truncate(g.OrderByDescending(l => l.Text.Length).First().Text, 60),
            })
            .OrderByDescending(f => f.Glyphs)
            .ToArray();

        var indents = document.Pages
            .SelectMany(p => p.Lines)
            .GroupBy(l => Math.Round(l.Bounds.Left, 0))
            .Select(g => new IndentStop(g.Key, g.Count()))
            .Where(s => s.Lines >= 2)
            .OrderByDescending(s => s.Lines)
            .Take(8)
            .OrderBy(s => s.X)
            .ToArray();

        // A leading measured per page then medianned is steadier than one measured over
        // the whole document, where a page break would contribute a nonsense gap.
        double leading = Median(pages.Where(p => p.Leading > 0).Select(p => p.Leading));

        var firstPage = pages.FirstOrDefault();
        return new DocumentStyleFacts
        {
            Pages = pages,
            PaperName = firstPage?.PaperName ?? "(none)",
            Width = firstPage?.Width ?? 0,
            Height = firstPage?.Height ?? 0,
            // A margin is how close ink ever gets to that edge, so it is the minimum over
            // pages and not the median. The last page of any document stops part-way down,
            // and a median would report that half-empty page's white space as the document's
            // bottom margin.
            MarginLeft = Closest(pages, p => p.MarginLeft),
            MarginRight = Closest(pages, p => p.MarginRight),
            MarginTop = Closest(pages, p => p.MarginTop),
            MarginBottom = Closest(pages, p => p.MarginBottom),
            Body = fonts.FirstOrDefault(),
            Fonts = fonts,
            Leading = leading,
            IndentStops = indents,
            RunningHeader = Repeated(pages, atTop: true),
            RunningFooter = Repeated(pages, atTop: false),
            RuleCount = pages.Sum(p => p.Rules.Count),
            FillCount = pages.Sum(p => p.Fills.Count),
            ImageCount = pages.Sum(p => p.Images.Count),
        };
    }

    /// <summary>The smallest margin any page with content has at this edge.</summary>
    private static double Closest(IReadOnlyList<PageStyleFacts> pages, Func<PageStyleFacts, double> edge)
    {
        var values = pages.Where(p => p.ContentBounds is not null).Select(edge).ToArray();
        return values.Length == 0 ? 0 : values.Min();
    }

    /// <summary>
    /// Text that appears in the same band on more than one page, with digit runs masked.
    /// Masking is what turns "Page 1 of 3" and "Page 2 of 3" into one running footer
    /// rather than three unrelated strings.
    /// </summary>
    private static IReadOnlyList<string> Repeated(IReadOnlyList<PageStyleFacts> pages, bool atTop)
    {
        if (pages.Count == 0)
        {
            return Array.Empty<string>();
        }

        // With one page there is nothing to repeat against, so only the tight band counts;
        // with several, the wide band is safe because a line still has to appear on two of
        // them before it is called running content.
        bool single = pages.Count == 1;
        int quorum = single ? 1 : 2;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PageStyleFacts page in pages)
        {
            IEnumerable<TextLine> band = atTop ? page.HeaderLines : page.FooterLines;
            if (single)
            {
                double limit = page.Height * SinglePageBandFraction;
                band = atTop
                    ? band.Where(l => l.Baseline <= limit)
                    : band.Where(l => l.Baseline >= page.Height - limit);
            }
            foreach (string text in band.Select(l => MaskDigits(l.Text)).Distinct(StringComparer.Ordinal))
            {
                counts[text] = counts.GetValueOrDefault(text) + 1;
            }
        }

        return counts.Where(kv => kv.Value >= quorum)
                     .OrderByDescending(kv => kv.Value)
                     .Select(kv => kv.Key)
                     .Take(6)
                     .ToArray();
    }

    public static string MaskDigits(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool inDigits = false;
        foreach (char c in text)
        {
            if (char.IsDigit(c))
            {
                if (!inDigits)
                {
                    sb.Append('#');
                    inDigits = true;
                }
            }
            else
            {
                sb.Append(c);
                inDigits = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Median distance between the baselines of consecutive body-text lines. Only lines
    /// in the body style count, and only gaps small enough to be leading rather than a
    /// paragraph space or a column break.
    /// </summary>
    private static double Leading(PdfPageModel page, FontUsage? body)
    {
        if (body is null || body.Size <= 0)
        {
            return 0;
        }
        var baselines = page.Lines
            .Where(l => Math.Abs(l.FontSize - body.Size) < 0.05 && l.FontName == body.Font)
            .Select(l => l.Baseline)
            .OrderBy(v => v)
            .ToArray();

        var gaps = new List<double>();
        for (int i = 1; i < baselines.Length; i++)
        {
            double gap = baselines[i] - baselines[i - 1];
            if (gap > 0.1 && gap < body.Size * 3)
            {
                gaps.Add(gap);
            }
        }
        return Median(gaps);
    }

    private static IReadOnlyList<IndentStop> IndentStops(PdfPageModel page) =>
        page.Lines
            .GroupBy(l => Math.Round(l.Bounds.Left, 0))
            .Select(g => new IndentStop(g.Key, g.Count()))
            .OrderByDescending(s => s.Lines)
            .Take(6)
            .OrderBy(s => s.X)
            .ToArray();

    public static string PaperName(double width, double height)
    {
        foreach ((string name, double w, double h) in KnownPapers)
        {
            if (Close(width, w) && Close(height, h))
            {
                return name;
            }
            if (Close(width, h) && Close(height, w))
            {
                return $"{name} landscape";
            }
        }
        return $"{width:F1}x{height:F1}pt";

        static bool Close(double a, double b) => Math.Abs(a - b) < 2.0;
    }

    /// <summary>Points as millimetres, for margins and sizes a stylesheet author states in mm.</summary>
    public static double ToMillimetres(double points) => points * 25.4 / 72.0;

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
