using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-neutralize</c>: generate IETP-neutral metadata for CSDB
/// objects. Neutralizing a data/publication module adds:
/// <list type="bullet">
///   <item>XLink attributes on linking elements, using the S1000D URN scheme
///         (<c>xlink.xsl</c>);</item>
///   <item>RDF + Dublin Core descriptive metadata (<c>rdf.xsl</c>);</item>
///   <item>optionally, the IETP <c>dm:</c>/<c>pm:</c> namespaces on elements
///         (<c>namespace.xsl</c>, enabled with <c>-n</c>).</item>
/// </list>
/// The <c>-D</c> option reverses the process, stripping neutral metadata
/// (<c>delete.xsl</c>).
///
/// The transformation is performed by applying the original tool's embedded
/// XSLT 1.0 stylesheets (copied verbatim into <c>Resources/neutralize/</c>) via
/// <see cref="XslCompiledTransform"/>. The stylesheets use only plain XSLT 1.0
/// constructs (no EXSLT), so no extension-object shim is required.
/// </summary>
public sealed class NeutralizeTool : ITool
{
    public string Name => "neutralize";

    public string Description => "Generate IETP-neutral metadata for CSDB objects.";

    // Mirrors VERSION in reference/tools/s1kd-neutralize/s1kd-neutralize.c.
    public string Version => "1.11.0";

    // The C tool only ever returns 0 (EXIT_SUCCESS); a bad list file is reported
    // but does not change the exit status. We keep a non-zero code for the .NET
    // edge case of an unknown option / missing argument so the CLI behaves.
    private const int ExitSuccess = 0;
    private const int ExitBadArgs = 2;

    private enum Verbosity { Quiet, Normal, Verbose }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string outfile = "-";
        bool overwrite = false;
        bool islist = false;
        bool namesp = false;
        bool delete = false;
        var verbosity = Verbosity.Normal;
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
                case "-D" or "--delete":
                    delete = true;
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-l" or "--list":
                    islist = true;
                    break;
                case "-n" or "--namespace":
                    namesp = true;
                    break;
                case "-o" or "--out":
                    if (++i >= args.Count)
                    {
                        if (verbosity >= Verbosity.Normal)
                        {
                            stderr.WriteLine($"{ErrPrefix}-o requires an argument");
                        }
                        return ExitBadArgs;
                    }
                    outfile = args[i];
                    break;
                case "-q" or "--quiet":
                    verbosity = Verbosity.Quiet;
                    break;
                case "-v" or "--verbose":
                    verbosity = Verbosity.Verbose;
                    break;
                default:
                    // Support clustered short options (e.g. "-fn") like getopt.
                    if (a.Length > 2 && a[0] == '-' && a[1] != '-')
                    {
                        bool consumed = true;
                        for (int c = 1; c < a.Length && consumed; c++)
                        {
                            switch (a[c])
                            {
                                case 'D': delete = true; break;
                                case 'f': overwrite = true; break;
                                case 'l': islist = true; break;
                                case 'n': namesp = true; break;
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

        // Dispatch mirrors main() in the C source.
        if (files.Count > 0)
        {
            foreach (string file in files)
            {
                if (islist)
                {
                    ProcessList(file, outfile, overwrite, namesp, delete, verbosity, stderr);
                }
                else
                {
                    ProcessFile(file, outfile, overwrite, namesp, delete, verbosity, stdout, stderr);
                }
            }
        }
        else if (islist)
        {
            ProcessList(null, outfile, overwrite, namesp, delete, verbosity, stderr);
        }
        else
        {
            // No files and not a list: read a single object from stdin. The C
            // tool forces overwrite=false in this case (output goes to -o/stdout).
            ProcessFile("-", outfile, false, namesp, delete, verbosity, stdout, stderr);
        }

        return ExitSuccess;
    }

    private void ProcessList(string? path, string outfile, bool overwrite, bool namesp,
        bool delete, Verbosity verbosity, TextWriter stderr)
    {
        TextReader reader;
        bool closeReader;
        if (path != null)
        {
            try
            {
                reader = new StreamReader(File.OpenRead(path));
                closeReader = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{ErrPrefix}Could not read list: {path}");
                }
                return;
            }
        }
        else
        {
            reader = new StreamReader(Console.OpenStandardInput());
            closeReader = false;
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // strtok(line, "\t\r\n") in the C: take up to the first tab/CR/LF.
                string fname = line.Split('\t', '\r', '\n')[0];
                if (fname.Length == 0)
                {
                    continue;
                }
                // List mode writes each object back; output to stdout is not
                // meaningful for a list, so respect overwrite/-o like the C tool.
                ProcessFile(fname, outfile, overwrite, namesp, delete, verbosity,
                    TextWriter.Null, stderr);
            }
        }
        finally
        {
            if (closeReader)
            {
                reader.Dispose();
            }
        }
    }

