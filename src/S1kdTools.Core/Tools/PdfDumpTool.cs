using System.Globalization;
using System.Text;
using System.Text.Json;
using S1kdTools.Pdf;

namespace S1kdTools.Tools;

/// <summary>
/// <c>s1kd-pdfdump</c>: describe what is on the pages of a PDF — every text line with its
/// position, font, size, weight and colour, every rule, fill and image, and the layout
/// measurements a stylesheet would have had to set to produce them.
///
/// <para>
/// The companion to <c>s1kd-pdfdiff</c>, and the first thing to reach for when reverse
/// engineering a stylesheet: before there is anything to compare, you need to know what
/// the target actually looks like. <c>--style</c> answers that in one screen — paper,
/// margins, body font, leading, indent stops, running heads.
/// </para>
/// </summary>
public sealed class PdfDumpTool : ITool
{
    public string Name => "pdfdump";

    public string Description => "Dump a PDF's page structure and layout measurements.";

    // No upstream C tool to track; versioned independently within the suite.
    public string Version => "1.0.0";

    private const int ExitSuccess = 0;
    private const int ExitError = 2;

    private const string ToolPrefix = "s1kd-pdfdump";
    private const string ErrPrefix = ToolPrefix + ": ERROR: ";

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? outFile = null;
        string? pageSpec = null;
        bool json = false;
        bool styleOnly = false;
        bool textOnly = false;
        var files = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return ExitSuccess;
                case "--version":
                    stdout.WriteLine($"{ToolPrefix} (s1kd-tools) {Version}");
                    return ExitSuccess;
                case "-J" or "--json":
                    json = true;
                    break;
                case "-s" or "--style":
                    styleOnly = true;
                    break;
                case "-x" or "--text":
                    textOnly = true;
                    break;
                case "-o" or "--out":
                    if (i + 1 >= args.Count) { return MissingArg(a, stderr); }
                    outFile = args[++i];
                    break;
                case "-p" or "--pages":
                    if (i + 1 >= args.Count) { return MissingArg(a, stderr); }
                    pageSpec = args[++i];
                    break;
                default:
                    if (a.Length > 1 && a[0] == '-' && a != "-")
                    {
                        stderr.WriteLine($"{ErrPrefix}Unknown option: {a}");
                        return ExitError;
                    }
                    files.Add(a);
                    break;
            }
        }

        if (files.Count != 1)
        {
            stderr.WriteLine($"{ErrPrefix}Expected exactly one PDF.");
            stderr.WriteLine($"Run '{ToolPrefix} --help' for usage.");
            return ExitError;
        }
        if (!File.Exists(files[0]))
        {
            stderr.WriteLine($"{ErrPrefix}No such file: {files[0]}");
            return ExitError;
        }

        PdfDocumentModel document;
        try
        {
            document = PdfExtractor.Load(files[0]);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException)
        {
            stderr.WriteLine($"{ErrPrefix}{e.Message}");
            return ExitError;
        }

        HashSet<int>? wanted = ParsePages(pageSpec, stderr);
        if (pageSpec is not null && wanted is null)
        {
            return ExitError;
        }

        DocumentStyleFacts style = StyleAnalyser.Analyse(document);
        var pages = document.Pages.Where(p => wanted is null || wanted.Contains(p.Number)).ToArray();

        string output = json
            ? Json(document, style, pages, styleOnly, textOnly)
            : Text(document, style, pages, styleOnly, textOnly);

        if (outFile is not null)
        {
            string? dir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(outFile, output);
        }
        else
        {
            stdout.WriteLine(output);
        }
        return ExitSuccess;
    }

    // ------------------------------------------------------------------------------ output

    private static string Text(
        PdfDocumentModel document, DocumentStyleFacts style,
        IReadOnlyList<PdfPageModel> pages, bool styleOnly, bool textOnly)
    {
        var sb = new StringBuilder();

        if (textOnly)
        {
            foreach (PdfPageModel page in pages)
            {
                sb.AppendLine($"=== page {page.Number} ===");
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }

        sb.AppendLine($"# {document.Path}");
        sb.AppendLine();
        sb.AppendLine($"pages          {document.PageCount}");
        sb.AppendLine($"words          {document.WordCount}");
        sb.AppendLine($"paper          {style.PaperName} ({style.Width:F1}x{style.Height:F1}pt)");
        sb.AppendLine($"margins        left {Length(style.MarginLeft)}  right {Length(style.MarginRight)}  "
                      + $"top {Length(style.MarginTop)}  bottom {Length(style.MarginBottom)}");
        sb.AppendLine($"body style     {style.Body?.Key ?? "(no text)"}");
        sb.AppendLine($"leading        {style.Leading:F1}pt"
                      + (style.LineHeightRatio > 0 ? $"  ({style.LineHeightRatio:F2}x the body size)" : ""));
        sb.AppendLine($"graphics       {style.RuleCount} rule(s), {style.FillCount} fill(s), {style.ImageCount} image(s)");
        if (style.RunningHeader.Count > 0)
        {
            sb.AppendLine($"running head   {string.Join(" | ", style.RunningHeader)}");
        }
        if (style.RunningFooter.Count > 0)
        {
            sb.AppendLine($"running foot   {string.Join(" | ", style.RunningFooter)}");
        }
        sb.AppendLine();

        sb.AppendLine("## Text styles (most glyphs first)");
        sb.AppendLine();
        foreach (FontUsage f in style.Fonts.Take(15))
        {
            sb.AppendLine($"  {f.Glyphs,7} glyphs  {f.Lines,4} lines  {f.Key}");
            sb.AppendLine($"                             e.g. \"{f.Sample}\"");
        }
        sb.AppendLine();

        if (style.IndentStops.Count > 0)
        {
            sb.AppendLine("## Indent stops (line-start x, and how many lines use it)");
            sb.AppendLine();
            foreach (IndentStop stop in style.IndentStops)
            {
                sb.AppendLine($"  x={stop.X,7:F1}pt ({StyleAnalyser.ToMillimetres(stop.X),6:F1}mm)  {stop.Lines} line(s)");
            }
            sb.AppendLine();
        }

        if (styleOnly)
        {
            return sb.ToString();
        }

        foreach (PdfPageModel page in pages)
        {
            PageStyleFacts facts = StyleAnalyser.Analyse(page);
            sb.AppendLine($"## Page {page.Number} — {page.Width:F1}x{page.Height:F1}pt, "
                          + $"{page.Lines.Count} line(s), {page.Graphics.Count} graphic mark(s), {page.WordCount} words");
            sb.AppendLine();
            sb.AppendLine($"   content box  {facts.ContentBounds?.ToString() ?? "(blank page)"}");
            sb.AppendLine($"   margins      left {Length(facts.MarginLeft)}  right {Length(facts.MarginRight)}  "
                          + $"top {Length(facts.MarginTop)}  bottom {Length(facts.MarginBottom)}");
            sb.AppendLine();

            // Text and graphics interleaved in page order: a rule belongs next to the
            // heading it underlines, not in a separate list at the end.
            var marks = page.Lines
                .Select(l => (Y: l.Baseline, X: l.Bounds.Left, Text: FormatLine(l)))
                .Concat(page.Graphics.Select(g => (Y: g.Bounds.Top, X: g.Bounds.Left, Text: FormatGraphic(g))))
                .OrderBy(m => m.Y)
                .ThenBy(m => m.X);
            foreach (var m in marks)
            {
                sb.AppendLine(m.Text);
            }
            sb.AppendLine();
        }

        return sb.ToString();

        static string Length(double pt) => $"{pt,6:F1}pt ({StyleAnalyser.ToMillimetres(pt):F1}mm)";

        static string FormatLine(TextLine l) =>
            $"   y={l.Baseline,7:F1} x={l.Bounds.Left,6:F1} w={l.Bounds.Width,6:F1}  "
            + $"{l.FontSize,5:F1}pt {l.FontName}{(l.Bold ? " bold" : "")}{(l.Italic ? " italic" : "")}"
            + $"{(l.Color == "#000000" ? "" : " " + l.Color)}  \"{l.Text}\"";

        static string FormatGraphic(GraphicMark g) =>
            $"   y={g.Bounds.Top,7:F1} x={g.Bounds.Left,6:F1} w={g.Bounds.Width,6:F1}  "
            + $"{g.Kind.ToString().ToLowerInvariant()} h={g.Bounds.Height:F1}pt {g.Color}";
    }

    private static string Json(
        PdfDocumentModel document, DocumentStyleFacts style,
        IReadOnlyList<PdfPageModel> pages, bool styleOnly, bool textOnly)
    {
        var root = new Dictionary<string, object?>
        {
            ["schema"] = "s1kd-pdfdump/1",
            ["path"] = document.Path,
            ["pages"] = document.PageCount,
            ["words"] = document.WordCount,
            ["paper"] = style.PaperName,
            ["pageWidthPt"] = Math.Round(style.Width, 2),
            ["pageHeightPt"] = Math.Round(style.Height, 2),
            ["marginsPt"] = new Dictionary<string, object?>
            {
                ["left"] = Math.Round(style.MarginLeft, 2),
                ["right"] = Math.Round(style.MarginRight, 2),
                ["top"] = Math.Round(style.MarginTop, 2),
                ["bottom"] = Math.Round(style.MarginBottom, 2),
            },
            ["bodyStyle"] = style.Body?.Key,
            ["leadingPt"] = Math.Round(style.Leading, 2),
            ["lineHeightRatio"] = Math.Round(style.LineHeightRatio, 3),
            ["fonts"] = style.Fonts.Take(20).Select(f => new Dictionary<string, object?>
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
            ["indentStopsPt"] = style.IndentStops.Select(s => new Dictionary<string, object?>
            {
                ["x"] = s.X,
                ["lines"] = s.Lines,
            }).ToArray(),
            ["runningHeader"] = style.RunningHeader,
            ["runningFooter"] = style.RunningFooter,
            ["rules"] = style.RuleCount,
            ["fills"] = style.FillCount,
            ["images"] = style.ImageCount,
        };

        if (textOnly)
        {
            root["text"] = pages.Select(p => new Dictionary<string, object?>
            {
                ["page"] = p.Number,
                ["text"] = p.Text,
            }).ToArray();
        }
        else if (!styleOnly)
        {
            root["outlines"] = pages.Select(p =>
            {
                var outline = JsonReport.Outline(p);
                outline["page"] = p.Number;
                return outline;
            }).ToArray();
        }

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Parses "3", "2-5", "1,4-6". Returns null when the spec is malformed.</summary>
    private static HashSet<int>? ParsePages(string? spec, TextWriter stderr)
    {
        if (spec is null)
        {
            return null;
        }
        var pages = new HashSet<int>();
        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] range = part.Split('-', 2);
            if (!int.TryParse(range[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int from))
            {
                stderr.WriteLine($"{ErrPrefix}Bad page spec: {part}");
                return null;
            }
            int to = from;
            if (range.Length == 2
                && !int.TryParse(range[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out to))
            {
                stderr.WriteLine($"{ErrPrefix}Bad page spec: {part}");
                return null;
            }
            for (int p = Math.Min(from, to); p <= Math.Max(from, to); p++)
            {
                pages.Add(p);
            }
        }
        return pages;
    }

    private static int MissingArg(string option, TextWriter stderr)
    {
        stderr.WriteLine($"{ErrPrefix}{option} requires an argument.");
        return ExitError;
    }

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: {ToolPrefix} [options] <file.pdf>");
        stdout.WriteLine();
        stdout.WriteLine("Describe what is on a PDF's pages: every text line with its position, font,");
        stdout.WriteLine("size, weight and colour, every rule, fill and image, and the layout the");
        stdout.WriteLine("stylesheet behind it must have set — paper, margins, body font, leading,");
        stdout.WriteLine("indent stops and running heads.");
        stdout.WriteLine();
        stdout.WriteLine("Positions are in points from the top-left of the page; for text lines, `y`");
        stdout.WriteLine("is the baseline.");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -s, --style             Layout measurements only; no per-page marks.");
        stdout.WriteLine("  -x, --text              Plain text of each page and nothing else.");
        stdout.WriteLine("  -p, --pages <spec>      Pages to dump: \"3\", \"2-5\", \"1,4-6\".");
        stdout.WriteLine("  -J, --json              Emit JSON instead of the readable dump.");
        stdout.WriteLine("  -o, --out <file>        Write here instead of to stdout.");
        stdout.WriteLine("  -h, -?, --help          Show usage message.");
        stdout.WriteLine("      --version           Show version information.");
    }
}
