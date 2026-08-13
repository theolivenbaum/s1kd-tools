using System.Text;

namespace S1kdTools.Pdf;

/// <summary>
/// Renders a <see cref="PdfComparison"/> as Markdown.
///
/// <para>
/// The report is written to be read by an agent reverse-engineering a stylesheet, and its
/// shape follows how that job actually goes: a score to track, document-wide metrics that
/// never hide behind an average, then <i>one</i> page taken apart in full. Detailing every
/// divergent page would be worse, not better — differences cascade, so pages two onward
/// usually restate the first page's bug in a hundred new places and bury it.
/// </para>
/// </summary>
public static class MarkdownReport
{
    public static string Write(PdfComparison c, PdfCompareOptions options)
    {
        var sb = new StringBuilder();

        Header(sb, c);
        Score(sb, c);
        DocumentMetrics(sb, c);
        PageMetrics(sb, c);
        StyleFindings(sb, c);

        foreach (PageComparison page in c.Pages.Where(p => p.Detailed))
        {
            DetailPage(sb, c, page, options);
        }

        if (c.Pages.Any(p => p.HasDifference && !p.Detailed))
        {
            int rest = c.Pages.Count(p => p.HasDifference && !p.Detailed);
            sb.AppendLine($"> {rest} further page(s) differ. They are counted in every metric above but "
                          + "not taken apart here: differences cascade, and the page-1-of-them teardown is "
                          + "almost always the cause of the rest. Pass `--all-pages` once the page above is clean.");
            sb.AppendLine();
        }

        Recommendations(sb, c);
        return sb.ToString();
    }

    private static void Header(StringBuilder sb, PdfComparison c)
    {
        sb.AppendLine("# PDF comparison report");
        sb.AppendLine();
        sb.AppendLine($"- **this rendering** — `{c.Actual.Path}` — {c.Actual.PageCount} page(s), {c.Actual.WordCount} words");
        sb.AppendLine($"- **reference** — `{c.Reference.Path}` — {c.Reference.PageCount} page(s), {c.Reference.WordCount} words");
        sb.AppendLine();
        if (c.Identical)
        {
            sb.AppendLine("**The two renderings agree on every page.**");
            sb.AppendLine();
        }
    }

    private static void Score(StringBuilder sb, PdfComparison c)
    {
        sb.AppendLine($"## Parity score — {c.ParityScore:F1} / 100");
        sb.AppendLine();
        sb.AppendLine("| component | weight | agreement | points |");
        sb.AppendLine("|---|---:|---:|---:|");
        Row("page count", 20, c.PageCountAgreement);
        Row("text (document-wide, pagination-blind)", 30, c.TextAgreement);
        Row("text (per page)", 10, c.PageTextAgreement);
        Row("ink quantity", 10, c.InkAmountAgreement);
        Row("ink placement (IoU)", 30, c.InkPlacementAgreement);
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(c.ProgressLine);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Track the components, not just the total. They fail in a fixed order: text agreement "
                      + "has to reach 1.0 before page count can, and page count before ink placement means "
                      + "anything — a page compared against the wrong page scores nonsense.");
        sb.AppendLine();

