namespace S1kdTools.Pdf;

/// <summary>What a cluster of differing ink most likely is.</summary>
public enum InkRegionKind
{
    /// <summary>The reference draws ink here and we draw little or none.</summary>
    MissingInk,

    /// <summary>We draw ink here and the reference draws little or none.</summary>
    ExtraInk,

    /// <summary>A rule, border or underline the reference has and we do not.</summary>
    MissingRule,

    /// <summary>A rule, border or underline we draw and the reference does not.</summary>
    ExtraRule,

    /// <summary>A shaded panel or cell background the reference has and we do not.</summary>
    MissingFill,

    /// <summary>A shaded panel or cell background we draw and the reference does not.</summary>
    ExtraFill,

    /// <summary>The same quantity of ink, in a different place — a reflow.</summary>
    Displaced,
}

/// <summary>One cluster of differing ink, located and characterised.</summary>
public sealed class InkRegion
{
    /// <summary>Where the cluster sits on the page, in points from the top-left.</summary>
    public required Rect Bounds { get; init; }

    /// <summary>Fraction of the page area the cluster's bounding box covers.</summary>
    public required double PageFraction { get; init; }

    /// <summary>Mean ink level (0-1) inside the cluster in the document under test.</summary>
    public required double ActualInk { get; init; }

    /// <summary>Mean ink level (0-1) inside the cluster in the reference.</summary>
    public required double ReferenceInk { get; init; }

    /// <summary>Positive when we draw more ink than the reference here.</summary>
    public double InkGap => ActualInk - ReferenceInk;

    public required InkRegionKind Kind { get; init; }

    /// <summary>A reader's description of the position: "top-left", "middle-centre".</summary>
    public required string Where { get; init; }

    /// <summary>Text the reference places inside the cluster, if any.</summary>
    public required IReadOnlyList<string> ReferenceText { get; init; }

    /// <summary>Text we place inside the cluster, if any.</summary>
    public required IReadOnlyList<string> ActualText { get; init; }

    public string Summary => Kind switch
    {
        InkRegionKind.MissingInk => "ink missing from ours — glyphs, a graphic or a fill",
        InkRegionKind.ExtraInk => "ink we draw that the reference does not",
        InkRegionKind.MissingRule => "a rule or border the reference draws and we do not",
        InkRegionKind.ExtraRule => "a rule or border we draw and the reference does not",
        InkRegionKind.MissingFill => "a shaded area the reference has and we do not",
        InkRegionKind.ExtraFill => "a shaded area we draw and the reference does not",
        _ => "the same ink, displaced or reshaped",
    };
}

/// <summary>The ink comparison of one page pair.</summary>
public sealed class InkPageDiff
{
    public required bool GeometryMatches { get; init; }

    public required double ActualWidth { get; init; }

    public required double ActualHeight { get; init; }

    public required double ReferenceWidth { get; init; }

    public required double ReferenceHeight { get; init; }

    /// <summary>Fraction of cells whose ink differs by more than the tolerance.</summary>
    public required double DifferingFraction { get; init; }

    /// <summary>Mean absolute ink difference over the page, 0-1.</summary>
    public required double MeanAbsError { get; init; }

    public required double ActualInkCoverage { get; init; }

    public required double ReferenceInkCoverage { get; init; }

    /// <summary>Ours ÷ reference. 1.0 means the same total amount of ink was laid down.</summary>
    public required double InkRatio { get; init; }

    /// <summary>
    /// Jaccard overlap of the inked cells: |A∩B| ÷ |A∪B|. The single most useful number
    /// here — it is 1.0 only when ink lands in the same places, falls smoothly as content
    /// drifts, and unlike <see cref="DifferingFraction"/> is not flattered by the fact
    /// that most of a page is blank paper.
    /// </summary>
    public required double InkIoU { get; init; }

    /// <summary>
    /// Best-fit vertical displacement of the whole page, in points. Positive means our
    /// content sits lower down the page than the reference's.
    /// </summary>
    public required double VerticalShiftPt { get; init; }

