namespace S1kdTools.Pdf;

/// <summary>How much a style finding is likely to matter.</summary>
public enum StyleSeverity
{
    /// <summary>Everything downstream depends on it: paper size, margins, body font.</summary>
    Structural,

    /// <summary>Visible and specific: a missing rule, a heading set at the wrong size.</summary>
    Significant,

    /// <summary>Worth knowing, unlikely to be the thing you are chasing.</summary>
    Minor,
}

/// <summary>
/// One measured difference between two documents' styling, stated as the property a
/// stylesheet sets rather than as a pixel observation.
/// </summary>
public sealed class StyleFinding
{
    /// <summary>What was measured, e.g. <c>page.margin-left</c> or <c>body.font-size</c>.</summary>
    public required string Property { get; init; }

    public required string Actual { get; init; }

    public required string Reference { get; init; }

    /// <summary>Ours minus reference, phrased for the property. Empty when not numeric.</summary>
    public string Delta { get; init; } = "";

    /// <summary>Where in an XSL-FO stylesheet this property is set.</summary>
    public required string FoHint { get; init; }

    public required StyleSeverity Severity { get; init; }

    public override string ToString() => $"{Property}: {Actual} vs {Reference}";
}

/// <summary>
/// Compares two documents' measured styling and reports the properties that differ.
///
/// <para>
/// This is the layer that makes the report actionable. A region diff can say "the top of
/// every page is wrong"; this says "your top margin is 56.7pt and the reference's is
/// 28.3pt, set <c>margin-top</c> on the <c>simple-page-master</c>" — which is a change
/// someone can make, and which usually removes a hundred region findings at once.
/// </para>
/// </summary>
public static class StyleDelta
{
    /// <summary>Margins differing by less than this are the same margin.</summary>
    private const double MarginTolerancePt = 1.0;

