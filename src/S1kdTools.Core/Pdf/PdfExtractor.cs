using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;

namespace S1kdTools.Pdf;

/// <summary>
/// Reads a PDF into a <see cref="PdfDocumentModel"/>: the marks on each page, in
/// top-left points, with the font and colour a stylesheet would have chosen.
///
/// <para>
/// The parser is <a href="https://github.com/UglyToad/PdfPig">PdfPig</a>, which is pure
/// managed code — no native rasteriser, no external process. That matters here because
/// the two PDFs being compared come from different toolchains: what we want to compare
/// is <i>where things were placed and how they were styled</i>, which lives in the
/// content stream, not in the pixels a particular rasteriser happens to produce for a
/// particular font.
/// </para>
/// </summary>
public static class PdfExtractor
{
    /// <summary>Glyph boxes closer together than this share a line, scaled by font size.</summary>
    private const double BaselineToleranceFactor = 0.35;

    /// <summary>...but never less than this, so 6pt footnotes still group.</summary>
    private const double MinBaselineTolerance = 0.8;

    /// <summary>A mark lighter than this is invisible on white paper and contributes no ink.</summary>
    private const double MinDarkness = 0.04;

    /// <summary>A filled shape thinner than this reads as a rule rather than an area.</summary>
    private const double RuleThicknessPt = 3.0;

