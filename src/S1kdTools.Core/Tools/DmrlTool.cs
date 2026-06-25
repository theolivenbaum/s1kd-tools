using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-dmrl</c>: create CSDB objects from a Data Management
/// Requirements List (DMRL / DML).
///
/// The original C tool transforms each DML with an XSLT stylesheet
/// (<c>dmrl.xsl</c>) into a series of <c>s1kd-new*</c> shell command lines and
/// then executes them. This port replaces both halves of that pipeline with
/// direct DOM walking: for every DML entry it determines which <c>new*</c> tool
/// applies, builds the matching argument list (mirroring the option mapping in
/// <c>dmrl.xsl</c>), and then either
///   * dispatches to the ported tool in-process via <see cref="ToolRegistry"/>
///     (default), or
///   * prints the equivalent command line when <c>-s</c>/<c>--commands</c> is
///     given.
///
/// Building an argument list directly (rather than a shell string) avoids the
/// quoting concerns the C tool has and lets us call the other tools without
/// spawning processes. When a required <c>new*</c> tool has not been ported yet
/// the entry is skipped gracefully and reported on stderr instead of crashing.
/// </summary>
public sealed class DmrlTool : ITool
{
    public string Name => "dmrl";
    public string Description => "Create CSDB objects from a DMRL.";

    // Matches VERSION in reference/tools/s1kd-dmrl/s1kd-dmrl.c.
    public string Version => "1.12.0";

