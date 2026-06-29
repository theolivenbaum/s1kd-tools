using System.Text;
using System.Xml;
using System.Xml.Xsl;
using Fop.Render.Pdf;
using Fop.Render.Text;

namespace S1kdTools.Tools;

/// <summary>
/// <c>s1kd-render</c>: render S1000D CSDB objects to a presentation format using
/// the <a href="https://www.nuget.org/packages/FOP.Sharp">FOP.Sharp</a> engine
/// (the C# port of Apache FOP).
///
/// <para>
/// This tool has no direct counterpart in the upstream C s1kd-tools — the C
/// project leaves rendering to an external XSL-FO processor (Apache FOP, run as
/// a separate Java process). FOP.Sharp brings that processor in-process, so the
/// .NET port can produce the output targets FOP supports directly: <b>PDF</b>,
/// plain <b>text</b>, <b>Markdown</b> and <b>HTML</b>.
/// </para>
///
/// <para>
/// S1000D objects are not themselves XSL-FO, so a <i>presentation stylesheet</i>
/// (<c>-s</c>) transforms the data/publication module into XSL-FO first; the FO
/// is then laid out and rendered. When the input is already XSL-FO (e.g. the
/// output of an earlier transform), pass <c>-F</c> to skip the transform step.
/// </para>
/// </summary>
public sealed class RenderTool : ITool
{
    public string Name => "render";

    public string Description => "Render CSDB objects to PDF, text, Markdown or HTML (via FOP.Sharp).";

    // No upstream C tool to track; versioned independently within the suite.
    public string Version => "1.0.0";

    private const int ExitSuccess = 0;
    private const int ExitError = 1;
    private const int ExitBadArgs = 2;

    private const string ToolPrefix = "s1kd-render";
    private const string ErrPrefix = ToolPrefix + ": ERROR: ";
    private const string InfPrefix = ToolPrefix + ": INFO: ";

    /// <summary>The presentation formats FOP.Sharp can produce.</summary>
    public enum RenderFormat
    {
        /// <summary>PDF (the default).</summary>
        Pdf,

        /// <summary>Plain UTF-8 text.</summary>
        Text,

        /// <summary>GitHub-flavoured Markdown.</summary>
        Markdown,

        /// <summary>A semantic HTML5 document.</summary>
        Html,
    }

    private enum Verbosity { Quiet, Normal, Verbose }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? stylesheet = null;
        string? outfile = null;
        RenderFormat? format = null;
        bool foInput = false;
        bool native = false;
        var verbosity = Verbosity.Normal;
        var fontDirs = new List<string>();
        var xsltParams = new Dictionary<string, string>(StringComparer.Ordinal);
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
                case "-F" or "--fo":
                    foInput = true;
                    break;
                case "-n" or "--native":
                    native = true;
                    break;
                case "-q" or "--quiet":
                    verbosity = Verbosity.Quiet;
                    break;
                case "-v" or "--verbose":
                    verbosity = Verbosity.Verbose;
                    break;
                case "-s" or "--stylesheet":
                    if (!Next(args, ref i, out stylesheet))
                    {
                        return MissingArg(a, verbosity, stderr);
                    }
                    break;
                case "-o" or "--out":
                    if (!Next(args, ref i, out outfile))
                    {
                        return MissingArg(a, verbosity, stderr);
                    }
                    break;
                case "-t" or "--format":
                    if (!Next(args, ref i, out string? fmt))
                    {
                        return MissingArg(a, verbosity, stderr);
                    }
                    if (!TryParseFormat(fmt!, out RenderFormat parsed))
                    {
                        if (verbosity >= Verbosity.Normal)
                        {
                            stderr.WriteLine($"{ErrPrefix}Unknown format: {fmt}");
                        }
                        return ExitBadArgs;
                    }
                    format = parsed;
                    break;
                case "-d" or "--fontdir":
                    if (!Next(args, ref i, out string? dir))
                    {
                        return MissingArg(a, verbosity, stderr);
                    }
                    fontDirs.Add(dir!);
                    break;
                case "-p" or "--param":
                    if (!Next(args, ref i, out string? param))
                    {
                        return MissingArg(a, verbosity, stderr);
                    }
                    int eq = param!.IndexOf('=');
                    if (eq < 1)
                    {
                        if (verbosity >= Verbosity.Normal)
                        {
                            stderr.WriteLine($"{ErrPrefix}Invalid parameter (expected name=value): {param}");
                        }
                        return ExitBadArgs;
                    }
                    xsltParams[param[..eq]] = param[(eq + 1)..];
                    break;
                default:
                    // Clustered short flags (boolean only), e.g. "-Fn", like getopt.
                    if (a.Length > 2 && a[0] == '-' && a[1] != '-')
                    {
                        bool consumed = true;
                        for (int c = 1; c < a.Length && consumed; c++)
                        {
                            switch (a[c])
                            {
                                case 'F': foInput = true; break;
                                case 'n': native = true; break;
                                case 'q': verbosity = Verbosity.Quiet; break;
                                case 'v': verbosity = Verbosity.Verbose; break;
                                case 'h' or '?': ShowHelp(stdout); return ExitSuccess;
                                default: consumed = false; break;
                            }
                        }
                        if (consumed)
                        {
                            break;
                        }
                    }

