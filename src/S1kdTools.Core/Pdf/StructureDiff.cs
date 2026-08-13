using System.Text;

namespace S1kdTools.Pdf;

/// <summary>What happened to one line between the reference and the document under test.</summary>
public enum LineChange
{
    /// <summary>Same text, same place, same style.</summary>
    Same,

    /// <summary>Same text and style, but not where the reference put it.</summary>
    Moved,

    /// <summary>Same text and place, set differently — font, size, weight or colour.</summary>
    Restyled,

    /// <summary>Recognisably the same line, but the words changed.</summary>
    Retexted,

    /// <summary>The reference has this line; we never drew it.</summary>
    Missing,

    /// <summary>We drew this line; the reference has nothing like it.</summary>
    Extra,
}

/// <summary>One line of the structural diff.</summary>
public sealed class LineDiffEntry
{
    public required LineChange Change { get; init; }

    public TextLine? Actual { get; init; }

    public TextLine? Reference { get; init; }

    /// <summary>Ours minus reference, in points. Positive means further right.</summary>
    public double DeltaX { get; init; }

    /// <summary>Ours minus reference, in points. Positive means further down.</summary>
    public double DeltaY { get; init; }

    /// <summary>
    /// <see cref="DeltaY"/> with the page-wide shift removed. This is the number that
    /// matters: when one bad measurement pushes a whole page down, every line has a large
    /// DeltaY and a residual of zero, and only the line where the residual jumps is the
    /// line that caused it.
    /// </summary>
    public double ResidualY { get; init; }

    /// <summary>Style properties that changed, as "font-size 12.0pt → 10.0pt".</summary>
    public required IReadOnlyList<string> StyleChanges { get; init; }

    public string Text => Reference?.Text ?? Actual?.Text ?? "";
}

/// <summary>The structural comparison of one page pair.</summary>
public sealed class PageStructureDiff
{
    public required IReadOnlyList<LineDiffEntry> Entries { get; init; }

    /// <summary>Median vertical displacement of matched lines, in points.</summary>
    public required double PageShiftPt { get; init; }

    /// <summary>Median horizontal displacement of matched lines, in points.</summary>
    public required double PageShiftXPt { get; init; }

    /// <summary>Word-sequence agreement on this page, 0-1, ignoring where the words landed.</summary>
    public required double TextSimilarity { get; init; }

    /// <summary>
    /// The words agree but the line breaks do not: the content is right and the measure,
    /// the font metrics or the available width is wrong.
    /// </summary>
    public required bool Rewrapped { get; init; }

    public int Missing => Entries.Count(e => e.Change == LineChange.Missing);

    public int Extra => Entries.Count(e => e.Change == LineChange.Extra);

    public int Moved => Entries.Count(e => e.Change == LineChange.Moved);

    public int Restyled => Entries.Count(e => e.Change == LineChange.Restyled);

    public int Retexted => Entries.Count(e => e.Change == LineChange.Retexted);

    public int Same => Entries.Count(e => e.Change == LineChange.Same);

    public bool HasDifference => Entries.Any(e => e.Change != LineChange.Same);
}

/// <summary>
/// Aligns the text lines of two renderings of the same page and says what changed.
///
/// <para>
/// The ink diff says <i>where</i> a page differs; this says <i>what</i> differs, in the
/// vocabulary a stylesheet is written in. Alignment is a longest-common-subsequence over
/// line text, then a fuzzy second pass for lines that were edited rather than removed —
/// so a line whose date format changed is reported as one retexted line instead of a
/// missing line plus an extra one.
/// </para>
/// </summary>
public static class StructureDiff
{
    /// <summary>Displacement below this is rounding, not a layout difference.</summary>
    public const double DefaultToleranceP = 0.75;

    /// <summary>Token overlap needed before two unmatched lines are called the same line, edited.</summary>
    private const double FuzzyThreshold = 0.5;