    // The C tool's exit status is the accumulated sum of the WEXITSTATUS of the
    // s1kd-new* commands it runs (0 when everything succeeds). We mirror that:
    // a non-zero status means at least one entry failed.
    private const int ExitSuccess = 0;
    private const int ExitBadUsage = 2;

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool execute = true;          // -s / --commands disables this
        bool noIssue = false;         // -N / --omit-issue
        bool failOnFirstErr = false;  // -F / --fail
        bool overwrite = false;       // -f / --overwrite
        bool noOverwriteError = false; // -q / --quiet
        bool verbose = false;         // -v / --verbose
        bool useRemarks = false;      // -m / --use-remarks
        string? specIssue = null;     // -$ / --issue
        string? templateDir = null;   // -% / --templates
        string? outDir = null;        // -@ / --out
        string? defaultsFname = null; // -d / --defaults
        string? dmtypesFname = null;  // -D / --dmtypes

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
                    stdout.WriteLine($"{Name} ({Version})");
                    return ExitSuccess;
                case "-s" or "--commands":
                    execute = false;
                    break;
                case "-m" or "--use-remarks":
                    useRemarks = true;
                    break;
                case "-N" or "--omit-issue":
                    noIssue = true;
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-F" or "--fail":
                    failOnFirstErr = true;
                    break;
                case "-q" or "--quiet":
                    noOverwriteError = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-$" or "--issue":
                    if (!TakeArg(args, ref i, out specIssue)) return ArgError(stderr, a);
                    break;
                case "-%" or "--templates":
                    if (!TakeArg(args, ref i, out templateDir)) return ArgError(stderr, a);
                    break;
                case "-@" or "--out":
                    if (!TakeArg(args, ref i, out outDir)) return ArgError(stderr, a);
                    break;
                case "-d" or "--defaults":
                    if (!TakeArg(args, ref i, out defaultsFname)) return ArgError(stderr, a);
                    break;
                case "-D" or "--dmtypes":
                    if (!TakeArg(args, ref i, out dmtypesFname)) return ArgError(stderr, a);
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return ExitBadUsage;
                    }
                    files.Add(a);
                    break;
            }
        }

        var opts = new GlobalOptions(
            NoIssue: noIssue,
            Overwrite: overwrite,
            NoOverwriteError: noOverwriteError,
            Verbose: verbose,
            UseRemarks: useRemarks,
            SpecIssue: specIssue,
            TemplateDir: templateDir,
            OutDir: outDir,
            DefaultsFname: defaultsFname,
            DmtypesFname: dmtypesFname);

        int err = 0;
        foreach (string file in files)
        {
            XmlDocument doc;
            try
            {
                doc = file == "-"
                    ? XmlUtils.ReadStream(Console.OpenStandardInput())
                    : XmlUtils.ReadDoc(file);
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                // The C tool silently skips files it cannot read (continue).
                stderr.WriteLine($"{Name}: ERROR: Could not read {file}: {ex.Message}");
                continue;
            }

            foreach (XmlElement entry in EnumerateEntries(doc))
            {
                var command = BuildCommand(entry, opts);
                if (command is null)
                {
                    // Entry that does not reference any known object kind; the
                    // XSLT would emit a bare line with no tool. Skip silently.
                    continue;
                }

                if (!execute)
                {
                    stdout.WriteLine(FormatCommandLine(command.Value.Tool, command.Value.Args));
                    continue;
                }

                ITool? target = ToolRegistry.Resolve(command.Value.Tool);
                if (target is null)
                {
                    // Required new* tool not ported yet: report and skip the
                    // entry without crashing.
                    stderr.WriteLine(
                        $"{Name}: ERROR: Cannot create object: tool '{command.Value.Tool}' is not available; skipping entry.");
                    err += 1;
                    if (failOnFirstErr) return err;
                    continue;
                }

                int r;
                try
                {
                    r = target.Run(command.Value.Args, stdout, stderr);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"{Name}: ERROR: {command.Value.Tool} failed: {ex.Message}");
                    r = 1;
                }

                err += r;
                if (err != 0 && failOnFirstErr)
                {
                    return err;
                }
            }
        }

        return err;
    }

    /// <summary>Global options carried into every entry (mirrors the XSLT params).</summary>
    private readonly record struct GlobalOptions(
        bool NoIssue,
        bool Overwrite,
        bool NoOverwriteError,
        bool Verbose,
        bool UseRemarks,
        string? SpecIssue,
        string? TemplateDir,
        string? OutDir,
        string? DefaultsFname,
        string? DmtypesFname);

    private readonly record struct Command(string Tool, List<string> Args);

    /// <summary>
    /// Yield the DML entry elements. The XSLT matches <c>dmlEntry|dmentry</c>
    /// anywhere under the root (reached via <c>dml -&gt; dmlContent|dmentry</c>
    /// and the recursive identity template); we simply collect every element
    /// with one of those local names.
    /// </summary>
    private static IEnumerable<XmlElement> EnumerateEntries(XmlDocument doc)
    {
        if (doc.DocumentElement is null)
        {
            yield break;
        }

        foreach (XmlElement el in doc.DocumentElement.SelectNodes(".//*")!.OfType<XmlElement>())
        {
            if (el.LocalName is "dmlEntry" or "dmentry")
            {
                yield return el;
            }
        }
    }

    /// <summary>
    /// Translate a single DML entry into the corresponding new* tool invocation,
    /// faithfully mirroring the option emission order of <c>dmrl.xsl</c>.
    /// Returns null when the entry references no known object kind.
    /// </summary>
    private static Command? BuildCommand(XmlElement entry, GlobalOptions opts)
    {
        // --- Tool selection (xsl:choose on the entry's reference child) ---
        string tool;
        bool appliesNoIssueFlag; // newdm/newpm/newdml honour -N; newcom/newimf do not
        bool appliesDmtypesFlag; // only newdm emits -D

        if (HasChild(entry, "dmRef") || HasChild(entry, "addresdm"))
        {
            tool = "newdm";
            appliesNoIssueFlag = true;
            appliesDmtypesFlag = true;
        }
        else if (HasChild(entry, "pmRef"))
        {
            tool = "newpm";
            appliesNoIssueFlag = true;
            appliesDmtypesFlag = false;
        }
        else if (HasChild(entry, "commentRef"))
        {
            tool = "newcom";
            appliesNoIssueFlag = false;
            appliesDmtypesFlag = false;
        }
        else if (HasChild(entry, "dmlRef"))
        {
            tool = "newdml";
            appliesNoIssueFlag = true;
            appliesDmtypesFlag = false;
        }
        else if (HasChild(entry, "infoEntityRef"))
        {
            tool = "newimf";
            appliesNoIssueFlag = false;
            appliesDmtypesFlag = false;
        }
        else
        {
            return null;
        }

        var argv = new List<string>();

        // Per-tool flags emitted right after the tool name.
        if (appliesNoIssueFlag && opts.NoIssue)
        {
            argv.Add("-N");
        }
        if (appliesDmtypesFlag && opts.DmtypesFname is not null)
        {
            argv.Add("-D");
            argv.Add(opts.DmtypesFname);
        }

        // Global flags (emitted for every tool, in XSLT order).
        if (opts.SpecIssue is not null)
        {
            argv.Add("-$");
            argv.Add(opts.SpecIssue);
        }
        if (opts.Overwrite)
        {
            argv.Add("-f");
        }
        if (opts.NoOverwriteError)
        {
            argv.Add("-q");
        }
        if (opts.Verbose)
        {
            argv.Add("-v");
        }
        if (opts.TemplateDir is not null)
        {
            argv.Add("-%");
            argv.Add(opts.TemplateDir);
        }
        if (opts.OutDir is not null)
        {
            argv.Add("-@");
            argv.Add(opts.OutDir);
        }
        if (opts.DefaultsFname is not null)
        {
            argv.Add("-d");
            argv.Add(opts.DefaultsFname);
        }

        // Per-element options: the XSLT recurses through every descendant of the
        // entry; emit options for those it has templates for, in document order.
        EmitElementOptions(entry, opts, argv);

        return new Command(tool, argv);
    }

    /// <summary>
    /// Walk the entry's descendants in document order, emitting new* options for
    /// the elements the stylesheet has templates for. Mirrors the recursive
    /// <c>apply-templates select="*"</c> traversal in <c>dmrl.xsl</c>.
    /// </summary>
    private static void EmitElementOptions(XmlElement context, GlobalOptions opts, List<string> argv)
    {
        foreach (XmlNode child in context.ChildNodes)
        {
            if (child is not XmlElement el)
            {
                continue;
            }

            switch (el.LocalName)
            {
                case "dmCode" or "dmc" or "pmCode" or "commentCode" or "dmlCode":
                    argv.Add("-#");
                    argv.Add(CodeToText(el));
                    break; // code elements have no relevant descendants

                case "infoEntityRef":
                    argv.Add(Attr(el, "infoEntityRefIdent"));
                    break;

                case "issueInfo":
                    argv.Add("-n");
                    argv.Add(Attr(el, "issueNumber"));
                    argv.Add("-w");
                    argv.Add(Attr(el, "inWork"));
                    break;

                case "issno":
                    argv.Add("-n");
                    argv.Add(Attr(el, "issno"));
                    if (el.HasAttribute("inwork"))
                    {
                        argv.Add("-w");
                        argv.Add(Attr(el, "inwork"));
                    }
                    break;

                case "language":
                    argv.Add("-L");
                    argv.Add(FirstAttr(el, "languageIsoCode", "language"));
                    argv.Add("-C");
                    argv.Add(FirstAttr(el, "countryIsoCode", "country"));
                    break;

                case "issueDate" or "issdate":
                    argv.Add("-I");
                    argv.Add($"{Attr(el, "year")}-{Attr(el, "month")}-{Attr(el, "day")}");
                    break;

                case "dmTitle" or "dmtitle":
                    EmitTitle(el, argv);
                    break;

                case "pmTitle":
                    argv.Add("-t");
                    argv.Add(el.InnerText);
                    break;

                case "shortPmTitle":
                    argv.Add("-s");
                    argv.Add(el.InnerText);
                    break;

                case "responsiblePartnerCompany":
                    EmitRpc(el, argv);
                    break;

                case "rpc":
                    EmitRpcOld(el, argv);
                    break;

                case "security":
                    argv.Add("-c");
                    argv.Add(FirstAttr(el, "securityClassification", "class"));
                    break;

                case "remarks":
                    if (opts.UseRemarks)
                    {
                        argv.Add("-m");
                        argv.Add(FirstChildText(el, "simplePara", "p"));
                    }
                    break;

                default:
                    // No template of its own: recurse into children, as the
                    // XSLT identity template does.
                    EmitElementOptions(el, opts, argv);
                    break;
            }
        }
    }

    private static void EmitTitle(XmlElement title, List<string> argv)
    {
        XmlElement? techName = FirstChild(title, "techName", "techname");
        if (techName is not null)
        {
            argv.Add("-t");
            argv.Add(techName.InnerText);
        }

        XmlElement? infoName = FirstChild(title, "infoName", "infoname");
        if (infoName is not null)
        {
            argv.Add("-i");
            argv.Add(infoName.InnerText);
        }
        else
        {
            argv.Add("-!");
        }

        XmlElement? variant = FirstChild(title, "infoNameVariant");
        if (variant is not null)
        {
            argv.Add("-V");
            argv.Add(variant.InnerText);
        }
    }

    private static void EmitRpc(XmlElement rpc, List<string> argv)
    {
        if (rpc.HasAttribute("enterpriseCode"))
        {
            argv.Add("-R");
            argv.Add(Attr(rpc, "enterpriseCode"));
        }

        XmlElement? name = FirstChild(rpc, "enterpriseName");
        if (name is not null)
        {
            argv.Add("-r");
            argv.Add(name.InnerText);
        }
    }

    private static void EmitRpcOld(XmlElement rpc, List<string> argv)
    {
        string text = DirectText(rpc);
        if (!string.IsNullOrEmpty(text))
        {
            argv.Add("-R");
            argv.Add(text);
        }
        if (rpc.HasAttribute("rpcname"))
        {
            argv.Add("-r");
            argv.Add(Attr(rpc, "rpcname"));
        }
    }

    /// <summary>
    /// Build the dash-separated code string for a code element, mirroring the
    /// <c>mode="text"</c> templates in <c>dmrl.xsl</c> (both the Issue 4.x+
    /// attribute form and the legacy element form).
    /// </summary>
    private static string CodeToText(XmlElement code)
    {
        return code.LocalName switch
        {
            "pmCode" => PmCodeText(code),
            "commentCode" => CommentCodeText(code),
            "dmlCode" => DmlCodeText(code),
            "dmc" => AveeText(FirstChild(code, "avee") ?? code),
            _ => AveeText(code), // dmCode (and avee passed directly)
        };
    }

    private static string AveeText(XmlElement e)
    {
        var sb = new StringBuilder();
        sb.Append(FirstAttrOrChild(e, "modelIdentCode", "modelic"));
        sb.Append('-');
        sb.Append(FirstAttrOrChild(e, "systemDiffCode", "sdc"));
        sb.Append('-');
        sb.Append(FirstAttrOrChild(e, "systemCode", "chapnum"));
        sb.Append('-');
        sb.Append(FirstAttrOrChild(e, "subSystemCode", "section"));
        sb.Append(FirstAttrOrChild(e, "subSubSystemCode", "subsect"));
        sb.Append('-');
        sb.Append(FirstAttrOrChild(e, "assyCode", "subject"));
        sb.Append('-');
        sb.Append(FirstAttrOrChild(e, "disassyCode", "discode"));
        sb.Append(FirstAttrOrChild(e, "disassyCodeVariant", "discodev"));
        sb.Append('-');
        sb.Append(FirstAttrOrChild(e, "infoCode", "incode"));
        sb.Append(FirstAttrOrChild(e, "infoCodeVariant", "incodev"));
        sb.Append('-');
        sb.Append(FirstAttrOrChild(e, "itemLocationCode", "itemloc"));
        if (e.HasAttribute("learnCode"))
        {
            sb.Append('-');
            sb.Append(Attr(e, "learnCode"));
            sb.Append(Attr(e, "learnEventCode"));
        }
        return sb.ToString();
    }

    private static string PmCodeText(XmlElement e) =>
        $"{Attr(e, "modelIdentCode")}-{Attr(e, "pmIssuer")}-{Attr(e, "pmNumber")}-{Attr(e, "pmVolume")}";

    private static string CommentCodeText(XmlElement e) =>
        $"{Attr(e, "modelIdentCode")}-{Attr(e, "senderIdent")}-{Attr(e, "yearOfDataIssue")}-" +
        $"{Attr(e, "seqNumber")}-{Attr(e, "commentType").ToUpperInvariant()}";

    private static string DmlCodeText(XmlElement e) =>
        $"{Attr(e, "modelIdentCode")}-{Attr(e, "senderIdent")}-{Attr(e, "dmlType").ToUpperInvariant()}-" +
        $"{Attr(e, "yearOfDataIssue")}-{Attr(e, "seqNumber")}";

    // --- Small DOM helpers --------------------------------------------------

    private static bool HasChild(XmlElement parent, string localName) =>
        FirstChild(parent, localName) is not null;

    private static XmlElement? FirstChild(XmlElement parent, params string[] localNames)
    {
        foreach (XmlNode n in parent.ChildNodes)
        {
            if (n is XmlElement el && Array.IndexOf(localNames, el.LocalName) >= 0)
            {
                return el;
            }
        }
        return null;
    }

    private static string FirstChildText(XmlElement parent, params string[] localNames)
    {
        XmlElement? el = FirstChild(parent, localNames);
        return el?.InnerText ?? string.Empty;
    }

    private static string Attr(XmlElement el, string name) => el.GetAttribute(name);

    private static string FirstAttr(XmlElement el, params string[] names)
    {
        foreach (string n in names)
        {
            if (el.HasAttribute(n))
            {
                return el.GetAttribute(n);
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Mirror the XSLT <c>@attr|child</c> unions used in the avee template:
    /// prefer the attribute form, fall back to a child element of the alt name.
    /// </summary>
    private static string FirstAttrOrChild(XmlElement el, string attrName, string childName)
    {
        if (el.HasAttribute(attrName))
        {
            return el.GetAttribute(attrName);
        }
        XmlElement? child = FirstChild(el, childName);
        return child?.InnerText ?? string.Empty;
    }

    /// <summary>Concatenated direct text-node children only (XSLT <c>text()</c>).</summary>
    private static string DirectText(XmlElement el)
    {
        var sb = new StringBuilder();
        foreach (XmlNode n in el.ChildNodes)
        {
            if (n.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                sb.Append(n.Value);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Render a command as a printable shell line (for <c>-s</c>/<c>--commands</c>),
    /// quoting arguments that contain whitespace so the output is copy-pasteable.
    /// </summary>
    private static string FormatCommandLine(string tool, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder("s1kd-");
        sb.Append(tool);
        foreach (string arg in args)
        {
            sb.Append(' ');
            sb.Append(QuoteIfNeeded(arg));
        }
        return sb.ToString();
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (arg.Length == 0)
        {
            return "\"\"";
        }
        bool needsQuote = arg.Any(c => char.IsWhiteSpace(c) || c == '"');
        if (!needsQuote)
        {
            return arg;
        }
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static bool TakeArg(IReadOnlyList<string> args, ref int i, out string? value)
    {
        if (i + 1 >= args.Count)
        {
            value = null;
            return false;
        }
        value = args[++i];
        return true;
    }

    private int ArgError(TextWriter stderr, string opt)
    {
        stderr.WriteLine($"{Name}: ERROR: {opt} requires an argument");
        return ExitBadUsage;
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] <DML>...");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -$, --issue <iss>      Which issue of the spec to use.");
        stdout.WriteLine("  -@, --out <dir>        Output to specified directory.");
        stdout.WriteLine("  -%, --templates <dir>  Custom XML template directory.");
        stdout.WriteLine("  -D, --dmtypes <path>   Specify .dmtypes file name.");
        stdout.WriteLine("  -d, --defaults <path>  Specify .defaults file name.");
        stdout.WriteLine("  -F, --fail             Fail on first error from s1kd-new* commands.");
        stdout.WriteLine("  -f, --overwrite        Overwrite existing CSDB objects.");
        stdout.WriteLine("  -h, -?, --help         Show usage message.");
        stdout.WriteLine("  -m, --use-remarks      Use the remarks for entries in the objects.");
        stdout.WriteLine("  -N, --omit-issue       Omit issue/inwork numbers.");
        stdout.WriteLine("  -q, --quiet            Don't report errors if objects exist.");
        stdout.WriteLine("  -s, --commands         Output s1kd-new* commands only.");
        stdout.WriteLine("  -v, --verbose          Print the names of newly created objects.");
        stdout.WriteLine("      --version          Show version information.");
    }
}