    /// <summary>
    /// True when regions were located after compensating for <see cref="VerticalShiftPt"/>.
    ///
    /// <para>
    /// A page-wide shift of even a point puts a sliver of difference along the top and
    /// bottom edge of every line on the page, which buries the handful of regions that are
    /// something other than "everything moved". Compensating before clustering leaves the
    /// residual differences — what is wrong <i>beyond</i> the shift. The metrics above are
    /// always measured unaligned, so the shift itself still costs what it should.
    /// </para>
    /// </summary>
    public required bool RegionsShiftCompensated { get; init; }

    /// <summary>The reported regions, largest and most explanatory first.</summary>
    public required IReadOnlyList<InkRegion> Regions { get; init; }

    /// <summary>
    /// How many regions were found before the per-page cap was applied. When this exceeds
    /// <c>Regions.Count</c> the report says so: a truncated list that looks complete reads
    /// as "that is all there is", which is the one thing it must not do.
    /// </summary>
    public required int TotalRegions { get; init; }

    public bool HasDifference => TotalRegions > 0 || !GeometryMatches;
}

/// <summary>Knobs for the ink comparison, all expressed in points so they survive a DPI change.</summary>
public sealed class InkDiffOptions
{
    /// <summary>Raster resolution. 144 = two cells per point.</summary>
    public double Dpi { get; init; } = 144.0;

    /// <summary>
    /// Ink levels (0-255) closer than this count as the same.
    ///
    /// <para>
    /// A comparison of two real rasterisations needs this up around 40, because two
    /// renderers that agree about where a glyph goes still disagree by 20-30 along its
    /// antialiased edge. The raster here is drawn from measured ink boxes and has no such
    /// noise, so the threshold only has to clear genuine sub-cell coverage — and staying
    /// low is what lets a 10%-grey panel that is entirely missing register at all, since
    /// its whole signal is 34 levels.
    /// </para>
    /// </summary>
    public int Threshold { get; init; } = 20;

    /// <summary>
    /// Differing cells within this distance of each other join one region. 1.5pt joins
    /// the strokes of a word without joining two columns of a table.
    /// </summary>
    public double DilatePt { get; init; } = 1.5;

    /// <summary>Regions covering less of the page than this are not worth reporting.</summary>
    public double MinRegionFraction { get; init; } = 0.0004;

    /// <summary>
    /// Most regions to report per page. Ink-imbalanced regions are kept ahead of merely
    /// displaced ones, so a cap never drops a missing thing in favour of a moved one; the
    /// report states the total when it truncates.
    /// </summary>
    public int MaxRegions { get; init; } = 25;
}