    public static IReadOnlyList<StyleFinding> Compare(
        DocumentStyleFacts actual, DocumentStyleFacts reference)
    {
        var findings = new List<StyleFinding>();

        if (actual.PaperName != reference.PaperName)
        {
            findings.Add(new StyleFinding
            {
                Property = "page.size",
                Actual = $"{actual.PaperName} ({actual.Width:F1}x{actual.Height:F1}pt)",
                Reference = $"{reference.PaperName} ({reference.Width:F1}x{reference.Height:F1}pt)",
                FoHint = "fo:simple-page-master/@page-width, @page-height",
                Severity = StyleSeverity.Structural,
            });
        }

        AddLength(findings, "page.margin-left", actual.MarginLeft, reference.MarginLeft,
            "fo:simple-page-master/@margin-left", StyleSeverity.Structural);
        AddLength(findings, "page.margin-right", actual.MarginRight, reference.MarginRight,
            "fo:simple-page-master/@margin-right", StyleSeverity.Structural);
        AddLength(findings, "page.margin-top", actual.MarginTop, reference.MarginTop,
            "fo:simple-page-master/@margin-top (or region-before/@extent)", StyleSeverity.Structural);
        AddLength(findings, "page.margin-bottom", actual.MarginBottom, reference.MarginBottom,
            "fo:simple-page-master/@margin-bottom (or region-after/@extent)", StyleSeverity.Structural);

        if (actual.Body is { } ab && reference.Body is { } rb)
        {
            if (!string.Equals(ab.Font, rb.Font, StringComparison.Ordinal))
            {
                findings.Add(new StyleFinding
                {
                    Property = "body.font-family",
                    Actual = ab.Font,
                    Reference = rb.Font,
                    FoHint = "fo:flow or the root fo:block/@font-family",
                    Severity = StyleSeverity.Structural,
                });
            }
            AddLength(findings, "body.font-size", ab.Size, rb.Size,
                "fo:block/@font-size", StyleSeverity.Structural, tolerance: 0.05, showMillimetres: false);
        }
        else if (actual.Body is null != (reference.Body is null))
        {
            findings.Add(new StyleFinding
            {
                Property = "body.text",
                Actual = actual.Body is null ? "no text at all" : actual.Body.Key,
                Reference = reference.Body is null ? "no text at all" : reference.Body.Key,
                FoHint = "fo:flow content",
                Severity = StyleSeverity.Structural,
            });
        }

        AddLength(findings, "body.line-height", actual.Leading, reference.Leading,
            "fo:block/@line-height", StyleSeverity.Significant, tolerance: 0.2, showMillimetres: false);

        if (actual.LineHeightRatio > 0 && reference.LineHeightRatio > 0
            && Math.Abs(actual.LineHeightRatio - reference.LineHeightRatio) > 0.03)
        {
            findings.Add(new StyleFinding
            {
                Property = "body.line-height (relative)",
                Actual = $"{actual.LineHeightRatio:F2}× font size",
                Reference = $"{reference.LineHeightRatio:F2}× font size",
                Delta = $"{actual.LineHeightRatio - reference.LineHeightRatio:+0.00;-0.00}×",
                FoHint = "fo:block/@line-height as a multiplier",
                Severity = StyleSeverity.Significant,
            });
        }

        AddFontRoles(findings, actual, reference);
        AddRunningText(findings, "page.running-header", actual.RunningHeader, reference.RunningHeader,
            "fo:static-content flow-name=\"xsl-region-before\"");
        AddRunningText(findings, "page.running-footer", actual.RunningFooter, reference.RunningFooter,
            "fo:static-content flow-name=\"xsl-region-after\"");

        AddCount(findings, "graphics.rules", actual.RuleCount, reference.RuleCount,
            "fo:block/@border-*, fo:leader, or table borders");
        AddCount(findings, "graphics.fills", actual.FillCount, reference.FillCount,
            "@background-color on fo:block / fo:table-cell");
        AddCount(findings, "graphics.images", actual.ImageCount, reference.ImageCount,
            "fo:external-graphic");

        AddIndents(findings, actual, reference);

        return findings
            .OrderBy(f => (int)f.Severity)
            .ThenBy(f => f.Property, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddLength(
        List<StyleFinding> findings, string property, double actual, double reference,
        string hint, StyleSeverity severity,
        double tolerance = MarginTolerancePt, bool showMillimetres = true)
    {
        double delta = actual - reference;
        if (Math.Abs(delta) <= tolerance)
        {
            return;
        }
        string mm = showMillimetres
            ? $" ({StyleAnalyser.ToMillimetres(delta):+0.0;-0.0}mm)"
            : "";
        findings.Add(new StyleFinding
        {
            Property = property,
            Actual = $"{actual:F1}pt" + (showMillimetres ? $" ({StyleAnalyser.ToMillimetres(actual):F1}mm)" : ""),
            Reference = $"{reference:F1}pt" + (showMillimetres ? $" ({StyleAnalyser.ToMillimetres(reference):F1}mm)" : ""),
            Delta = $"{delta:+0.0;-0.0}pt{mm}",
            FoHint = hint,
            Severity = severity,
        });
    }

    private static void AddCount(
        List<StyleFinding> findings, string property, int actual, int reference, string hint)
    {
        if (actual == reference)
        {
            return;
        }
        findings.Add(new StyleFinding
        {
            Property = property,
            Actual = actual.ToString(),
            Reference = reference.ToString(),
            Delta = $"{actual - reference:+0;-0}",
            FoHint = hint,
            Severity = StyleSeverity.Significant,
        });
    }

    /// <summary>
    /// Text styles are matched by <i>role</i> — largest, second largest and so on — rather
    /// than by name, because that is how a stylesheet is organised: there is a body size,
    /// a heading size and a caption size, and it is the sizes that need to line up even
    /// when the two toolchains never had the same fonts installed.
    /// </summary>
    private static void AddFontRoles(
        List<StyleFinding> findings, DocumentStyleFacts actual, DocumentStyleFacts reference)
    {
        var actualRoles = Roles(actual);
        var referenceRoles = Roles(reference);

        for (int i = 0; i < Math.Max(actualRoles.Count, referenceRoles.Count); i++)
        {
            FontUsage? a = i < actualRoles.Count ? actualRoles[i] : null;
            FontUsage? r = i < referenceRoles.Count ? referenceRoles[i] : null;
            string role = i == 0 ? "text.largest" : $"text.size-rank-{i + 1}";

            if (a is null || r is null)
            {
                findings.Add(new StyleFinding
                {
                    Property = role,
                    Actual = a?.Key ?? "(absent)",
                    Reference = r?.Key ?? "(absent)",
                    FoHint = a is null
                        ? "a text style the reference uses and this rendering never produces"
                        : "a text style this rendering produces and the reference does not",
                    Severity = StyleSeverity.Significant,
                });
                continue;
            }

            if (Math.Abs(a.Size - r.Size) > 0.05 || a.Bold != r.Bold || a.Color != r.Color)
            {
                findings.Add(new StyleFinding
                {
                    Property = role,
                    Actual = $"{a.Key} — e.g. \"{a.Sample}\"",
                    Reference = $"{r.Key} — e.g. \"{r.Sample}\"",
                    Delta = Math.Abs(a.Size - r.Size) > 0.05 ? $"{a.Size - r.Size:+0.0;-0.0}pt" : "",
                    FoHint = "fo:block/@font-size, @font-weight, @color for this role",
                    Severity = StyleSeverity.Significant,
                });
            }
        }
    }

    /// <summary>Distinct text sizes, largest first, ignoring styles used for only a few glyphs.</summary>
    private static IReadOnlyList<FontUsage> Roles(DocumentStyleFacts facts)
    {
        int total = Math.Max(1, facts.Fonts.Sum(f => f.Glyphs));
        return facts.Fonts
            .Where(f => f.Glyphs >= total * 0.005)
            .GroupBy(f => Math.Round(f.Size, 1))
            .Select(g => g.OrderByDescending(f => f.Glyphs).First())
            .OrderByDescending(f => f.Size)
            .Take(6)
            .ToArray();
    }

    private static void AddRunningText(
        List<StyleFinding> findings, string property,
        IReadOnlyList<string> actual, IReadOnlyList<string> reference, string hint)
    {
        string a = actual.Count == 0 ? "(none)" : string.Join(" ⏐ ", actual);
        string r = reference.Count == 0 ? "(none)" : string.Join(" ⏐ ", reference);
        if (string.Equals(a, r, StringComparison.Ordinal))
        {
            return;
        }
        findings.Add(new StyleFinding
        {
            Property = property,
            Actual = a,
            Reference = r,
            FoHint = hint,
            Severity = reference.Count > 0 && actual.Count == 0
                ? StyleSeverity.Structural
                : StyleSeverity.Significant,
        });
    }

    private static void AddIndents(
        List<StyleFinding> findings, DocumentStyleFacts actual, DocumentStyleFacts reference)
    {
        // Only stops the reference actually relies on are worth chasing; a stop used by
        // two lines in a three-page document is noise.
        var a = actual.IndentStops.Where(s => s.Lines >= 3).Select(s => Math.Round(s.X)).ToHashSet();
        var r = reference.IndentStops.Where(s => s.Lines >= 3).Select(s => Math.Round(s.X)).ToHashSet();
        var missing = r.Except(a).OrderBy(x => x).ToArray();
        var extra = a.Except(r).OrderBy(x => x).ToArray();
        if (missing.Length == 0 && extra.Length == 0)
        {
            return;
        }
        findings.Add(new StyleFinding
        {
            Property = "text.indent-stops",
            Actual = a.Count == 0 ? "(none)" : string.Join(", ", a.OrderBy(x => x).Select(x => $"{x:F0}pt")),
            Reference = r.Count == 0 ? "(none)" : string.Join(", ", r.OrderBy(x => x).Select(x => $"{x:F0}pt")),
            Delta = (missing.Length > 0 ? $"absent here: {string.Join(", ", missing.Select(x => $"{x:F0}pt"))}" : "")
                    + (missing.Length > 0 && extra.Length > 0 ? "; " : "")
                    + (extra.Length > 0 ? $"only here: {string.Join(", ", extra.Select(x => $"{x:F0}pt"))}" : ""),
            FoHint = "fo:block/@start-indent, @text-indent, or list-block label separation",
            Severity = StyleSeverity.Minor,
        });
    }
}
