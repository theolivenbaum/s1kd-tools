namespace S1kdTools.Pdf;

/// <summary>
/// A page turned into a greyscale image of where ink lands.
///
/// <para>
/// The raster is drawn from the page model rather than by a font rasteriser, and that is
/// a deliberate choice. The two PDFs under comparison come from different toolchains and
/// will almost never embed the same font programs, so a true rasteriser would report a
/// difference on every glyph edge of every matching line — the antialiasing noise that
/// makes a raw "83% of pixels differ" meaningless. Painting each mark's measured ink
/// box instead makes the metric answer the question a stylesheet author actually has:
/// <i>is the ink in the right place, in the right amount?</i>
/// </para>
///
/// <para>
/// The cost of that choice is real and worth stating: a glyph-shape difference (wrong
/// typeface at the same size and width) is invisible here. It shows up in the structural
/// diff instead, as a font-name change on the line.
/// </para>
/// </summary>
public sealed class InkRaster
{
    /// <summary>Cells are 8-bit ink: 0 = bare paper, 255 = solid black.</summary>
    public byte[] Cells { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Cells per point. 2.0 means one cell per half-point.</summary>
    public double Scale { get; }

    /// <summary>The page this was drawn from, in points.</summary>
    public double PageWidth { get; }

    public double PageHeight { get; }

    /// <summary>A cell at or above this ink level counts as inked for coverage and IoU.</summary>
    public const byte InkThreshold = 24;

    private InkRaster(int width, int height, double scale, double pageWidth, double pageHeight)
    {
        Width = width;
        Height = height;
        Scale = scale;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
        Cells = new byte[width * height];
    }

    /// <summary>
    /// Rasterise a page at <paramref name="dpi"/>. The default of 144 is two cells per
    /// point: fine enough that a one-point shift moves content by two cells, coarse
    /// enough that an A4 page is a couple of million bytes and diffs in milliseconds.
    /// </summary>
    /// <param name="canvas">
    /// Optional page size to draw into, in points. When two documents disagree about
    /// paper size, both are rasterised onto the larger canvas so the diff still lines up
    /// at the top-left rather than being refused outright — the size difference is
    /// reported separately, as a page-geometry finding.
    /// </param>
    public static InkRaster Render(PdfPageModel page, double dpi = 144.0, (double W, double H)? canvas = null)
    {
        double scale = dpi / 72.0;
        double w = canvas?.W ?? page.Width;
        double h = canvas?.H ?? page.Height;
        int cw = Math.Max(1, (int)Math.Ceiling(w * scale));
        int ch = Math.Max(1, (int)Math.Ceiling(h * scale));
        var raster = new InkRaster(cw, ch, scale, w, h);

        foreach (GraphicMark g in page.Graphics)
        {
            // Rules and hairlines can be thinner than a cell. Painting them at their true
            // sub-cell height would leave a faint smear that the diff threshold swallows,
            // so a rule always gets at least one full cell of presence.
            raster.Paint(g.Bounds, g.Darkness, minimumCells: g.Kind == MarkKind.Rule ? 1.0 : 0.0);
        }

        foreach (TextLine line in page.Lines)
        {
            double darkness = PdfExtractor.DarknessOf(line.Color);
            foreach (WordBox word in line.Words)
            {
                // A word box is the tight ink extent of its glyphs, but glyphs do not fill
                // it — roughly 45% of the box is stem and bowl for body text. Scaling the
                // intensity keeps a page's absolute ink coverage in the same range a real
                // rasteriser would report, so "ink per page" reads as a plausible number
                // rather than an inflated one.
                raster.Paint(word.Bounds, darkness * GlyphCoverage, minimumCells: 0.5);
            }
        }

        return raster;
    }

    /// <summary>Fraction of a word box that glyph strokes actually cover, empirically.</summary>
    private const double GlyphCoverage = 0.45;

    /// <summary>
    /// Paint a rectangle, accumulating fractional coverage at the edges. Antialiasing the
    /// edges is what lets sub-point shifts show up as a smooth change rather than snapping
    /// between whole cells, which in turn is what makes the shift search reliable.
    /// </summary>
    private void Paint(Rect r, double intensity, double minimumCells)
    {
        if (intensity <= 0)
        {
            return;
        }

        double x0 = r.Left * Scale;
        double y0 = r.Top * Scale;
        double x1 = r.Right * Scale;
        double y1 = r.Bottom * Scale;

        if (x1 - x0 < minimumCells)
        {
            double mid = (x0 + x1) / 2;
            x0 = mid - (minimumCells / 2);
            x1 = mid + (minimumCells / 2);
        }
        if (y1 - y0 < minimumCells)
        {
            double mid = (y0 + y1) / 2;
            y0 = mid - (minimumCells / 2);
            y1 = mid + (minimumCells / 2);
        }

        int cx0 = Math.Max(0, (int)Math.Floor(x0));
        int cy0 = Math.Max(0, (int)Math.Floor(y0));
        int cx1 = Math.Min(Width - 1, (int)Math.Ceiling(x1) - 1);
        int cy1 = Math.Min(Height - 1, (int)Math.Ceiling(y1) - 1);

        for (int cy = cy0; cy <= cy1; cy++)
        {
            double coverY = Math.Min(cy + 1, y1) - Math.Max(cy, y0);
            if (coverY <= 0)
            {
                continue;
            }
            int row = cy * Width;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                double coverX = Math.Min(cx + 1, x1) - Math.Max(cx, x0);
                if (coverX <= 0)
                {
                    continue;
                }
                int add = (int)Math.Round(intensity * coverX * coverY * 255);
                if (add <= 0)
                {
                    continue;
                }
                int v = Cells[row + cx] + add;
                Cells[row + cx] = v >= 255 ? (byte)255 : (byte)v;
            }
        }
    }

    /// <summary>Fraction of the page carrying ink. The headline "how full is this page".</summary>
    public double InkCoverage()
    {
        long inked = 0;
        foreach (byte c in Cells)
        {
            if (c >= InkThreshold)
            {
                inked++;
            }
        }
        return (double)inked / Cells.Length;
    }

    /// <summary>Number of inked cells.</summary>
    public long InkCells()
    {
        long inked = 0;
        foreach (byte c in Cells)
        {
            if (c >= InkThreshold)
            {
                inked++;
            }
        }
        return inked;
    }

    /// <summary>Ink per row of cells, used to detect a page-wide vertical shift.</summary>
    public int[] RowProfile()
    {
        var profile = new int[Height];
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            int n = 0;
            for (int x = 0; x < Width; x++)
            {
                if (Cells[row + x] >= InkThreshold)
                {
                    n++;
                }
            }
            profile[y] = n;
        }
        return profile;
    }

    /// <summary>Convert a cell coordinate back to points, for reporting positions.</summary>
    public double ToPoints(int cells) => cells / Scale;
}