    public static PdfDocumentModel Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using PdfDocument doc = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true });
        var pages = new List<PdfPageModel>(doc.NumberOfPages);
        for (int i = 1; i <= doc.NumberOfPages; i++)
        {
            pages.Add(ReadPage(doc.GetPage(i)));
        }
        return new PdfDocumentModel { Path = path, Pages = pages };
    }

    private static PdfPageModel ReadPage(Page page)
    {
        PdfRectangle crop = page.CropBox.Bounds;
        double originX = crop.Left;
        double originTop = crop.Top;
        double width = crop.Width;
        double height = crop.Height;

        Rect ToRect(PdfRectangle r) =>
            Rect.FromEdges(r.Left - originX, originTop - r.Top, r.Right - originX, originTop - r.Bottom);

        var lines = BuildLines(page, ToRect, originTop);
        var graphics = BuildGraphics(page, ToRect, width, height);

        Rect? content = null;
        foreach (Rect r in lines.Select(l => l.Bounds).Concat(graphics.Select(g => g.Bounds)))
        {
            content = content is null ? r : content.Value.Union(r);
        }

        return new PdfPageModel
        {
            Number = page.Number,
            Width = width,
            Height = height,
            Rotation = page.Rotation.Value,
            Lines = lines,
            Graphics = graphics,
            ContentBounds = content,
        };
    }

    // ------------------------------------------------------------------------------ text

    private static IReadOnlyList<TextLine> BuildLines(
        Page page, Func<PdfRectangle, Rect> toRect, double originTop)
    {
        var words = new List<(Word Word, double Baseline, Rect Bounds)>();
        foreach (Word w in page.GetWords())
        {
            if (string.IsNullOrWhiteSpace(w.Text) || w.Letters.Count == 0)
            {
                continue;
            }
            // The baseline is taken from the letters rather than the bounding box: it is
            // stable across ascenders and descenders, which is what makes it usable as the
            // "same line" key and as the vertical position a stylesheet is responsible for.
            double baseline = originTop - Median(w.Letters.Select(l => l.StartBaseLine.Y));
            words.Add((w, baseline, toRect(w.BoundingBox)));
        }

        // Reading order: down the page, then across. Grouping walks this order once.
        words.Sort((a, b) =>
        {
            int cmp = a.Baseline.CompareTo(b.Baseline);
            return cmp != 0 ? cmp : a.Bounds.Left.CompareTo(b.Bounds.Left);
        });

        var lines = new List<TextLine>();
        var current = new List<(Word Word, double Baseline, Rect Bounds)>();

        foreach (var entry in words)
        {
            if (current.Count > 0)
            {
                double size = DominantSize(current.SelectMany(c => c.Word.Letters));
                double tolerance = Math.Max(MinBaselineTolerance, size * BaselineToleranceFactor);
                if (entry.Baseline - current[^1].Baseline > tolerance)
                {
                    lines.AddRange(MakeLines(current));
                    current.Clear();
                }
            }
            current.Add(entry);
        }
        if (current.Count > 0)
        {
            lines.AddRange(MakeLines(current));
        }

        return lines;
    }

    /// <summary>
    /// Turn one baseline's worth of words into lines, splitting where the words are
    /// separated by a column-sized gap.
    ///
    /// <para>
    /// Without this, the two cells of a table row and the two ends of a running header
    /// come back as a single line whose text is "Item Value" — which then diffs against
    /// the reference as one moved line instead of two cells at two measurable positions.
    /// Even fully justified text does not open a space of two and a half ems, so the
    /// threshold separates columns without ever splitting a sentence.
    /// </para>
    /// </summary>
    private static IEnumerable<TextLine> MakeLines(List<(Word Word, double Baseline, Rect Bounds)> parts)
    {
        var ordered = parts.OrderBy(p => p.Bounds.Left).ToList();
        double size = DominantSize(ordered.SelectMany(p => p.Word.Letters));
        double columnGap = Math.Max(10.0, size * 2.5);

        var run = new List<(Word Word, double Baseline, Rect Bounds)> { ordered[0] };
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Bounds.Left - run[^1].Bounds.Right > columnGap)
            {
                yield return MakeLine(run);
                run = new List<(Word, double, Rect)>();
            }
            run.Add(ordered[i]);
        }
        yield return MakeLine(run);
    }

    private static TextLine MakeLine(List<(Word Word, double Baseline, Rect Bounds)> parts)
    {
        // Words arrive sorted by baseline then x; within a line only x matters.
        var ordered = parts.OrderBy(p => p.Bounds.Left).ToList();
        var letters = ordered.SelectMany(p => p.Word.Letters).ToList();

        Rect bounds = ordered[0].Bounds;
        foreach (var p in ordered.Skip(1))
        {
            bounds = bounds.Union(p.Bounds);
        }

        string font = DominantFont(letters);
        double size = DominantSize(letters);
        string colour = DominantColour(letters);

        return new TextLine
        {
            Text = string.Join(" ", ordered.Select(p => p.Word.Text)),
            Bounds = bounds,
            Baseline = Median(ordered.Select(p => p.Baseline)),
            FontName = font,
            FontSize = Math.Round(size, 1),
            Bold = LooksBold(font, letters),
            Italic = LooksItalic(font, letters),
            Color = colour,
            Words = ordered.Select(p => new WordBox { Text = p.Word.Text, Bounds = p.Bounds }).ToArray(),
            GlyphCount = letters.Count,
        };
    }

    /// <summary>
    /// Strips the six-letter subset tag PDF producers prepend when they embed only the
    /// glyphs a document uses (<c>VDMHZV+Liberation Serif</c>). The tag is randomly
    /// generated per build, so leaving it in would make every font name differ between
    /// two renderings of the same document.
    /// </summary>
    public static string NormaliseFontName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "(unknown)";
        }
        if (name.Length > 7 && name[6] == '+')
        {
            name = name[7..];
        }
        return name;
    }

    private static string DominantFont(IReadOnlyList<Letter> letters) =>
        letters.GroupBy(l => NormaliseFontName(l.FontDetails.Name))
               .OrderByDescending(g => g.Count())
               .Select(g => g.Key)
               .FirstOrDefault() ?? "(unknown)";

    private static double DominantSize(IEnumerable<Letter> letters)
    {
        var group = letters.GroupBy(l => Math.Round(l.PointSize, 1))
                           .OrderByDescending(g => g.Count())
                           .FirstOrDefault();
        return group?.Key ?? 0;
    }

    private static string DominantColour(IReadOnlyList<Letter> letters) =>
        letters.GroupBy(l => ToHex(l.Color))
               .OrderByDescending(g => g.Count())
               .Select(g => g.Key)
               .FirstOrDefault() ?? "#000000";

    // FOP and most other producers encode weight in the font name rather than in the
    // descriptor flags PdfPig surfaces, so the name is checked first and trusted.
    private static bool LooksBold(string font, IReadOnlyList<Letter> letters) =>
        font.Contains("bold", StringComparison.OrdinalIgnoreCase)
        || font.Contains("black", StringComparison.OrdinalIgnoreCase)
        || letters.Any(l => l.FontDetails.IsBold || l.FontDetails.Weight >= 600);

    private static bool LooksItalic(string font, IReadOnlyList<Letter> letters) =>
        font.Contains("italic", StringComparison.OrdinalIgnoreCase)
        || font.Contains("oblique", StringComparison.OrdinalIgnoreCase)
        || letters.Any(l => l.FontDetails.IsItalic);

    // -------------------------------------------------------------------------- graphics

    private static IReadOnlyList<GraphicMark> BuildGraphics(
        Page page, Func<PdfRectangle, Rect> toRect, double pageWidth, double pageHeight)
    {
        var marks = new List<GraphicMark>();

        foreach (var path in page.Paths)
        {
            PdfRectangle? box = path.GetBoundingRectangle();
            if (box is null)
            {
                continue;
            }
            IColor? colour = path.IsFilled ? path.FillColor : path.StrokeColor;
            double darkness = Darkness(colour);
            // A white fill paints nothing on white paper. Producers emit page-sized white
            // rectangles routinely; counting them would put an "ink" difference on every
            // page purely because one toolchain paints its background and the other does not.
            if (darkness < MinDarkness)
            {
                continue;
            }

            Rect r = toRect(box.Value);
            if (r.Width <= 0 && r.Height <= 0)
            {
                continue;
            }

            double thin = Math.Min(r.Width, r.Height);
            double along = Math.Max(r.Width, r.Height);
            MarkKind kind = thin <= RuleThicknessPt && along >= thin * 4
                ? MarkKind.Rule
                : path.IsFilled ? MarkKind.Fill : MarkKind.Stroke;

            marks.Add(new GraphicMark
            {
                Kind = kind,
                Bounds = r,
                Color = ToHex(colour),
                Darkness = darkness,
            });
        }

        foreach (IPdfImage image in page.GetImages())
        {
            marks.Add(new GraphicMark
            {
                Kind = MarkKind.Image,
                Bounds = toRect(image.BoundingBox),
                Color = "(image)",
                // An image's true coverage would need decoding it; assume a mid-grey block,
                // which keeps a present/absent image obvious without pretending to know more.
                Darkness = 0.5,
            });
        }

        return marks;
    }

    // ----------------------------------------------------------------------------- colour

    private static string ToHex(IColor? colour)
    {
        if (colour is null)
        {
            return "#000000";
        }
        (double r, double g, double b) = colour.ToRGBValues();
        return $"#{Channel(r):x2}{Channel(g):x2}{Channel(b):x2}";
    }

    private static int Channel(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>Perceptual darkness: 0 for white, 1 for black.</summary>
    private static double Darkness(IColor? colour)
    {
        if (colour is null)
        {
            return 1.0;
        }
        (double r, double g, double b) = colour.ToRGBValues();
        return Math.Clamp(1.0 - ((0.299 * r) + (0.587 * g) + (0.114 * b)), 0, 1);
    }

    /// <summary>Darkness of a <c>#rrggbb</c> string, for marks already reduced to hex.</summary>
    public static double DarknessOf(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            return 1.0;
        }
        double r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        double g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        double b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        return Math.Clamp(1.0 - ((0.299 * r) + (0.587 * g) + (0.114 * b)), 0, 1);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
