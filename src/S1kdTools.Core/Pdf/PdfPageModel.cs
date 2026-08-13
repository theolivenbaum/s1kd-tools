namespace S1kdTools.Pdf;

/// <summary>
/// An axis-aligned rectangle in <b>points</b>, with the origin at the <b>top-left</b>
/// of the page and <c>Y</c> increasing downwards.
///
/// <para>
/// PDF itself uses a bottom-left origin, but every consumer of this model — a human
/// reading the report, and an agent mapping a difference back onto an XSL-FO
/// stylesheet — thinks top-down, the way a page is read and the way FO lays one out.
/// The conversion happens once, in <see cref="PdfExtractor"/>; nothing downstream ever
/// sees PDF's coordinate space.
/// </para>
/// </summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CentreX => X + (Width / 2);

    public double CentreY => Y + (Height / 2);

    public double Area => Width * Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static Rect FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    public Rect Union(Rect other) => FromEdges(
        Math.Min(Left, other.Left), Math.Min(Top, other.Top),
        Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    public bool Intersects(Rect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    /// <summary>Area of the overlap with <paramref name="other"/>, zero when disjoint.</summary>
    public double IntersectionArea(Rect other)
    {
        double w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        double h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }

    public override string ToString() =>
        $"({X:F1},{Y:F1}) {Width:F1}x{Height:F1}pt";
}

/// <summary>How a mark was painted, which is what decides how a difference reads.</summary>
public enum MarkKind
{
    /// <summary>A run of glyphs.</summary>
    Text,

    /// <summary>A thin filled or stroked shape: a horizontal rule, a table border, an underline.</summary>
    Rule,

    /// <summary>A filled area: cell shading, a highlight panel, a coloured background.</summary>
    Fill,

    /// <summary>A stroked path that is not thin enough to read as a rule.</summary>
    Stroke,

    /// <summary>A raster or embedded image (an ICN, a logo).</summary>
    Image,
}

/// <summary>One word: the smallest text unit the comparison reasons about.</summary>
public sealed class WordBox
{
    public required string Text { get; init; }

    public required Rect Bounds { get; init; }

    public override string ToString() => $"{Text} @ {Bounds}";
}

/// <summary>
/// A run of words sharing a baseline. Lines — not letters, and not paragraphs — are the
/// unit the structural diff aligns on: they survive rewrapping badly enough to expose it,
/// and they carry exactly the properties a stylesheet sets (font, size, weight, colour,
/// indent, baseline position).
/// </summary>
public sealed class TextLine
{
    public required string Text { get; init; }

    public required Rect Bounds { get; init; }

    /// <summary>Baseline distance from the top of the page, in points.</summary>
    public required double Baseline { get; init; }

    /// <summary>The font covering most glyphs on the line, with any subset prefix stripped.</summary>
    public required string FontName { get; init; }

    /// <summary>Point size of the dominant font, rounded to 0.1pt.</summary>
    public required double FontSize { get; init; }

    public required bool Bold { get; init; }

    public required bool Italic { get; init; }

    /// <summary>Dominant colour as <c>#rrggbb</c>.</summary>
    public required string Color { get; init; }

    public required IReadOnlyList<WordBox> Words { get; init; }

    public int GlyphCount { get; init; }

    /// <summary>A compact style key: two lines with the same key are styled identically.</summary>
    public string StyleKey =>
        $"{FontName} {FontSize:F1}pt{(Bold ? " bold" : "")}{(Italic ? " italic" : "")} {Color}";

    public override string ToString() => $"[{Baseline:F1}] {Text}";
}

/// <summary>A non-text mark: a rule, a shaded area, a stroked path, an image.</summary>
public sealed class GraphicMark
{
    public required MarkKind Kind { get; init; }

    public required Rect Bounds { get; init; }

    /// <summary>Paint colour as <c>#rrggbb</c>; the fill colour when the mark is filled.</summary>
    public required string Color { get; init; }

    /// <summary>0 = white, 1 = black. Drives how much ink the mark contributes.</summary>
    public required double Darkness { get; init; }

    public override string ToString() => $"{Kind} {Bounds} {Color}";
}

/// <summary>Everything on one page that can differ between two renderings.</summary>
public sealed class PdfPageModel
{
    /// <summary>1-based page number.</summary>
    public required int Number { get; init; }

    /// <summary>Page width in points (the crop box).</summary>
    public required double Width { get; init; }

    /// <summary>Page height in points.</summary>
    public required double Height { get; init; }

    public required int Rotation { get; init; }

    public required IReadOnlyList<TextLine> Lines { get; init; }

    public required IReadOnlyList<GraphicMark> Graphics { get; init; }

    /// <summary>Bounding box of every mark on the page; null when the page is blank.</summary>
    public Rect? ContentBounds { get; init; }

    /// <summary>Every word on the page in reading order.</summary>
    public IReadOnlyList<string> Words =>
        _words ??= Lines.SelectMany(l => l.Words).Select(w => w.Text).ToArray();

    private IReadOnlyList<string>? _words;

    public int WordCount => Words.Count;

    public string Text => string.Join("\n", Lines.Select(l => l.Text));
}

/// <summary>A whole PDF, reduced to the marks on its pages.</summary>
public sealed class PdfDocumentModel
{
    public required string Path { get; init; }

    public required IReadOnlyList<PdfPageModel> Pages { get; init; }

    public int PageCount => Pages.Count;

    public int WordCount => Pages.Sum(p => p.WordCount);

    /// <summary>Every word in the document, in reading order, ignoring page boundaries.</summary>
    public IReadOnlyList<string> Words =>
        _words ??= Pages.SelectMany(p => p.Words).ToArray();

    private IReadOnlyList<string>? _words;
}