                    if (a.Length > 1 && a[0] == '-' && a != "-")
                    {
                        if (verbosity >= Verbosity.Normal)
                        {
                            stderr.WriteLine($"{ErrPrefix}Unknown option: {a}");
                        }
                        return ExitBadArgs;
                    }
                    files.Add(a);
                    break;
            }
        }

        if (!foInput && stylesheet == null)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}A presentation stylesheet is required (-s), or pass -F for XSL-FO input.");
            }
            return ExitBadArgs;
        }

        // No files: render a single object read from stdin.
        if (files.Count == 0)
        {
            files.Add("-");
        }

        if (outfile != null && files.Count > 1)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}-o cannot be used with multiple inputs; outputs are auto-named.");
            }
            return ExitBadArgs;
        }

        XslCompiledTransform? style = null;
        if (!foInput)
        {
            try
            {
                style = LoadStylesheet(stylesheet!);
            }
            catch (Exception ex) when (ex is IOException or XmlException or XsltException or UnauthorizedAccessException)
            {
                if (verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{ErrPrefix}Could not load stylesheet {stylesheet}: {ex.Message}");
                }
                return ExitError;
            }
        }

        int exit = ExitSuccess;
        foreach (string file in files)
        {
            if (!RenderOne(file, style, xsltParams, outfile, format, fontDirs, native, verbosity, stdout, stderr))
            {
                exit = ExitError;
            }
        }

        return exit;
    }

    private bool RenderOne(string file, XslCompiledTransform? style, IReadOnlyDictionary<string, string> xsltParams,
        string? outfile, RenderFormat? format, IReadOnlyList<string> fontDirs, bool native,
        Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        // Resolve the output target and format. With no -o the output is derived
        // from the input name (replacing its extension); stdin with no -o goes to
        // stdout. The format is the explicit -t, else inferred from the output
        // extension, else PDF.
        bool toStdout = outfile == null && file == "-";
        RenderFormat fmt = format ?? (outfile != null ? FormatFromExtension(outfile) : RenderFormat.Pdf);
        string? target = outfile ?? (toStdout ? null : DeriveOutputPath(file, fmt));

        string foXml;
        try
        {
            foXml = ProduceFo(file, style, xsltParams);
        }
        catch (Exception ex) when (ex is IOException or XmlException or XsltException or UnauthorizedAccessException)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Could not process {InputLabel(file)}: {ex.Message}");
            }
            return false;
        }

        byte[] output;
        try
        {
            output = Render(foXml, fmt, fontDirs, native);
        }
        catch (Exception ex)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Failed to render {InputLabel(file)}: {ex.Message}");
            }
            return false;
        }

        if (verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"{InfPrefix}Rendered {InputLabel(file)} to {(target ?? "stdout")} ({fmt}).");
        }

        try
        {
            if (target != null)
            {
                File.WriteAllBytes(target, output);
            }
            else
            {
                using Stream os = Console.OpenStandardOutput();
                os.Write(output, 0, output.Length);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Could not write {target}: {ex.Message}");
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// Produce the XSL-FO source for an input object: transform it through the
    /// presentation <paramref name="style"/> (passing <paramref name="xsltParams"/>),
    /// or, when <paramref name="style"/> is null, take the input verbatim as FO.
    /// </summary>
    private static string ProduceFo(string file, XslCompiledTransform? style,
        IReadOnlyDictionary<string, string> xsltParams)
    {
        XmlDocument doc = file == "-"
            ? XmlUtils.ReadStream(Console.OpenStandardInput())
            : XmlUtils.ReadDoc(file);

        if (style == null)
        {
            return XmlUtils.ToXmlString(doc);
        }

        var argList = new XsltArgumentList();
        foreach (KeyValuePair<string, string> p in xsltParams)
        {
            argList.AddParam(p.Key, string.Empty, p.Value);
        }

        using var buffer = new MemoryStream();
        var writerSettings = new XmlWriterSettings
        {
            CloseOutput = false,
            OmitXmlDeclaration = true,
        };
        using (XmlWriter writer = XmlWriter.Create(buffer, writerSettings))
        {
            style.Transform(doc, argList, writer);
        }

        return new UTF8Encoding(false).GetString(buffer.ToArray());
    }

    /// <summary>
    /// Render an XSL-FO document to the requested format, returning the encoded
    /// bytes (UTF-8 for the text formats). This is the programmatic entry point
    /// used by the CLI and tests.
    /// </summary>
    public static byte[] Render(string foXml, RenderFormat format,
        IReadOnlyList<string>? fontDirs = null, bool native = false)
    {
        ArgumentNullException.ThrowIfNull(foXml);

        switch (format)
        {
            case RenderFormat.Text:
                return Utf8(new PlainTextRenderer().Convert(foXml));
            case RenderFormat.Markdown:
                return Utf8(new MarkdownRenderer().Convert(foXml));
            case RenderFormat.Html:
                return Utf8(new HtmlRenderer().Convert(foXml));
            default:
                var processor = new FopProcessor();
                if (fontDirs != null)
                {
                    foreach (string dir in fontDirs)
                    {
                        if (Directory.Exists(dir))
                        {
                            processor.RegisterFontsDirectory(dir);
                        }
                    }
                }

                if (native)
                {
                    using var ms = new MemoryStream();
                    using (var fo = new MemoryStream(Utf8(foXml)))
                    {
                        processor.ConvertNative(fo, ms);
                    }
                    return ms.ToArray();
                }

                return processor.Convert(foXml);
        }
    }

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    private static XslCompiledTransform LoadStylesheet(string path)
    {
        var xslt = new XslCompiledTransform();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };
        using XmlReader reader = XmlReader.Create(path, settings);
        // Allow the stylesheet to resolve xsl:include/xsl:import relative to its
        // own location, matching libxslt's default document resolution.
        xslt.Load(reader, XsltSettings.TrustedXslt, new XmlUrlResolver());
        return xslt;
    }

    private static bool TryParseFormat(string s, out RenderFormat format)
    {
        switch (s.ToLowerInvariant())
        {
            case "pdf":
                format = RenderFormat.Pdf;
                return true;
            case "txt" or "text":
                format = RenderFormat.Text;
                return true;
            case "md" or "markdown":
                format = RenderFormat.Markdown;
                return true;
            case "html" or "htm":
                format = RenderFormat.Html;
                return true;
            default:
                format = RenderFormat.Pdf;
                return false;
        }
    }

    private static RenderFormat FormatFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" or ".text" => RenderFormat.Text,
            ".md" or ".markdown" => RenderFormat.Markdown,
            ".html" or ".htm" => RenderFormat.Html,
            _ => RenderFormat.Pdf,
        };

    private static string Extension(RenderFormat format) => format switch
    {
        RenderFormat.Text => ".txt",
        RenderFormat.Markdown => ".md",
        RenderFormat.Html => ".html",
        _ => ".pdf",
    };

    private static string DeriveOutputPath(string input, RenderFormat format)
    {
        string dir = Path.GetDirectoryName(input) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(input);
        return Path.Combine(dir, stem + Extension(format));
    }

    private static string InputLabel(string file) => file == "-" ? "<stdin>" : file;

    private static bool Next(IReadOnlyList<string> args, ref int i, out string? value)
    {
        if (i + 1 >= args.Count)
        {
            value = null;
            return false;
        }
        value = args[++i];
        return true;
    }

    private int MissingArg(string flag, Verbosity verbosity, TextWriter stderr)
    {
        if (verbosity >= Verbosity.Normal)
        {
            stderr.WriteLine($"{ErrPrefix}{flag} requires an argument.");
        }
        return ExitBadArgs;
    }

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: {ToolPrefix} (-s <stylesheet> | -F) [-t <format>] [-o <file>] [options] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Render S1000D CSDB objects to a presentation format using FOP.Sharp.");
        stdout.WriteLine("A presentation stylesheet (-s) transforms each object into XSL-FO, which is");
        stdout.WriteLine("then rendered. Use -F when the input is already XSL-FO.");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -s, --stylesheet <xsl>  XSLT stylesheet that produces XSL-FO from the object.");
        stdout.WriteLine("  -F, --fo                Input is already XSL-FO; skip the transform.");
        stdout.WriteLine("  -t, --format <fmt>      Output format: pdf (default), text, md or html.");
        stdout.WriteLine("  -o, --out <file>        Output file (single input only). Default: derived");
        stdout.WriteLine("                          from the input name, or stdout when reading stdin.");
        stdout.WriteLine("  -p, --param <n=v>       Pass a parameter to the stylesheet (repeatable).");
        stdout.WriteLine("  -d, --fontdir <dir>     Register TTF/OTF fonts from <dir> (repeatable, PDF).");
        stdout.WriteLine("  -n, --native            Use the native (PdfSharp-free) PDF renderer.");
        stdout.WriteLine("  -q, --quiet             Quiet mode.");
        stdout.WriteLine("  -v, --verbose           Verbose output.");
        stdout.WriteLine("  -h, -?, --help          Show usage message.");
        stdout.WriteLine("      --version           Show version information.");
        stdout.WriteLine();
        stdout.WriteLine("If -t is omitted, the format is inferred from the output extension, else PDF.");
    }
}
