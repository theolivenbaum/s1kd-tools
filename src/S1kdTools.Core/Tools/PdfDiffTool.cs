using System.Globalization;
using S1kdTools.Pdf;

namespace S1kdTools.Tools;

/// <summary>
/// <c>s1kd-pdfdiff</c>: compare a rendered PDF against a reference PDF and report the
/// differences in the terms a presentation stylesheet is written in.
///
/// <para>
/// It has no counterpart in the upstream C s1kd-tools, and exists for one job: reverse
/// engineering a stylesheet. You have a PDF from some other toolchain, the S1000D source
/// it was built from, and a stylesheet of your own that does not yet produce the same
/// thing. This tool measures the gap — page count, words per page, ink per page, and
/// <i>where</i> on the page the ink differs — and takes the first divergent page apart in
/// enough detail to say what to change.
/// </para>
///
/// <para>
/// The metrics are built to be tracked across iterations rather than merely looked at
/// once: a single parity score out of 100, and the five agreements it is made of.
/// </para>
/// </summary>
public sealed class PdfDiffTool : ITool
{
    public string Name => "pdfdiff";

    public string Description => "Compare two PDFs and report the differences (for stylesheet work).";

    // No upstream C tool to track; versioned independently within the suite.
    public string Version => "1.0.0";

    private const int ExitMatch = 0;
    private const int ExitDiffers = 1;
    private const int ExitError = 2;

    private const string ToolPrefix = "s1kd-pdfdiff";
    private const string ErrPrefix = ToolPrefix + ": ERROR: ";

    /// <summary>What the report is written as.</summary>
    public enum ReportFormat
    {
        /// <summary>A Markdown report meant to be read (the default).</summary>
        Markdown,

        /// <summary>The same comparison as JSON, for tooling.</summary>
        Json,

        /// <summary>The one-line progress summary and nothing else.</summary>
        Summary,
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? outFile = null;
        string? jsonFile = null;
        string? imageDir = null;
        var format = ReportFormat.Markdown;
        bool allPages = false;
        bool quiet = false;
        int detailPages = 1;
        int maxLines = 80;
        double dpi = 144;
        int threshold = new InkDiffOptions().Threshold;
        double dilate = 1.5;
        double minRegion = 0.0004;
        double tolerance = StructureDiff.DefaultToleranceP;
        double? failUnder = null;
        var files = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return ExitMatch;
                case "--version":
                    stdout.WriteLine($"{ToolPrefix} (s1kd-tools) {Version}");
                    return ExitMatch;
                case "-a" or "--all-pages":
                    allPages = true;
                    break;
                case "-q" or "--quiet":
                    quiet = true;
                    break;
                case "-o" or "--out":
                    if (!Next(args, ref i, out outFile)) { return MissingArg(a, stderr); }
                    break;
                case "-j" or "--json":
                    if (!Next(args, ref i, out jsonFile)) { return MissingArg(a, stderr); }
                    break;
                case "-I" or "--images":
                    if (!Next(args, ref i, out imageDir)) { return MissingArg(a, stderr); }
                    break;
                case "-f" or "--format":
                    if (!Next(args, ref i, out string? fmt)) { return MissingArg(a, stderr); }
                    if (!TryParseFormat(fmt!, out format))
                    {
                        stderr.WriteLine($"{ErrPrefix}Unknown format: {fmt}");
                        return ExitError;
                    }
                    break;
                case "-p" or "--detail-pages":
                    if (!NextInt(args, ref i, a, stderr, out detailPages)) { return ExitError; }
                    break;
                case "-l" or "--max-lines":
                    if (!NextInt(args, ref i, a, stderr, out maxLines)) { return ExitError; }
                    break;
                case "-d" or "--dpi":
                    if (!NextDouble(args, ref i, a, stderr, out dpi)) { return ExitError; }
                    break;
                case "-t" or "--threshold":
                    if (!NextInt(args, ref i, a, stderr, out threshold)) { return ExitError; }
                    break;
                case "-D" or "--dilate":
                    if (!NextDouble(args, ref i, a, stderr, out dilate)) { return ExitError; }
                    break;
                case "-m" or "--min-region":
                    if (!NextDouble(args, ref i, a, stderr, out minRegion)) { return ExitError; }
                    break;
                case "-T" or "--tolerance":
                    if (!NextDouble(args, ref i, a, stderr, out tolerance)) { return ExitError; }
                    break;
                case "-F" or "--fail-under":
                    if (!NextDouble(args, ref i, a, stderr, out double under)) { return ExitError; }
                    failUnder = under;
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

        if (files.Count != 2)
        {
            stderr.WriteLine($"{ErrPrefix}Expected exactly two PDFs: <rendered.pdf> <reference.pdf>.");
            stderr.WriteLine($"Run '{ToolPrefix} --help' for usage.");
            return ExitError;
        }

        foreach (string file in files)
        {
            if (!File.Exists(file))
            {
                stderr.WriteLine($"{ErrPrefix}No such file: {file}");
                return ExitError;
            }
        }

        var options = new PdfCompareOptions
        {
            // 0 means "every divergent page"; --all-pages is the readable spelling of it.
            DetailPages = allPages ? 0 : detailPages,
            MaxLineEntries = maxLines,
            MovementTolerancePt = tolerance,
            ImageDirectory = imageDir,
            Ink = new InkDiffOptions
            {
                Dpi = dpi,
                Threshold = threshold,
                DilatePt = dilate,
                MinRegionFraction = minRegion,
            },
        };

