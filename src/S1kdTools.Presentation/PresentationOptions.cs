using System.Globalization;

namespace S1kdTools.Presentation;

/// <summary>A page size, in millimetres.</summary>
/// <param name="WidthMm">Page width in millimetres.</param>
/// <param name="HeightMm">Page height in millimetres.</param>
public readonly record struct PageSize(double WidthMm, double HeightMm)
{
    /// <summary>ISO A4 portrait, 210 × 297 mm — the S1000D page-oriented default.</summary>
    public static PageSize A4 => new(210, 297);

    /// <summary>ISO A5 portrait, 148 × 210 mm.</summary>
    public static PageSize A5 => new(148, 210);

    /// <summary>US Letter portrait, 215.9 × 279.4 mm.</summary>
    public static PageSize Letter => new(215.9, 279.4);

    /// <summary>This page size turned on its side.</summary>
    public PageSize Landscape() => new(HeightMm, WidthMm);
}

/// <summary>Page margins, in millimetres.</summary>
/// <param name="TopMm">Top margin.</param>
/// <param name="BottomMm">Bottom margin.</param>
/// <param name="InnerMm">Binding-edge (start) margin.</param>
/// <param name="OuterMm">Outer (end) margin.</param>
public readonly record struct PageMargins(double TopMm, double BottomMm, double InnerMm, double OuterMm)
{
    /// <summary>The default margins: 12 mm top and bottom, 20 mm inner, 15 mm outer.</summary>
    public static PageMargins Default => new(12, 12, 20, 15);
}

/// <summary>
/// How a CSDB object is laid out on the page. Every value here is handed to the
/// presentation stylesheet as an XSLT parameter, so the same knobs work whether
/// you render through <see cref="S1000DPresentation"/> or run one of the
/// stylesheets yourself through <c>s1kd render -s</c>.
/// </summary>
public sealed record PresentationOptions
{
    /// <summary>The options used when a caller passes none.</summary>
    public static PresentationOptions Default { get; } = new();

    /// <summary>
    /// Name printed at the top left of every page. When null, the responsible
    /// partner company of the object is used, falling back to its originator.
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Publication title printed at the top right of every page (e.g.
    /// "AIRCRAFT MAINTENANCE MANUAL"). When null, the default for the object
    /// type is used — see <see cref="CsdbObjectTypeInfo.PublicationTitle"/>.
    /// </summary>
    public string? PublicationTitle { get; init; }

    /// <summary>Page size. Defaults to <see cref="PageSize.A4"/>.</summary>
    public PageSize Page { get; init; } = PageSize.A4;

    /// <summary>Page margins. Defaults to <see cref="PageMargins.Default"/>.</summary>
    public PageMargins Margins { get; init; } = PageMargins.Default;

    /// <summary>Body text font family. Defaults to Helvetica.</summary>
    public string FontFamily { get; init; } = "Helvetica";

    /// <summary>Font family for verbatim text. Defaults to Courier.</summary>
    public string MonospaceFontFamily { get; init; } = "Courier";

    /// <summary>Body text size in points. Defaults to 9.</summary>
    public double FontSizePt { get; init; } = 9;

    /// <summary>
    /// Print the data module title block (identification, issue, applicability,
    /// security and quality assurance) ahead of the content. Defaults to true.
    /// </summary>
    public bool IncludeTitleBlock { get; init; } = true;

    /// <summary>
    /// Text drawn diagonally across every page, e.g. "DRAFT" or "NOT FOR
    /// NAVIGATION". Null (the default) draws no watermark.
    /// </summary>
    public string? Watermark { get; init; }

    /// <summary>
    /// Directories searched for the ICN files referenced by <c>graphic</c> and
    /// <c>symbol</c> elements. A referenced ICN that is not found renders as a
    /// labelled placeholder frame instead of failing the render.
    /// </summary>
    public IReadOnlyList<string> GraphicsDirectories { get; init; } = [];

    /// <summary>
    /// Directories of TTF/OTF fonts to register with the PDF renderer, for
    /// stylesheets that ask for a font the built-in base-14 set does not cover.
    /// </summary>
    public IReadOnlyList<string> FontDirectories { get; init; } = [];

    /// <summary>
    /// Use the native (PdfSharp-free) PDF renderer of FOP.Sharp. Defaults to false.
    /// </summary>
    public bool UseNativePdfRenderer { get; init; }

    /// <summary>
    /// Extra XSLT parameters, applied after the ones derived from this record —
    /// so a name given here overrides the built-in value.
    /// </summary>
    public IReadOnlyDictionary<string, string> StylesheetParameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Flatten these options into the XSLT parameter set the presentation
    /// stylesheets expect.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToStylesheetParameters(CsdbObjectTypeInfo info)
    {
        var p = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["publication-title"] = PublicationTitle ?? info.PublicationTitle,
            ["page-width"] = Num(Page.WidthMm),
            ["page-height"] = Num(Page.HeightMm),
            ["margin-top"] = Num(Margins.TopMm),
            ["margin-bottom"] = Num(Margins.BottomMm),
            ["margin-inner"] = Num(Margins.InnerMm),
            ["margin-outer"] = Num(Margins.OuterMm),
            ["font-family"] = FontFamily,
            ["mono-font-family"] = MonospaceFontFamily,
            ["font-size"] = Num(FontSizePt),
            ["title-block"] = IncludeTitleBlock ? "1" : "0",
            ["watermark"] = Watermark ?? string.Empty,
        };

        if (Publisher != null)
        {
            p["publisher"] = Publisher;
        }

        foreach (KeyValuePair<string, string> extra in StylesheetParameters)
        {
            p[extra.Key] = extra.Value;
        }

        return p;
    }

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