    public static PageStructureDiff Compare(
        PdfPageModel actual, PdfPageModel reference, double tolerancePt = DefaultToleranceP)
    {
        var actualLines = actual.Lines;
        var referenceLines = reference.Lines;

        var pairs = MatchExact(actualLines, referenceLines);
        MatchFuzzy(actualLines, referenceLines, pairs);

        // The page shift is taken from the exactly-matched lines only. Including fuzzy or
        // unmatched lines would let the very content that moved define "normal".
        var matchedDy = new List<double>();
        var matchedDx = new List<double>();
        foreach ((int ai, int ri) in pairs.OrderBy(p => p.Value).Select(p => (p.Key, p.Value)))
        {
            matchedDy.Add(actualLines[ai].Baseline - referenceLines[ri].Baseline);
            matchedDx.Add(actualLines[ai].Bounds.Left - referenceLines[ri].Bounds.Left);
        }
        double shiftY = StyleAnalyser.Median(matchedDy);
        double shiftX = StyleAnalyser.Median(matchedDx);

        var entries = new List<LineDiffEntry>();
        var usedActual = new HashSet<int>(pairs.Keys);
        var byReference = pairs.ToDictionary(p => p.Value, p => p.Key);

        for (int ri = 0; ri < referenceLines.Count; ri++)
        {
            if (!byReference.TryGetValue(ri, out int ai))
            {
                entries.Add(new LineDiffEntry
                {
                    Change = LineChange.Missing,
                    Reference = referenceLines[ri],
                    StyleChanges = Array.Empty<string>(),
                });
                continue;
            }

            TextLine a = actualLines[ai], r = referenceLines[ri];
            double dx = a.Bounds.Left - r.Bounds.Left;
            double dy = a.Baseline - r.Baseline;
            double residual = dy - shiftY;
            var styleChanges = StyleChanges(a, r);
            bool textDiffers = !Normalise(a.Text).Equals(Normalise(r.Text), StringComparison.Ordinal);
            bool moved = Math.Abs(dx) > tolerancePt || Math.Abs(residual) > tolerancePt;

            LineChange change =
                textDiffers ? LineChange.Retexted
                : styleChanges.Count > 0 ? LineChange.Restyled
                : moved ? LineChange.Moved
                : LineChange.Same;

            entries.Add(new LineDiffEntry
            {
                Change = change,
                Actual = a,
                Reference = r,
                DeltaX = dx,
                DeltaY = dy,
                ResidualY = residual,
                StyleChanges = styleChanges,
            });
        }

        for (int ai = 0; ai < actualLines.Count; ai++)
        {
            if (!usedActual.Contains(ai))
            {
                entries.Add(new LineDiffEntry
                {
                    Change = LineChange.Extra,
                    Actual = actualLines[ai],
                    StyleChanges = Array.Empty<string>(),
                });
            }
        }

        // Report in page order, so the reader walks down the page rather than through a
        // list sorted by how the algorithm happened to find things.
        entries = entries
            .OrderBy(e => e.Reference?.Baseline ?? e.Actual?.Baseline ?? 0)
            .ThenBy(e => e.Reference?.Bounds.Left ?? e.Actual?.Bounds.Left ?? 0)
            .ToList();

        double similarity = SequenceSimilarity(actual.Words, reference.Words);
        int matchedLines = pairs.Count;
        int maxLines = Math.Max(1, Math.Max(actualLines.Count, referenceLines.Count));

        return new PageStructureDiff
        {
            Entries = entries,
            PageShiftPt = shiftY,
            PageShiftXPt = shiftX,
            TextSimilarity = similarity,
            Rewrapped = similarity > 0.9 && (double)matchedLines / maxLines < 0.7,
        };
    }

    // ---------------------------------------------------------------------------- matching