        PdfComparison comparison;
        try
        {
            comparison = PdfComparer.Compare(files[0], files[1], options);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException)
        {
            stderr.WriteLine($"{ErrPrefix}{e.Message}");
            return ExitError;
        }

        string report = format switch
        {
            ReportFormat.Json => JsonReport.Write(comparison),
            ReportFormat.Summary => comparison.ProgressLine,
            _ => MarkdownReport.Write(comparison, options),
        };

        if (outFile is not null)
        {
            WriteTo(outFile, report);
        }
        else if (!quiet)
        {
            stdout.WriteLine(report);
        }

        // --json alongside a Markdown report is the common case: a human reads one and a
        // pipeline consumes the other from the same run, with no risk of them disagreeing.
        if (jsonFile is not null && format != ReportFormat.Json)
        {
            WriteTo(jsonFile, JsonReport.Write(comparison));
        }

        if (quiet && format != ReportFormat.Summary)
        {
            stdout.WriteLine(comparison.ProgressLine);
        }

        if (failUnder is { } floor)
        {
            return comparison.ParityScore < floor ? ExitDiffers : ExitMatch;
        }
        return comparison.Identical ? ExitMatch : ExitDiffers;
    }

    private static void WriteTo(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
    }

    private static bool TryParseFormat(string text, out ReportFormat format)
    {
        switch (text.ToLowerInvariant())
        {
            case "md" or "markdown": format = ReportFormat.Markdown; return true;
            case "json": format = ReportFormat.Json; return true;
            case "summary" or "line": format = ReportFormat.Summary; return true;
            default: format = ReportFormat.Markdown; return false;
        }
    }

    private static bool Next(IReadOnlyList<string> args, ref int i, out string? value)
    {
        if (i + 1 < args.Count)
        {
            value = args[++i];
            return true;
        }
        value = null;
        return false;
    }

    private static bool NextInt(
        IReadOnlyList<string> args, ref int i, string option, TextWriter stderr, out int value)
    {
        value = 0;
        if (!Next(args, ref i, out string? text))
        {
            MissingArg(option, stderr);
            return false;
        }
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            stderr.WriteLine($"{ErrPrefix}{option} expects a whole number, got: {text}");
            return false;
        }
        return true;
    }

    private static bool NextDouble(
        IReadOnlyList<string> args, ref int i, string option, TextWriter stderr, out double value)
    {
        value = 0;
        if (!Next(args, ref i, out string? text))
        {
            MissingArg(option, stderr);
            return false;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            stderr.WriteLine($"{ErrPrefix}{option} expects a number, got: {text}");
            return false;
        }
        return true;
    }

    private static int MissingArg(string option, TextWriter stderr)
    {
        stderr.WriteLine($"{ErrPrefix}{option} requires an argument.");
        return ExitError;
    }

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: {ToolPrefix} [options] <rendered.pdf> <reference.pdf>");
        stdout.WriteLine();
        stdout.WriteLine("Compare a PDF you produced against a reference PDF from another toolchain, and");
        stdout.WriteLine("report the differences as stylesheet properties: page count, words per page, ink");
        stdout.WriteLine("per page, where on the page the ink differs, and what changed line by line.");
        stdout.WriteLine();
        stdout.WriteLine("Metrics always cover the whole document. Detailed findings stop at the FIRST");
        stdout.WriteLine("divergent page, because differences cascade and later pages usually restate the");
        stdout.WriteLine("same cause. Use -a to detail every divergent page.");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -o, --out <file>        Write the report here instead of to stdout.");
        stdout.WriteLine("  -f, --format <fmt>      md (default), json, or summary (the progress line).");
        stdout.WriteLine("  -j, --json <file>       Also write the JSON report here.");
        stdout.WriteLine("  -I, --images <dir>      Write per-page PNGs: rendered, reference, and the");
        stdout.WriteLine("                          reference with differing regions boxed in red.");
        stdout.WriteLine("  -a, --all-pages         Detail every divergent page, not just the first.");
        stdout.WriteLine("  -p, --detail-pages <n>  Detail the first <n> divergent pages. Default 1.");
        stdout.WriteLine("  -l, --max-lines <n>     Line-level entries per detailed page. Default 80.");
        stdout.WriteLine("  -d, --dpi <n>           Ink raster resolution. Default 144 (2 cells per point).");
        stdout.WriteLine("  -t, --threshold <n>     Ink levels (0-255) closer than this are equal. Default 20.");
        stdout.WriteLine("  -D, --dilate <pt>       Join differing pixels this far apart. Default 1.5pt.");
        stdout.WriteLine("  -m, --min-region <f>    Ignore regions smaller than this share of the page.");
        stdout.WriteLine("                          Default 0.0004.");
        stdout.WriteLine("  -T, --tolerance <pt>    Displacement below this is not a move. Default 0.75pt.");
        stdout.WriteLine("  -F, --fail-under <n>    Exit 1 when the parity score is below <n>, rather than");
        stdout.WriteLine("                          on any difference at all. Use this as a build gate.");
        stdout.WriteLine("  -q, --quiet             Print only the one-line progress summary.");
        stdout.WriteLine("  -h, -?, --help          Show usage message.");
        stdout.WriteLine("      --version           Show version information.");
        stdout.WriteLine();
        stdout.WriteLine("Exit status: 0 when the renderings agree (or the score clears --fail-under),");
        stdout.WriteLine("1 when they differ, 2 on a usage or input error.");
    }
}
