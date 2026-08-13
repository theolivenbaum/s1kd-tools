using System.Text.Json;

namespace S1kdTools.Pdf;

/// <summary>
/// The same comparison as machine-readable JSON.
///
/// <para>
/// Built from explicit dictionaries rather than by serialising the model types directly:
/// the shape of this document is an interface that a build pipeline or an agent's tooling
/// will depend on, and it should only change when someone means to change it — not
/// because a property was renamed somewhere in <see cref="PdfComparison"/>.
/// </para>
/// </summary>
public static class JsonReport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Write(PdfComparison c)
    {
        var root = new Dictionary<string, object?>
        {
            ["schema"] = "s1kd-pdfdiff/1",
            ["actual"] = Document(c.Actual, c.ActualStyle),
            ["reference"] = Document(c.Reference, c.ReferenceStyle),
            ["score"] = new Dictionary<string, object?>
            {
                ["parity"] = Round(c.ParityScore, 2),
                ["pageCountAgreement"] = Round(c.PageCountAgreement, 4),
                ["textAgreement"] = Round(c.TextAgreement, 4),
                ["pageTextAgreement"] = Round(c.PageTextAgreement, 4),
                ["inkAmountAgreement"] = Round(c.InkAmountAgreement, 4),
                ["inkPlacementAgreement"] = Round(c.InkPlacementAgreement, 4),
            },
            ["firstDivergentPage"] = c.FirstDivergentPage,
            ["identical"] = c.Identical,
            ["progressLine"] = c.ProgressLine,
            ["styleFindings"] = c.StyleFindings.Select(f => new Dictionary<string, object?>
            {
                ["property"] = f.Property,
                ["actual"] = f.Actual,
                ["reference"] = f.Reference,
                ["delta"] = f.Delta,
                ["foHint"] = f.FoHint,
                ["severity"] = f.Severity.ToString().ToLowerInvariant(),
            }).ToArray(),
            ["pages"] = c.Pages.Select(p => Page(c, p)).ToArray(),
        };
        return JsonSerializer.Serialize(root, Options);
    }

    private static Dictionary<string, object?> Document(PdfDocumentModel doc, DocumentStyleFacts style) => new()
    {
        ["path"] = doc.Path,
        ["pages"] = doc.PageCount,
        ["words"] = doc.WordCount,
        ["paper"] = style.PaperName,
        ["pageWidthPt"] = Round(style.Width, 2),
        ["pageHeightPt"] = Round(style.Height, 2),
        ["marginsPt"] = new Dictionary<string, object?>
        {
            ["left"] = Round(style.MarginLeft, 2),
            ["right"] = Round(style.MarginRight, 2),
            ["top"] = Round(style.MarginTop, 2),
            ["bottom"] = Round(style.MarginBottom, 2),
        },
        ["bodyStyle"] = style.Body?.Key,
        ["leadingPt"] = Round(style.Leading, 2),
        ["lineHeightRatio"] = Round(style.LineHeightRatio, 3),
        ["fonts"] = style.Fonts.Take(12).Select(f => new Dictionary<string, object?>
        {
            ["font"] = f.Font,
            ["sizePt"] = f.Size,
            ["bold"] = f.Bold,
            ["italic"] = f.Italic,
            ["color"] = f.Color,
            ["glyphs"] = f.Glyphs,
            ["lines"] = f.Lines,
            ["sample"] = f.Sample,
        }).ToArray(),
        ["runningHeader"] = style.RunningHeader,
        ["runningFooter"] = style.RunningFooter,
        ["rules"] = style.RuleCount,
        ["fills"] = style.FillCount,
        ["images"] = style.ImageCount,
    };

    private static Dictionary<string, object?> Page(PdfComparison c, PageComparison p)
    {
        var page = new Dictionary<string, object?>
        {
            ["number"] = p.Number,
            ["inActual"] = p.InActual,
            ["inReference"] = p.InReference,
            ["actualWords"] = p.ActualWords,
            ["referenceWords"] = p.ReferenceWords,
            ["score"] = Round(p.Score, 4),
            ["hasDifference"] = p.HasDifference,
            ["detailed"] = p.Detailed,
            ["verdict"] = p.Verdict,
        };

        if (p.Ink is { } ink)
        {
            page["ink"] = new Dictionary<string, object?>
            {
                ["geometryMatches"] = ink.GeometryMatches,
                ["actualCoverage"] = Round(ink.ActualInkCoverage, 5),
                ["referenceCoverage"] = Round(ink.ReferenceInkCoverage, 5),
                ["ratio"] = double.IsInfinity(ink.InkRatio) ? null : Round(ink.InkRatio, 4),
                ["iou"] = Round(ink.InkIoU, 4),
                ["differingFraction"] = Round(ink.DifferingFraction, 5),
                ["meanAbsError"] = Round(ink.MeanAbsError, 5),
                ["verticalShiftPt"] = Round(ink.VerticalShiftPt, 2),
                ["totalRegions"] = ink.TotalRegions,
                ["regions"] = ink.Regions.Select(r => new Dictionary<string, object?>
                {
                    ["kind"] = r.Kind.ToString(),
                    ["where"] = r.Where,
                    ["boundsPt"] = new[]
                    {
                        Round(r.Bounds.X, 1), Round(r.Bounds.Y, 1),
                        Round(r.Bounds.Width, 1), Round(r.Bounds.Height, 1),
                    },
                    ["pageFraction"] = Round(r.PageFraction, 5),
                    ["actualInk"] = Round(r.ActualInk, 4),
                    ["referenceInk"] = Round(r.ReferenceInk, 4),
                    ["summary"] = r.Summary,
                    ["referenceText"] = r.ReferenceText,
                    ["actualText"] = r.ActualText,
                }).ToArray(),
            };
        }

        if (p.Structure is { } structure)
        {
            page["structure"] = new Dictionary<string, object?>
            {
                ["pageShiftPt"] = Round(structure.PageShiftPt, 2),
                ["pageShiftXPt"] = Round(structure.PageShiftXPt, 2),
                ["textSimilarity"] = Round(structure.TextSimilarity, 4),
                ["rewrapped"] = structure.Rewrapped,
                ["counts"] = new Dictionary<string, object?>
                {
                    ["same"] = structure.Same,
                    ["moved"] = structure.Moved,
                    ["restyled"] = structure.Restyled,
                    ["retexted"] = structure.Retexted,
                    ["missing"] = structure.Missing,
                    ["extra"] = structure.Extra,
                },
            };

            // Line-level entries and the page outline are only carried for the detailed
            // page(s). Emitting them for every page of a long publication would make the
            // JSON larger than the PDFs it describes.
            if (p.Detailed)
            {
                ((Dictionary<string, object?>)page["structure"]!)["changes"] = structure.Entries
                    .Where(e => e.Change != LineChange.Same)
                    .Select(e => new Dictionary<string, object?>
                    {
                        ["change"] = e.Change.ToString().ToLowerInvariant(),
                        ["text"] = e.Text,
                        ["actualText"] = e.Actual?.Text,
                        ["referenceText"] = e.Reference?.Text,
                        ["deltaXPt"] = e.Actual is null || e.Reference is null ? null : Round(e.DeltaX, 2),
                        ["deltaYPt"] = e.Actual is null || e.Reference is null ? null : Round(e.DeltaY, 2),
                        ["residualYPt"] = e.Actual is null || e.Reference is null ? null : Round(e.ResidualY, 2),
                        ["styleChanges"] = e.StyleChanges,
                        ["referenceStyle"] = e.Reference?.StyleKey,
                        ["actualStyle"] = e.Actual?.StyleKey,
                        ["referenceAt"] = e.Reference is null
                            ? null
                            : new[] { Round(e.Reference.Bounds.Left, 1), Round(e.Reference.Baseline, 1) },
                        ["actualAt"] = e.Actual is null
                            ? null
                            : new[] { Round(e.Actual.Bounds.Left, 1), Round(e.Actual.Baseline, 1) },
                    }).ToArray();

                if (p.Number <= c.Reference.PageCount)
                {
                    page["referenceOutline"] = Outline(c.Reference.Pages[p.Number - 1]);
                }
                if (p.Number <= c.Actual.PageCount)
                {
                    page["actualOutline"] = Outline(c.Actual.Pages[p.Number - 1]);
                }
            }
        }

        return page;
    }

    /// <summary>Every mark on a page, in reading order — what a stylesheet has to reproduce.</summary>
    public static Dictionary<string, object?> Outline(PdfPageModel page) => new()
    {
        ["widthPt"] = Round(page.Width, 2),
        ["heightPt"] = Round(page.Height, 2),
        ["lines"] = page.Lines.Select(l => new Dictionary<string, object?>
        {
            ["text"] = l.Text,
            ["xPt"] = Round(l.Bounds.Left, 1),
            ["baselinePt"] = Round(l.Baseline, 1),
            ["widthPt"] = Round(l.Bounds.Width, 1),
            ["font"] = l.FontName,
            ["sizePt"] = l.FontSize,
            ["bold"] = l.Bold,
            ["italic"] = l.Italic,
            ["color"] = l.Color,
        }).ToArray(),
        ["graphics"] = page.Graphics.Select(g => new Dictionary<string, object?>
        {
            ["kind"] = g.Kind.ToString().ToLowerInvariant(),
            ["boundsPt"] = new[]
            {
                Round(g.Bounds.X, 1), Round(g.Bounds.Y, 1),
                Round(g.Bounds.Width, 1), Round(g.Bounds.Height, 1),
            },
            ["color"] = g.Color,
        }).ToArray(),
    };

    private static double Round(double value, int digits) =>
        double.IsFinite(value) ? Math.Round(value, digits) : 0;
}