    private void ProcessFile(string fname, string outfile, bool overwrite, bool namesp,
        bool delete, Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        if (verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"{InfPrefix}Adding neutral metadata to {fname}...");
        }

        XmlDocument doc;
        try
        {
            doc = fname == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(fname);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Could not read {fname}: {ex.Message}");
            }
            return;
        }

        XmlDocument result = delete
            ? Deneutralize(doc)
            : Neutralize(doc, namesp);

        if (overwrite && fname != "-")
        {
            XmlUtils.SaveDoc(result, fname);
        }
        else if (outfile != "-")
        {
            XmlUtils.SaveDoc(result, outfile);
        }
        else
        {
            stdout.Write(XmlUtils.ToXmlString(result));
            stdout.Write('\n');
        }
    }

    /// <summary>
    /// Apply the xlink + rdf (+ optional namespace) stylesheets, mirroring
    /// <c>neutralizeFile</c> in the C source.
    /// </summary>
    public static XmlDocument Neutralize(XmlDocument doc, bool namesp)
    {
        XmlDocument res = Transform(doc, "neutralize/xlink.xsl");
        res = Transform(res, "neutralize/rdf.xsl");
        if (namesp)
        {
            res = Transform(res, "neutralize/namespace.xsl");
        }
        return res;
    }

    /// <summary>
    /// Apply the delete stylesheet, mirroring <c>deneutralizeFile</c> in the C
    /// source.
    /// </summary>
    public static XmlDocument Deneutralize(XmlDocument doc)
    {
        return Transform(doc, "neutralize/delete.xsl");
    }

    private static XmlDocument Transform(XmlDocument doc, string resourcePath)
    {
        var xslt = new XslCompiledTransform();

        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };

        using (Stream styleStream = EmbeddedResources.Open(resourcePath)
            ?? throw new FileNotFoundException($"Embedded stylesheet not found: {resourcePath}"))
        using (XmlReader styleReader = XmlReader.Create(styleStream, readerSettings))
        {
            xslt.Load(styleReader);
        }

        var output = XmlUtils.NewDocument();
        using (var ms = new MemoryStream())
        {
            // Match libxml2/libxslt serialization defaults closely: no BOM, no
            // forced indentation beyond what the stylesheet emits.
            var writerSettings = new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Auto,
                OmitXmlDeclaration = true,
            };
            using (XmlWriter writer = XmlWriter.Create(ms, writerSettings))
            {
                xslt.Transform(doc, writer);
            }
            ms.Position = 0;
            using XmlReader resultReader = XmlReader.Create(ms, readerSettings);
            output.Load(resultReader);
        }

        return output;
    }

    private const string ToolPrefix = "s1kd-neutralize";
    private const string ErrPrefix = ToolPrefix + ": ERROR: ";
    private const string InfPrefix = ToolPrefix + ": INFO: ";

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: {ToolPrefix} [-o <file>] [-Dflnqvh?] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -D, --delete      Remove neutral metadata.");
        stdout.WriteLine("  -f, --overwrite   Overwrite CSDB objects automatically.");
        stdout.WriteLine("  -h, -?, --help    Show usage message.");
        stdout.WriteLine("  -l, --list        Treat input as list of CSDB objects.");
        stdout.WriteLine("  -n, --namespace   Include IETP namespaces on elements.");
        stdout.WriteLine("  -o, --out <file>  Output to <file> instead of stdout.");
        stdout.WriteLine("  -q, --quiet       Quiet mode.");
        stdout.WriteLine("  -v, --verbose     Verbose output.");
        stdout.WriteLine("  --version         Show version information.");
    }
}