/// <summary>
/// Diffs two <see cref="InkRaster"/>s and clusters the differing cells into regions.
///
/// <para>
/// Clustering is the point. A percentage tells you a page is wrong; a list of boxes tells
/// you <i>the header rule is missing and the body block starts 28pt too high</i>, which is
/// a stylesheet edit. The approach follows the one used to compare LibreOffice against its
/// C# port: threshold, dilate, connected components, then judge each component by whether
/// one side has substantially more ink in it than the other — because ink <i>imbalance</i>
/// separates a missing thing from a moved thing, and only the first is usually a bug in
/// what you drew rather than in where you put it.
/// </para>
/// </summary>
public static class InkDiff
{
    public static InkPageDiff Compare(
        PdfPageModel actual, PdfPageModel reference, InkDiffOptions? options = null)
    {
        options ??= new InkDiffOptions();

        bool geometryMatches =
            Math.Abs(actual.Width - reference.Width) < 1.0 &&
            Math.Abs(actual.Height - reference.Height) < 1.0;

        // Different paper sizes are a finding, not a reason to give up: both pages are
        // drawn onto the union canvas so everything still aligns from the top-left corner
        // and the rest of the report stays meaningful.
        var canvas = (Math.Max(actual.Width, reference.Width), Math.Max(actual.Height, reference.Height));
        InkRaster a = InkRaster.Render(actual, options.Dpi, canvas);
        InkRaster b = InkRaster.Render(reference, options.Dpi, canvas);

        int w = a.Width, h = a.Height;
        int n = w * h;
        long differing = 0, absError = 0, both = 0, either = 0;

        // Metrics are measured with the two pages laid exactly on top of each other. A
        // displaced page really is wrong, and aligning before measuring would score it as
        // if it were not.
        for (int i = 0; i < n; i++)
        {
            int av = a.Cells[i], bv = b.Cells[i];
            int d = Math.Abs(av - bv);
            absError += d;
            if (d > options.Threshold)
            {
                differing++;
            }
            bool ai = av >= InkRaster.InkThreshold;
            bool bi = bv >= InkRaster.InkThreshold;
            if (ai || bi)
            {
                either++;
                if (ai && bi)
                {
                    both++;
                }
            }
        }

        double inkA = a.InkCoverage();
        double inkB = b.InkCoverage();

        // Regions, by contrast, are located after compensating for a page-wide shift, so
        // the list says what is wrong beyond the shift rather than restating it once per
        // line. `alignment` is in raster cells: our row y answers the reference's row
        // y + alignment.
        int alignment = BestAlignment(a, b);
        var mask = new byte[n];
        for (int y = 0; y < h; y++)
        {
            int sourceY = y - alignment;
            int row = y * w;
            int sourceRow = sourceY * w;
            for (int x = 0; x < w; x++)
            {
                int av = sourceY >= 0 && sourceY < h ? a.Cells[sourceRow + x] : 0;
                if (Math.Abs(av - b.Cells[row + x]) > options.Threshold)
                {
                    mask[row + x] = 1;
                }
            }
        }

        int dilateCells = Math.Max(1, (int)Math.Round(options.DilatePt * a.Scale));
        byte[] dilated = Dilate(mask, w, h, dilateCells);

        int minCells = Math.Max(4, (int)Math.Round(options.MinRegionFraction * n));
        var allRegions = Cluster(dilated, w, h, minCells)
            .Select(box => Describe(box, a, b, actual, reference, alignment))
            // Regions where one side has ink the other does not come first: those are the
            // ones that mean something is absent rather than merely somewhere else.
            .OrderBy(r => r.Kind == InkRegionKind.Displaced ? 1 : 0)
            .ThenByDescending(r => r.Bounds.Area)
            .ToArray();
        var regions = allRegions.Take(options.MaxRegions).ToArray();

        return new InkPageDiff
        {
            GeometryMatches = geometryMatches,
            ActualWidth = actual.Width,
            ActualHeight = actual.Height,
            ReferenceWidth = reference.Width,
            ReferenceHeight = reference.Height,
            DifferingFraction = (double)differing / n,
            MeanAbsError = (double)absError / n / 255.0,
            ActualInkCoverage = inkA,
            ReferenceInkCoverage = inkB,
            InkRatio = inkB > 1e-9 ? inkA / inkB : (inkA <= 1e-9 ? 1.0 : double.PositiveInfinity),
            InkIoU = either > 0 ? (double)both / either : 1.0,
            // Negated so the sign reads the same way as the structural diff's: positive
            // means ours sits lower down the page.
            VerticalShiftPt = -a.ToPoints(alignment),
            RegionsShiftCompensated = alignment != 0,
            Regions = regions,
            TotalRegions = allRegions.Length,
        };
    }

    // ---------------------------------------------------------------------------- masking

    /// <summary>
    /// Grow the mask by <paramref name="radius"/> cells, separably: a running countdown
    /// along each row, then each column. Linear in the number of cells, where the obvious
    /// box-filter version would be quadratic in the radius.
    /// </summary>
    private static byte[] Dilate(byte[] mask, int w, int h, int radius)
    {
        var wide = new byte[mask.Length];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int run = 0;
            for (int x = 0; x < w; x++)
            {
                run = mask[row + x] != 0 ? radius + 1 : Math.Max(run - 1, 0);
                if (run > 0)
                {
                    wide[row + x] = 1;
                }
            }
            run = 0;
            for (int x = w - 1; x >= 0; x--)
            {
                run = mask[row + x] != 0 ? radius + 1 : Math.Max(run - 1, 0);
                if (run > 0)
                {
                    wide[row + x] = 1;
                }
            }
        }