    /// <summary>Longest common subsequence over normalised line text: actual index → reference index.</summary>
    private static Dictionary<int, int> MatchExact(
        IReadOnlyList<TextLine> actual, IReadOnlyList<TextLine> reference)
    {
        int n = actual.Count, m = reference.Count;
        var a = actual.Select(l => Normalise(l.Text)).ToArray();
        var r = reference.Select(l => Normalise(l.Text)).ToArray();

        var table = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                table[i, j] = a[i] == r[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var pairs = new Dictionary<int, int>();
        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (a[i] == r[j])
            {
                pairs[i] = j;
                i++;
                j++;
            }
            else if (table[i + 1, j] >= table[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }
        return pairs;
    }

    /// <summary>
    /// Pair up what the LCS left over, by token overlap. Greedy on the best score, which
    /// is enough: these are the leftovers, and there are rarely many.
    /// </summary>
    private static void MatchFuzzy(
        IReadOnlyList<TextLine> actual, IReadOnlyList<TextLine> reference, Dictionary<int, int> pairs)
    {
        var freeActual = Enumerable.Range(0, actual.Count).Where(i => !pairs.ContainsKey(i)).ToList();
        var takenReference = new HashSet<int>(pairs.Values);
        var freeReference = Enumerable.Range(0, reference.Count).Where(j => !takenReference.Contains(j)).ToList();

        var candidates = new List<(double Score, int A, int R)>();
        foreach (int i in freeActual)
        {
            foreach (int j in freeReference)
            {
                double score = TokenOverlap(actual[i].Text, reference[j].Text);
                if (score >= FuzzyThreshold)
                {
                    candidates.Add((score, i, j));
                }
            }
        }

        foreach ((double _, int i, int j) in candidates.OrderByDescending(c => c.Score))
        {
            if (!pairs.ContainsKey(i) && !pairs.ContainsValue(j))
            {
                pairs[i] = j;
            }
        }
    }

    private static double TokenOverlap(string a, string b)
    {
        var sa = Normalise(a).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var sb = Normalise(b).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (sa.Count == 0 || sb.Count == 0)
        {
            return 0;
        }
        int shared = sa.Count(t => sb.Contains(t));
        return (double)shared / Math.Max(sa.Count, sb.Count);
    }

    private static IReadOnlyList<string> StyleChanges(TextLine actual, TextLine reference)
    {
        var changes = new List<string>();
        if (!string.Equals(actual.FontName, reference.FontName, StringComparison.Ordinal))
        {
            changes.Add($"font-family {reference.FontName} → {actual.FontName}");
        }
        if (Math.Abs(actual.FontSize - reference.FontSize) > 0.05)
        {
            changes.Add($"font-size {reference.FontSize:F1}pt → {actual.FontSize:F1}pt");
        }
        if (actual.Bold != reference.Bold)
        {
            changes.Add($"font-weight {(reference.Bold ? "bold" : "normal")} → {(actual.Bold ? "bold" : "normal")}");
        }
        if (actual.Italic != reference.Italic)
        {
            changes.Add($"font-style {(reference.Italic ? "italic" : "normal")} → {(actual.Italic ? "italic" : "normal")}");
        }
        if (!string.Equals(actual.Color, reference.Color, StringComparison.Ordinal))
        {
            changes.Add($"color {reference.Color} → {actual.Color}");
        }
        return changes;
    }

    /// <summary>Case and spacing are the renderer's business; the words are the document's.</summary>
    public static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = true;
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!space)
                {
                    sb.Append(' ');
                    space = true;
                }
            }
            else
            {
                sb.Append(c);
                space = false;
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Longest-common-subsequence ratio over two word streams: 1.0 when the same words
    /// appear in the same order, regardless of where they were placed. This is the
    /// pagination-independent measure of "is the right content there at all", and it is
    /// the first thing to get to 1.0 when reverse-engineering a stylesheet.
    /// </summary>
    public static double SequenceSimilarity(IReadOnlyList<string> actual, IReadOnlyList<string> reference)
    {
        if (actual.Count == 0 && reference.Count == 0)
        {
            return 1.0;
        }
        if (actual.Count == 0 || reference.Count == 0)
        {
            return 0.0;
        }

        // The exact LCS is quadratic. Past a few million cells a long publication would
        // spend minutes on a number that a multiset overlap gets within a point or two of,
        // so beyond that budget the cheaper measure is used. It is still monotone in the
        // right direction, which is what a progress metric needs.
        if ((long)actual.Count * reference.Count > 25_000_000L)
        {
            return BagSimilarity(actual, reference);
        }

        // Two rolling rows rather than a full table: a long publication can run to tens of
        // thousands of words per side, where the full matrix would be gigabytes.
        var a = actual.Select(w => w.ToLowerInvariant()).ToArray();
        var r = reference.Select(w => w.ToLowerInvariant()).ToArray();
        var previous = new int[r.Length + 1];
        var current = new int[r.Length + 1];

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= r.Length; j++)
            {
                current[j] = a[i - 1] == r[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }
            (previous, current) = (current, previous);
            Array.Clear(current);
        }

        int lcs = previous[r.Length];
        return (double)lcs / Math.Max(a.Length, r.Length);
    }

    /// <summary>Multiset overlap: order-blind, but linear. The fallback for very long documents.</summary>
    private static double BagSimilarity(IReadOnlyList<string> actual, IReadOnlyList<string> reference)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string w in reference)
        {
            counts[w] = counts.GetValueOrDefault(w) + 1;
        }
        int shared = 0;
        foreach (string w in actual)
        {
            if (counts.TryGetValue(w, out int n) && n > 0)
            {
                counts[w] = n - 1;
                shared++;
            }
        }
        return (double)shared / Math.Max(actual.Count, reference.Count);
    }
}