        void Row(string name, double weight, double agreement) =>
            sb.AppendLine($"| {name} | {weight:F0} | {agreement:P1} | {weight * agreement:F1} |");
    }

    private static void DocumentMetrics(StringBuilder sb, PdfComparison c)
    {
        var actualPages = c.Pages.Where(p => p.InActual).ToList();
        var referencePages = c.Pages.Where(p => p.InReference).ToList();

        sb.AppendLine("## Document metrics");
        sb.AppendLine();
        sb.AppendLine("| metric | this rendering | reference | delta |");
        sb.AppendLine("|---|---:|---:|---:|");
        Row("pages", c.Actual.PageCount, c.Reference.PageCount, "F0");
        Row("words", c.Actual.WordCount, c.Reference.WordCount, "F0");
        Row("words per page (mean)",
            actualPages.Count == 0 ? 0 : (double)c.Actual.WordCount / actualPages.Count,
            referencePages.Count == 0 ? 0 : (double)c.Reference.WordCount / referencePages.Count, "F1");
        Row("text lines",
            c.Actual.Pages.Sum(p => p.Lines.Count), c.Reference.Pages.Sum(p => p.Lines.Count), "F0");

        var inked = c.Pages.Where(p => p.Ink is not null).Select(p => p.Ink!).ToList();
        if (inked.Count > 0)
        {
            Row("ink coverage per page (mean %)",
                inked.Average(i => i.ActualInkCoverage) * 100,
                inked.Average(i => i.ReferenceInkCoverage) * 100, "F2");
            sb.AppendLine($"| differing pixels per page (mean %) | {inked.Average(i => i.DifferingFraction) * 100:F2} | — | — |");
            sb.AppendLine($"| ink placement IoU (mean) | {inked.Average(i => i.InkIoU):F3} | 1.000 | {inked.Average(i => i.InkIoU) - 1:+0.000;-0.000} |");
            sb.AppendLine($"| clustered difference regions | {inked.Sum(i => i.TotalRegions)} | 0 | — |");
        }

        Row("paper", c.ActualStyle.PaperName, c.ReferenceStyle.PaperName);
        Row("body style", c.ActualStyle.Body?.Key ?? "(no text)", c.ReferenceStyle.Body?.Key ?? "(no text)");
        Row("margins L/R/T/B (pt)",
            $"{c.ActualStyle.MarginLeft:F1}/{c.ActualStyle.MarginRight:F1}/{c.ActualStyle.MarginTop:F1}/{c.ActualStyle.MarginBottom:F1}",
            $"{c.ReferenceStyle.MarginLeft:F1}/{c.ReferenceStyle.MarginRight:F1}/{c.ReferenceStyle.MarginTop:F1}/{c.ReferenceStyle.MarginBottom:F1}");
        Row("leading (pt)", $"{c.ActualStyle.Leading:F1}", $"{c.ReferenceStyle.Leading:F1}");
        sb.AppendLine();
        sb.AppendLine($"First page that differs: **{(c.FirstDivergentPage?.ToString() ?? "none")}**.");
        sb.AppendLine();

        void Row(string name, object actual, object reference, string? format = null)
        {
            if (format is null)
            {
                sb.AppendLine($"| {name} | {actual} | {reference} | {(Equals(actual, reference) ? "—" : "differs")} |");
                return;
            }
            double a = Convert.ToDouble(actual), r = Convert.ToDouble(reference);
            // The sign is prepended rather than expressed as a positive/negative format
            // section: in a sectioned custom format the "F" of "F1" is a literal, not a
            // standard specifier, and the delta prints as "-F43".
            string delta = Math.Abs(a - r) < 1e-9
                ? "—"
                : (a > r ? "+" : "") + (a - r).ToString(format);
            sb.AppendLine($"| {name} | {a.ToString(format)} | {r.ToString(format)} | {delta} |");
        }
    }

    private static void PageMetrics(StringBuilder sb, PdfComparison c)
    {
        sb.AppendLine("## Per-page metrics");
        sb.AppendLine();
        sb.AppendLine("`ink%` is the share of the page carrying ink; `IoU` is how much of the combined ink "
                      + "lands in the same place on both sides; `diff%` is the share of pixels that differ; "
                      + "`shift` is the best-fit vertical displacement of the whole page.");
        sb.AppendLine();
        sb.AppendLine("| page | words | ref words | text | ink% | ref ink% | ink ratio | IoU | diff% | shift | regions | verdict |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (PageComparison p in c.Pages)
        {
            if (p.Ink is null || p.Structure is null)
            {
                sb.AppendLine($"| {p.Number} | {p.ActualWords} | {p.ReferenceWords} | — | — | — | — | — | — | — | — | "
                              + $"{Short(p.Verdict)} |");
                continue;
            }
            sb.AppendLine(
                $"| {p.Number} | {p.ActualWords} | {p.ReferenceWords} | {p.Structure.TextSimilarity:P0} | "
                + $"{p.Ink.ActualInkCoverage * 100:F2} | {p.Ink.ReferenceInkCoverage * 100:F2} | "
                + $"{Ratio(p.Ink.InkRatio)} | {p.Ink.InkIoU:F3} | {p.Ink.DifferingFraction * 100:F2} | "
                + $"{p.Structure.PageShiftPt:+0.0;-0.0;0.0}pt | {p.Ink.Regions.Count} | {Short(p.Verdict)} |");
        }
        sb.AppendLine();

        static string Ratio(double r) => double.IsInfinity(r) ? "∞" : r.ToString("F3");
    }

    private static void StyleFindings(StringBuilder sb, PdfComparison c)
    {
        sb.AppendLine("## Style differences");
        sb.AppendLine();
        if (c.StyleFindings.Count == 0)
        {
            sb.AppendLine("None: paper, margins, fonts, leading, running heads and graphic counts all agree.");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("Measured off the ink of both documents, stated as the property a stylesheet sets. "
                      + "These are document-wide, so one fix here usually removes many page findings.");
        sb.AppendLine();
        sb.AppendLine("| | property | this rendering | reference | delta | set in |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (StyleFinding f in c.StyleFindings)
        {
            string mark = f.Severity switch
            {
                StyleSeverity.Structural => "‼",
                StyleSeverity.Significant => "•",
                _ => "·",
            };
            sb.AppendLine($"| {mark} | `{f.Property}` | {Cell(f.Actual)} | {Cell(f.Reference)} | {Cell(f.Delta)} | {Cell(f.FoHint)} |");
        }
        sb.AppendLine();
        sb.AppendLine("‼ structural · • significant · · minor");
        sb.AppendLine();
    }

    // ------------------------------------------------------------------- the detailed page

    private static void DetailPage(StringBuilder sb, PdfComparison c, PageComparison page, PdfCompareOptions options)
    {
        sb.AppendLine($"## Page {page.Number} — first divergence, in detail");
        sb.AppendLine();
        sb.AppendLine($"**{page.Verdict}**");
        sb.AppendLine();

        if (page.Ink is null || page.Structure is null)
        {
            sb.AppendLine("Only one of the two documents has this page, so there is nothing to lay side by side.");
            sb.AppendLine();
            return;
        }

        PdfPageModel actual = c.Actual.Pages[page.Number - 1];
        PdfPageModel reference = c.Reference.Pages[page.Number - 1];

        sb.AppendLine($"- page box — this rendering {page.Ink.ActualWidth:F1}x{page.Ink.ActualHeight:F1}pt, "
                      + $"reference {page.Ink.ReferenceWidth:F1}x{page.Ink.ReferenceHeight:F1}pt");
        sb.AppendLine($"- whole-page vertical shift — {page.Structure.PageShiftPt:+0.0;-0.0;0.0}pt "
                      + $"(horizontal {page.Structure.PageShiftXPt:+0.0;-0.0;0.0}pt)");
        sb.AppendLine($"- lines — {page.Structure.Same} unchanged, {page.Structure.Moved} moved, "
                      + $"{page.Structure.Restyled} restyled, {page.Structure.Retexted} retexted, "
                      + $"{page.Structure.Missing} missing, {page.Structure.Extra} extra");
        if (options.ImageDirectory is { } dir)
        {
            sb.AppendLine($"- images — `{Path.Combine(dir, $"page-{page.Number:D3}-diff.png")}` "
                          + "(reference faded, differing regions boxed in red), plus `-actual.png` and `-reference.png`");
        }
        sb.AppendLine();

        InkRegions(sb, page);
        LineTable(sb, page, options);
        Outline(sb, "the reference", reference);
        Outline(sb, "this rendering", actual);
    }

    private static void InkRegions(StringBuilder sb, PageComparison page)
    {
        sb.AppendLine("### Where the ink differs");
        sb.AppendLine();
        var regions = page.Ink!.Regions;
        if (regions.Count == 0)
        {
            sb.AppendLine("No clustered ink difference: whatever differs on this page is structural rather than visual.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("Differing pixels, dilated and grouped into connected regions. Regions where one "
                      + "side has ink the other does not come first, then the largest. Boxes are in "
                      + "points from the top-left of the **reference** page.");
        sb.AppendLine();
        if (page.Ink.RegionsShiftCompensated)
        {
            sb.AppendLine($"*The whole page is displaced by {page.Ink.VerticalShiftPt:+0.0;-0.0}pt, and that "
                          + "displacement was compensated for before clustering — otherwise every line on "
                          + "the page would contribute a sliver region saying the same thing. What follows "
                          + "is what differs **beyond** the shift. The metrics above are measured unaligned, "
                          + "so the shift itself still counts against them.*");
            sb.AppendLine();
        }
        if (page.Ink.TotalRegions > regions.Count)
        {
            sb.AppendLine($"*{page.Ink.TotalRegions} regions were found; the {regions.Count} listed here are "
                          + "the ones that carry the most, and the rest are smaller. This is a cap, not the "
                          + "whole picture.*");
            sb.AppendLine();
        }
        sb.AppendLine("| # | where | box (x, y, w×h pt) | % page | ink here (ours → ref) | reading |");
        sb.AppendLine("|---:|---|---|---:|---|---|");
        int i = 1;
        foreach (InkRegion r in regions)
        {
            sb.AppendLine($"| {i++} | {r.Where} | {r.Bounds.X:F1}, {r.Bounds.Y:F1}, {r.Bounds.Width:F1}×{r.Bounds.Height:F1} | "
                          + $"{r.PageFraction * 100:F2} | {r.ActualInk:F3} → {r.ReferenceInk:F3} | {r.Summary} |");
        }
        sb.AppendLine();

        i = 1;
        foreach (InkRegion r in regions.Take(8))
        {
            if (r.ReferenceText.Count == 0 && r.ActualText.Count == 0)
            {
                i++;
                continue;
            }
            sb.AppendLine($"Region {i++} ({r.Where}) contains:");
            sb.AppendLine();
            sb.AppendLine($"- reference: {Sample(r.ReferenceText)}");
            sb.AppendLine($"- ours: {Sample(r.ActualText)}");
            sb.AppendLine();
        }

        static string Sample(IReadOnlyList<string> lines) =>
            lines.Count == 0 ? "*(nothing)*" : string.Join(" / ", lines.Select(t => $"`{Trim(t, 70)}`"));
    }

    private static void LineTable(StringBuilder sb, PageComparison page, PdfCompareOptions options)
    {
        var entries = page.Structure!.Entries.Where(e => e.Change != LineChange.Same).ToList();
        sb.AppendLine("### What changed, line by line");
        sb.AppendLine();
        if (entries.Count == 0)
        {
            sb.AppendLine("Every text line matched in position and style.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("`Δx`/`Δy` are ours minus the reference, in points. `resid` is `Δy` with the whole-page "
                      + "shift removed — the line where `resid` first jumps is where the cascade started, and "
                      + "everything below it is a consequence.");
        sb.AppendLine();
        sb.AppendLine("| change | ref y | ref x | Δx | Δy | resid | style change | text |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---|---|");

        foreach (LineDiffEntry e in entries.Take(options.MaxLineEntries))
        {
            TextLine? anchor = e.Reference ?? e.Actual;
            string dx = e.Change is LineChange.Missing or LineChange.Extra ? "—" : $"{e.DeltaX:+0.0;-0.0;0.0}";
            string dy = e.Change is LineChange.Missing or LineChange.Extra ? "—" : $"{e.DeltaY:+0.0;-0.0;0.0}";
            string resid = e.Change is LineChange.Missing or LineChange.Extra ? "—" : $"{e.ResidualY:+0.0;-0.0;0.0}";
            string style = e.StyleChanges.Count == 0 ? "" : string.Join("; ", e.StyleChanges);
            string text = e.Change == LineChange.Retexted
                ? $"`{Trim(e.Reference?.Text ?? "", 45)}` → `{Trim(e.Actual?.Text ?? "", 45)}`"
                : $"`{Trim(e.Text, 60)}`";

            sb.AppendLine($"| {Label(e.Change)} | {anchor?.Baseline:F1} | {anchor?.Bounds.Left:F1} | {dx} | {dy} | "
                          + $"{resid} | {Cell(style)} | {text} |");
        }
        if (entries.Count > options.MaxLineEntries)
        {
            sb.AppendLine();
            sb.AppendLine($"*({entries.Count - options.MaxLineEntries} further changed line(s) not listed; "
                          + "raise `--max-lines` to see them.)*");
        }
        sb.AppendLine();

        var missing = entries.Where(e => e.Change == LineChange.Missing).ToList();
        if (missing.Count > 0)
        {
            sb.AppendLine("Lines the reference draws and this rendering never emits, with the style they are set in:");
            sb.AppendLine();
            foreach (LineDiffEntry e in missing.Take(20))
            {
                TextLine r = e.Reference!;
                sb.AppendLine($"- `{Trim(r.Text, 80)}` — {r.StyleKey}, at x={r.Bounds.Left:F1} y={r.Baseline:F1}");
            }
            sb.AppendLine();
        }

        static string Label(LineChange change) => change switch
        {
            LineChange.Missing => "**missing**",
            LineChange.Extra => "**extra**",
            LineChange.Retexted => "retexted",
            LineChange.Restyled => "restyled",
            LineChange.Moved => "moved",
            _ => "same",
        };
    }

    /// <summary>
    /// The page as a list of marks, in reading order. This is the raw material for
    /// reconstructing a stylesheet: every line with the position, font, size and weight it
    /// was set in, plus the rules and fills around it.
    /// </summary>
    private static void Outline(StringBuilder sb, string which, PdfPageModel page)
    {
        sb.AppendLine($"### Structure of {which}");
        sb.AppendLine();
        sb.AppendLine($"{page.Width:F1}×{page.Height:F1}pt, {page.Lines.Count} line(s), "
                      + $"{page.Graphics.Count} graphic mark(s). Positions are points from the top-left; "
                      + "`y` is the baseline for text and the top edge for graphics.");
        sb.AppendLine();
        sb.AppendLine("```");

        var marks = page.Lines
            .Select(l => (Y: l.Baseline, X: l.Bounds.Left, Text: FormatLine(l)))
            .Concat(page.Graphics.Select(g => (Y: g.Bounds.Top, X: g.Bounds.Left, Text: FormatGraphic(g))))
            .OrderBy(m => m.Y)
            .ThenBy(m => m.X);

        foreach (var m in marks)
        {
            sb.AppendLine(m.Text);
        }
        sb.AppendLine("```");
        sb.AppendLine();

        static string FormatLine(TextLine l) =>
            $"y={l.Baseline,7:F1} x={l.Bounds.Left,6:F1} w={l.Bounds.Width,6:F1}  "
            + $"{l.FontSize,5:F1}pt {l.FontName}{(l.Bold ? " bold" : "")}{(l.Italic ? " italic" : "")}"
            + $"{(l.Color == "#000000" ? "" : " " + l.Color)}  \"{Trim(l.Text, 70)}\"";

        static string FormatGraphic(GraphicMark g) =>
            $"y={g.Bounds.Top,7:F1} x={g.Bounds.Left,6:F1} w={g.Bounds.Width,6:F1}  "
            + $"{g.Kind.ToString().ToLowerInvariant()} h={g.Bounds.Height:F1}pt {g.Color}";
    }

    // ------------------------------------------------------------------------ what to do

    private static void Recommendations(StringBuilder sb, PdfComparison c)
    {
        var actions = new List<string>();

        foreach (StyleFinding f in c.StyleFindings.Where(f => f.Severity == StyleSeverity.Structural))
        {
            actions.Add($"Set `{f.Property}` to match the reference ({f.Reference}, currently {f.Actual}) — {f.FoHint}.");
        }

        if (c.Actual.PageCount != c.Reference.PageCount)
        {
            actions.Add($"Pagination differs ({c.Actual.PageCount} against {c.Reference.PageCount}). "
                        + "Page-by-page comparisons below the first divergence compare unrelated pages until "
                        + "this is resolved, so fix the geometry and leading findings before reading them.");
        }

        PageComparison? detailed = c.Pages.FirstOrDefault(p => p.Detailed);
        if (detailed?.Structure is { } structure)
        {
            var missing = structure.Entries.Where(e => e.Change == LineChange.Missing).ToList();
            if (missing.Count > 0)
            {
                actions.Add($"Emit the {missing.Count} missing line(s) on page {detailed.Number}, starting with "
                            + $"\"{Trim(missing[0].Reference!.Text, 60)}\" ({missing[0].Reference!.StyleKey}).");
            }
            if (structure.Rewrapped)
            {
                actions.Add("The words agree but the line breaks do not: check the region width and the "
                            + "start/end indents before chasing anything else on the page.");
            }
            if (Math.Abs(structure.PageShiftPt) > 1.0)
            {
                actions.Add($"The whole page sits {structure.PageShiftPt:+0.0;-0.0}pt from where the reference "
                            + "puts it. Find the first line whose `resid` is non-zero — that is where the "
                            + "displacement is introduced.");
            }
        }

        foreach (var group in (detailed?.Ink?.Regions ?? Array.Empty<InkRegion>())
                     .Where(r => r.Kind is InkRegionKind.MissingRule or InkRegionKind.MissingFill)
                     .GroupBy(r => r.Kind))
        {
            string what = group.Key == InkRegionKind.MissingRule ? "rule/border" : "shaded area";
            string where = string.Join(", ", group.Take(3).Select(r => $"{r.Where} at y={r.Bounds.Y:F0}pt"));
            actions.Add($"The reference draws {group.Count()} {what}(s) this rendering does not: {where}. "
                        + "Look for `@border-*`, `fo:leader` or `@background-color` in the reference's styling.");
        }

        if (c.ReferenceStyle.RunningHeader.Count > 0 && c.ActualStyle.RunningHeader.Count == 0)
        {
            actions.Add("The reference has a running header this rendering has no equivalent of: "
                        + $"\"{string.Join(" / ", c.ReferenceStyle.RunningHeader)}\". That needs an "
                        + "`fo:static-content` for `xsl-region-before` and an `@extent` on the region.");
        }
        if (c.ReferenceStyle.RunningFooter.Count > 0 && c.ActualStyle.RunningFooter.Count == 0)
        {
            actions.Add("The reference has a running footer this rendering has no equivalent of: "
                        + $"\"{string.Join(" / ", c.ReferenceStyle.RunningFooter)}\". That needs an "
                        + "`fo:static-content` for `xsl-region-after`.");
        }

        sb.AppendLine("## What to change next");
        sb.AppendLine();
        if (actions.Count == 0)
        {
            sb.AppendLine(c.Identical
                ? "Nothing: the two renderings agree."
                : "No structural finding stands out. Work through the line table above.");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("In the order they should be tackled — each one can change everything below it, so "
                      + "re-run the comparison after every change rather than batching them.");
        sb.AppendLine();
        int i = 1;
        foreach (string action in actions.Take(12))
        {
            sb.AppendLine($"{i++}. {action}");
        }
        sb.AppendLine();
    }

    // ----------------------------------------------------------------------------- helpers

    /// <summary>Pipes and newlines would break the table they sit in.</summary>
    private static string Cell(string? value) =>
        string.IsNullOrEmpty(value) ? "—" : value.Replace("|", "\\|").Replace("\n", " ");

    private static string Trim(string text, int max)
    {
        text = text.Replace("|", "\\|").Replace("`", "'");
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    private static string Short(string verdict)
    {
        int stop = verdict.IndexOf('—');
        string head = stop > 0 ? verdict[..stop].Trim() : verdict;
        return head.Length > 40 ? head[..39] + "…" : head;
    }
}