        var outMask = new byte[mask.Length];
        for (int x = 0; x < w; x++)
        {
            int run = 0;
            for (int y = 0; y < h; y++)
            {
                run = wide[(y * w) + x] != 0 ? radius + 1 : Math.Max(run - 1, 0);
                if (run > 0)
                {
                    outMask[(y * w) + x] = 1;
                }
            }
            run = 0;
            for (int y = h - 1; y >= 0; y--)
            {
                run = wide[(y * w) + x] != 0 ? radius + 1 : Math.Max(run - 1, 0);
                if (run > 0)
                {
                    outMask[(y * w) + x] = 1;
                }
            }
        }
        return outMask;
    }

    private readonly record struct Box(int X0, int Y0, int X1, int Y1, long Cells);

    /// <summary>Connected components of the dilated mask, as bounding boxes.</summary>
    private static List<Box> Cluster(byte[] mask, int w, int h, int minCells)
    {
        var seen = new bool[mask.Length];
        var found = new List<Box>();
        var stack = new Stack<int>();

        for (int start = 0; start < mask.Length; start++)
        {
            if (mask[start] == 0 || seen[start])
            {
                continue;
            }
            stack.Push(start);
            seen[start] = true;
            int x0 = start % w, x1 = x0, y0 = start / w, y1 = y0;
            long cells = 0;

            while (stack.Count > 0)
            {
                int p = stack.Pop();
                cells++;
                int px = p % w, py = p / w;
                if (px < x0) { x0 = px; }
                if (px > x1) { x1 = px; }
                if (py < y0) { y0 = py; }
                if (py > y1) { y1 = py; }

                if (px > 0) { Push(p - 1); }
                if (px + 1 < w) { Push(p + 1); }
                if (py > 0) { Push(p - w); }
                if (py + 1 < h) { Push(p + w); }
            }

            if (cells >= minCells)
            {
                found.Add(new Box(x0, y0, x1, y1, cells));
            }

            void Push(int q)
            {
                if (mask[q] != 0 && !seen[q])
                {
                    seen[q] = true;
                    stack.Push(q);
                }
            }
        }
        return found;
    }

    // ------------------------------------------------------------------------ description

    /// <summary>
    /// Measure and classify one clustered box. Coordinates are in the <b>reference</b>
    /// page's frame; our side is read <paramref name="alignment"/> cells away, which is
    /// what makes a shifted page's regions line up with the reference the reader is
    /// looking at.
    /// </summary>
    private static InkRegion Describe(
        Box box, InkRaster a, InkRaster b, PdfPageModel actual, PdfPageModel reference, int alignment)
    {
        int bw = box.X1 - box.X0 + 1;
        int bh = box.Y1 - box.Y0 + 1;

        // Large regions are sampled rather than walked: the mean is what matters and a
        // few thousand samples pin it down as well as a million do.
        int step = Math.Max(1, (bw * bh) / 4000);
        long sumA = 0, sumB = 0, count = 0;
        for (int y = box.Y0; y <= box.Y1; y++)
        {
            int row = y * a.Width;
            int sourceY = y - alignment;
            int sourceRow = sourceY * a.Width;
            bool inRange = sourceY >= 0 && sourceY < a.Height;
            for (int x = box.X0; x <= box.X1; x += step)
            {
                sumA += inRange ? a.Cells[sourceRow + x] : 0;
                sumB += b.Cells[row + x];
                count++;
            }
        }
        count = Math.Max(1, count);
        double inkA = sumA / (double)count / 255.0;
        double inkB = sumB / (double)count / 255.0;

        var bounds = new Rect(
            a.ToPoints(box.X0), a.ToPoints(box.Y0), a.ToPoints(bw), a.ToPoints(bh));
        // The same box on our page, which is where our text and marks have to be looked for.
        var ourBounds = bounds with { Y = bounds.Y - a.ToPoints(alignment) };

        // The gap threshold scales with how much ink the region holds, so a faint 10%
        // grey panel that is entirely absent still registers as missing.
        double gap = inkA - inkB;
        double gapThreshold = Math.Max(0.06, 0.35 * Math.Max(inkA, inkB));

        return new InkRegion
        {
            Bounds = bounds,
            PageFraction = (double)box.Cells / (a.Width * (long)a.Height),
            ActualInk = inkA,
            ReferenceInk = inkB,
            Kind = Classify(bounds, gap, gapThreshold, actual, reference, ourBounds),
            Where = Where(bounds, a.PageWidth, a.PageHeight),
            ReferenceText = TextIn(reference, bounds),
            ActualText = TextIn(actual, ourBounds),
        };
    }

    /// <summary>
    /// Decide what a region is by looking at the marks that fall inside it on each side,
    /// not at the shape of the differing pixels.
    ///
    /// <para>
    /// Judging by pixel shape alone gets this wrong in a way that matters: a dilated
    /// paragraph fills its own bounding box just as completely as a shaded panel does, so
    /// a block of text present on one side reads as "a fill we draw and the reference does
    /// not" and sends the reader looking for a <c>background-color</c> that was never
    /// involved. The page model already knows whether a rule, a fill or a run of glyphs
    /// sits there, so it is asked.
    /// </para>
    /// </summary>
    private static InkRegionKind Classify(
        Rect bounds, double gap, double gapThreshold,
        PdfPageModel actual, PdfPageModel reference, Rect ourBounds)
    {
        if (Math.Abs(gap) <= gapThreshold)
        {
            return InkRegionKind.Displaced;
        }

        // A negative gap means the reference is darker here, so the missing thing is one of
        // the reference's marks; a positive gap points at ours.
        bool missing = gap < 0;
        PdfPageModel source = missing ? reference : actual;
        PdfPageModel other = missing ? actual : reference;
        Rect sourceBox = missing ? bounds : ourBounds;
        Rect otherBox = missing ? ourBounds : bounds;

        // A rule is only called a rule when the region is itself rule-shaped. A table
        // border inside a large block of differing text is present, but it is not what the
        // region is about, and naming it would send the reader to the wrong place.
        double thin = Math.Min(bounds.Width, bounds.Height);
        double along = Math.Max(bounds.Width, bounds.Height);
        bool regionIsRuleShaped = thin <= 6.0 && along >= thin * 6;

        if (regionIsRuleShaped && Has(source, sourceBox, MarkKind.Rule) && !Has(other, otherBox, MarkKind.Rule))
        {
            return missing ? InkRegionKind.MissingRule : InkRegionKind.ExtraRule;
        }
        // A fill has to account for most of the region before the region is called a fill;
        // otherwise a shaded table header inside a differing block would rename the block.
        if (FillCoverage(source, sourceBox) > 0.5 && FillCoverage(other, otherBox) < 0.2)
        {
            return missing ? InkRegionKind.MissingFill : InkRegionKind.ExtraFill;
        }
        return missing ? InkRegionKind.MissingInk : InkRegionKind.ExtraInk;

        static bool Has(PdfPageModel page, Rect box, MarkKind kind) =>
            page.Graphics.Any(g => g.Kind == kind && g.Bounds.IntersectionArea(box) > g.Bounds.Area * 0.25);
    }

    /// <summary>Share of a region covered by filled areas, capped at 1 when fills overlap.</summary>
    private static double FillCoverage(PdfPageModel page, Rect region)
    {
        if (region.Area <= 0)
        {
            return 0;
        }
        double covered = page.Graphics
            .Where(g => g.Kind == MarkKind.Fill)
            .Sum(g => g.Bounds.IntersectionArea(region));
        return Math.Min(1.0, covered / region.Area);
    }

    /// <summary>Text lines the region overlaps, so a box on the page has words attached to it.</summary>
    private static IReadOnlyList<string> TextIn(PdfPageModel page, Rect region)
    {
        var hits = new List<string>();
        foreach (TextLine line in page.Lines)
        {
            // Half of a line inside the box is enough: a region often clips a line's edge.
            if (line.Bounds.IntersectionArea(region) > line.Bounds.Area * 0.4)
            {
                hits.Add(line.Text);
                if (hits.Count == 6)
                {
                    break;
                }
            }
        }
        return hits;
    }

    private static string Where(Rect r, double pageWidth, double pageHeight)
    {
        if (r.Width > 0.8 * pageWidth && r.Height > 0.8 * pageHeight)
        {
            return "the whole page";
        }
        double cx = r.CentreX / Math.Max(1, pageWidth);
        double cy = r.CentreY / Math.Max(1, pageHeight);
        string row = cy < 0.33 ? "top" : cy < 0.67 ? "middle" : "bottom";
        string col = cx < 0.33 ? "left" : cx < 0.67 ? "centre" : "right";
        return $"{row}-{col}";
    }

    /// <summary>
    /// The vertical offset, in raster cells, that best aligns the two ink-per-row
    /// profiles: our row <c>y</c> answers the reference's row <c>y + result</c>. A
    /// non-zero result is the signature of a reflow cascade — the content is all there,
    /// and one measurement upstream pushed it down the page.
    /// </summary>
    private static int BestAlignment(InkRaster a, InkRaster b)
    {
        int[] pa = a.RowProfile(), pb = b.RowProfile();
        int n = Math.Min(pa.Length, pb.Length);
        if (n == 0)
        {
            return 0;
        }
        int limit = Math.Min((int)(40 * a.Scale), Math.Max(1, n / 4));
        int bestShift = 0;
        double bestCost = double.MaxValue;
        for (int shift = -limit; shift <= limit; shift++)
        {
            long cost = 0;
            int count = 0;
            for (int y = 0; y < n; y++)
            {
                int sy = y + shift;
                if (sy >= 0 && sy < n)
                {
                    cost += Math.Abs(pa[y] - pb[sy]);
                    count++;
                }
            }
            if (count == 0)
            {
                continue;
            }
            double norm = (double)cost / count;
            if (norm < bestCost)
            {
                bestCost = norm;
                bestShift = shift;
            }
        }
        return bestShift;
    }

    /// <summary>
    /// Write the diagnostic images for a page: ours, the reference, and the reference
    /// faded with each differing region boxed in red.
    /// </summary>
    public static void WriteImages(
        string directory, int pageNumber,
        PdfPageModel actual, PdfPageModel reference, InkPageDiff diff, InkDiffOptions options)
    {
        var canvas = (Math.Max(actual.Width, reference.Width), Math.Max(actual.Height, reference.Height));
        InkRaster a = InkRaster.Render(actual, options.Dpi, canvas);
        InkRaster b = InkRaster.Render(reference, options.Dpi, canvas);

        Directory.CreateDirectory(directory);
        PngWriter.WriteGray(Path.Combine(directory, $"page-{pageNumber:D3}-actual.png"),
            a.Width, a.Height, Invert(a.Cells));
        PngWriter.WriteGray(Path.Combine(directory, $"page-{pageNumber:D3}-reference.png"),
            b.Width, b.Height, Invert(b.Cells));
        PngWriter.WriteRgb(Path.Combine(directory, $"page-{pageNumber:D3}-diff.png"),
            b.Width, b.Height, Annotate(b, diff.Regions));
    }

    /// <summary>Ink is accumulated darkness; an image wants brightness.</summary>
    private static byte[] Invert(byte[] ink)
    {
        var px = new byte[ink.Length];
        for (int i = 0; i < ink.Length; i++)
        {
            px[i] = (byte)(255 - ink[i]);
        }
        return px;
    }

    private static byte[] Annotate(InkRaster reference, IReadOnlyList<InkRegion> regions)
    {
        int w = reference.Width, h = reference.Height;
        var rgb = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            // The reference is faded to a third of its contrast so the red boxes read
            // over it while the page is still recognisable underneath.
            byte pale = (byte)(255 - (reference.Cells[i] / 3));
            rgb[i * 3] = pale;
            rgb[(i * 3) + 1] = pale;
            rgb[(i * 3) + 2] = pale;
        }

        foreach (InkRegion r in regions)
        {
            int x0 = Clamp((int)(r.Bounds.Left * reference.Scale), 0, w - 1);
            int x1 = Clamp((int)(r.Bounds.Right * reference.Scale), 0, w - 1);
            int y0 = Clamp((int)(r.Bounds.Top * reference.Scale), 0, h - 1);
            int y1 = Clamp((int)(r.Bounds.Bottom * reference.Scale), 0, h - 1);

            for (int y = y0 + 1; y < y1; y++)
            {
                for (int x = x0 + 1; x < x1; x++)
                {
                    int j = ((y * w) + x) * 3;
                    rgb[j] = (byte)Math.Min(255, rgb[j] + 40);
                    rgb[j + 1] = (byte)Math.Max(0, rgb[j + 1] - 25);
                    rgb[j + 2] = (byte)Math.Max(0, rgb[j + 2] - 25);
                }
            }
            for (int x = x0; x <= x1; x++)
            {
                Mark(x, y0);
                Mark(x, y1);
            }
            for (int y = y0; y <= y1; y++)
            {
                Mark(x0, y);
                Mark(x1, y);
            }

            void Mark(int x, int y)
            {
                int j = ((y * w) + x) * 3;
                rgb[j] = 220;
                rgb[j + 1] = 0;
                rgb[j + 2] = 0;
            }
        }
        return rgb;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
}
