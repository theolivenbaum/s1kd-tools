using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-refs</c>: list the references (dependencies) found in CSDB
/// objects (dmRef, pmRef, externalPubRef, infoEntityRef/ICN, commentRef, dmlRef,
/// SCORM content package refs, source/repository idents, IPD/CSN refs, fragments).
///
/// Implemented behaviour mirrors the C tool for the "listing" use cases:
///   type selection (-CDEGLPSBKTZY and -H reserved), source-object column
///   (-f/-n), recursive directory scan (-r), matching to CSDB files in a
///   directory (-d), latest matching, unmatched-only (-u), show-unmatched
///   (-a), fully-qualified filenames (default loose match / -m strict),
///   content-only (-c), no-match (-M), ignore/omit issue (-i/-N), list input
///   (-l), include source object (-s), XML report (-x), custom format (-t),
///   remove-deleted (-^), and the unmatched-reference exit code.
///
/// The mutating / advanced modes — update refs (-U/-I/-F), tag unmatched (-X),
/// where-used (-w), hotspot matching (-H/-j/-J), exec (-e) and the
/// non-chapterized IPD SNS construction (-b) — are not ported here and are
/// tracked in todo.md. The corresponding option flags are still parsed so the
/// CLI surface matches, but they fall back to plain listing.
/// </summary>
public sealed class RefsTool : ITool
{
    public string Name => "refs";
    public string Description => "List references in CSDB objects.";
    public string Version => "5.2.2";

    private const int ExitUnmatchedRef = 1;
    private const int ExitBadStdin = 3;
    private const int ExitBadCsnCode = 4;

    private const string FragmentSep = "#";

    // Which kinds of object references will be listed (bit flags, mirroring the
    // SHOW_* macros from the C source).
    [Flags]
    private enum Show
    {
        None = 0,
        Com = 0x0001,
        Dmc = 0x0002,
        Icn = 0x0004,
        Pmc = 0x0008,
        Epr = 0x0010,
        Hot = 0x0020,
        Frg = 0x0040,
        Dml = 0x0080,
        Smc = 0x0100,
        Src = 0x0200,
        Rep = 0x0400,
        Ipd = 0x0800,
        Csn = 0x1000,
        All = Com | Dmc | Icn | Pmc | Epr | Hot | Frg | Dml | Smc | Src | Rep | Ipd | Csn,
    }

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2, Debug = 3 }

    // Output mode selector for matched / unmatched references.
    private enum OutputMode { Plain, Src, SrcLine, Xml, Custom }

    // ----- Runtime options (set from args, then immutable for the run) -----
    private sealed class Options
    {
        public bool ContentOnly;
        public Verbosity Verbosity = Verbosity.Normal;
        public bool MatchReferences = true;
        public bool NoIssue;
        public bool ShowUnmatched;     // -a: print unmatched codes instead of error
        public bool ShowMatched = true; // -u clears this
        public bool Recursive;          // -r recurse directories when matching
        public string Directory = ".";
        public bool IgnoreIss;
        public bool ListSrc;            // -s
        public bool LooseMatch = true;  // -m clears this
        public OutputMode Mode = OutputMode.Plain;
        public string? Format;          // -t
        public bool RemDelete;          // -^
        public bool XmlOutput;          // -x
        public string? FigNumVarFormat = "%"; // -k
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var opts = new Options();
        var inputs = new List<string>();
        Show show = Show.None;
        bool isList = false;
        bool inclSrcFname = false;
        bool inclLineNum = false;

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help": ShowHelp(stdout); return 0;
                case "--version":
                    stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
                    return 0;
                case "-q" or "--quiet": opts.Verbosity--; break;
                case "-v" or "--verbose": opts.Verbosity++; break;
                case "-c" or "--content": opts.ContentOnly = true; break;
                case "-N" or "--omit-issue": opts.NoIssue = true; break;
                case "-M" or "--no-match": opts.MatchReferences = false; opts.ShowUnmatched = true; break;
                case "-a" or "--all": opts.ShowUnmatched = true; break;
                case "-u" or "--unmatched": opts.ShowMatched = false; break;
                case "-i" or "--ignore-issue": opts.IgnoreIss = true; break;
                case "-m" or "--strict-match": opts.LooseMatch = false; break;
                case "-r" or "--recursive": opts.Recursive = true; break;
                case "-l" or "--list": isList = true; break;
                case "-s" or "--include-src": opts.ListSrc = true; break;
                case "-f" or "--filename": inclSrcFname = true; break;
                case "-n" or "--lineno": inclSrcFname = true; inclLineNum = true; break;
                case "-x" or "--xml": opts.XmlOutput = true; break;
                case "-^" or "--remove-deleted": opts.RemDelete = true; break;
                // Object-type selectors.
                case "-C" or "--com": show |= Show.Com; break;
                case "-D" or "--dm": show |= Show.Dmc; break;
                case "-G" or "--icn": show |= Show.Icn; break;
                case "-P" or "--pm": show |= Show.Pmc; break;
                case "-E" or "--epr": show |= Show.Epr; break;
                case "-L" or "--dml": show |= Show.Dml; break;
                case "-S" or "--smc": show |= Show.Smc; break;
                case "-T" or "--fragment": show |= Show.Frg; break;
                case "-Z" or "--source": show |= Show.Src; break;
                case "-Y" or "--repository": show |= Show.Rep; break;
                case "-B" or "--ipd": show |= Show.Ipd; break;
                case "-K" or "--csn": show |= Show.Csn; break;
                case "-H" or "--hotspot": show |= Show.Hot; break;
                case "-d" or "--dir":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -d requires an argument"); return 2; }
                    opts.Directory = args[i];
                    break;
                case "-t" or "--format":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -t requires an argument"); return 2; }
                    opts.Format = args[i];
                    break;
                case "-k" or "--ipd-dcv":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -k requires an argument"); return 2; }
                    opts.FigNumVarFormat = args[i];
                    break;
                case "-b" or "--ipd-sns":
                    // Non-chapterized IPD SNS construction is not ported; consume arg.
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -b requires an argument"); return 2; }
                    break;
                case "-3" or "--externalpubs":
                case "-j" or "--hotspot-xpath":
                case "-J" or "--namespace":
                case "-e" or "--exec":
                    // Options that take an argument but whose feature is not ported.
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: {a} requires an argument"); return 2; }
                    break;
                // Flags whose feature is not ported (parsed for CLI compatibility).
                case "-F" or "--overwrite":
                case "-U" or "--update":
                case "-I" or "--update-issue":
                case "-X" or "--tag-unmatched":
                case "-w" or "--where-used":
                case "-o" or "--output-valid":
                case "-R" or "--recursively":
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return 2;
                    }
                    inputs.Add(a);
                    break;
            }
        }

        // If no type selector given, show all.
        if (show == Show.None)
        {
            show = Show.All;
        }

        // Pick the output mode (precedence mirrors main() in the C tool).
        if (opts.Format != null)
        {
            opts.Mode = OutputMode.Custom;
        }
        else if (opts.XmlOutput)
        {
            opts.Mode = OutputMode.Xml;
        }
        else if (inclSrcFname)
        {
            opts.Mode = inclLineNum ? OutputMode.SrcLine : OutputMode.Src;
        }

        if (opts.XmlOutput)
        {
            stdout.WriteLine("<?xml version=\"1.0\"?>");
            stdout.Write("<results>");
        }

        int unmatched = 0;
        try
        {
            if (inputs.Count > 0)
            {
                foreach (string input in inputs)
                {
                    unmatched += isList
                        ? ListReferencesInList(input, show, opts, stdout, stderr)
                        : ListReferences(input, show, opts, stdout, stderr);
                }
            }
            else if (isList)
            {
                unmatched += ListReferencesInList(null, show, opts, stdout, stderr);
            }
            else
            {
                unmatched += ListReferences("-", show, opts, stdout, stderr);
            }
        }
        catch (BadStdinException)
        {
            stderr.WriteLine($"{Name}: ERROR: stdin does not contain valid XML.");
            return ExitBadStdin;
        }

        if (opts.XmlOutput)
        {
            stdout.Write("</results>\n");
        }

        return unmatched > 0 ? ExitUnmatchedRef : 0;
    }

    private sealed class BadStdinException : Exception { }

    // ----- List input (-l) -----

    private int ListReferencesInList(string? path, Show show, Options opts, TextWriter stdout, TextWriter stderr)
    {
        int unmatched = 0;
        TextReader reader;
        bool dispose = false;
        if (path == null)
        {
            reader = Console.In;
        }
        else
        {
            try
            {
                reader = new StreamReader(path);
                dispose = true;
            }
            catch (IOException)
            {
                stderr.WriteLine($"{Name}: ERROR: Could not read list: {path}");
                return 0;
            }
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Split('\t')[0].TrimEnd('\r', '\n');
                if (trimmed.Length == 0) continue;
                unmatched += ListReferences(trimmed, show, opts, stdout, stderr);
            }
        }
        finally
        {
            if (dispose) reader.Dispose();
        }

        return unmatched;
    }

    // ----- Core listing -----

    private int ListReferences(string path, Show show, Options opts, TextWriter stdout, TextWriter stderr)
    {
        int unmatched = 0;

        if (opts.ListSrc)
        {
            PrintMatched(null, path, path, null, path, opts, stdout);
        }

        XmlDocument doc;
        try
        {
            if (path == "-")
            {
                doc = XmlUtils.ReadStream(Console.OpenStandardInput());
            }
            else
            {
                doc = XmlUtils.ReadDoc(path);
            }
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            if (path == "-")
            {
                throw new BadStdinException();
            }
            return 0;
        }

        if (opts.RemDelete)
        {
            XmlUtils.RemoveDeleteElements(doc);
        }

        XmlNode? contextNode;
        if (opts.ContentOnly)
        {
            contextNode = XmlUtils.XPathFirstNode(doc, null,
                "//content|//dmlContent|//dml|//ddnContent|//delivlst");
        }
        else
        {
            contextNode = doc.DocumentElement;
        }

        if (contextNode == null)
        {
            return 0;
        }

        var nodes = contextNode.SelectNodes(RefsXPath);
        if (nodes != null)
        {
            foreach (XmlNode node in nodes)
            {
                unmatched += PrintReference(node, path, show, opts, stdout, stderr);
            }
        }

        if (opts.Verbosity >= Verbosity.Verbose)
        {
            if (unmatched != 0)
            {
                stderr.WriteLine($"{Name}: FAILURE: Unmatched references in {path}");
            }
            else
            {
                stderr.WriteLine($"{Name}: SUCCESS: No unmatched references in {path}");
            }
        }

        return unmatched;
    }

    // XPath selecting all reference types (mirrors REFS_XPATH).
    private const string RefsXPath =
        ".//dmRef|.//refdm|.//addresdm|" +
        ".//pmRef|.//refpm|" +
        ".//infoEntityRef|.//@infoEntityIdent|.//@boardno|" +
        ".//commentRef|" +
        ".//dmlRef|" +
        ".//externalPubRef|.//reftp|" +
        ".//dispatchFileName|.//ddnfilen|" +
        ".//graphic[hotspot]|" +
        ".//dmRef/@referredFragment|.//refdm/@target|" +
        ".//scormContentPackageRef|" +
        ".//sourceDmIdent|.//sourcePmIdent|.//repositorySourceDmIdent|" +
        ".//catalogSeqNumberRef|.//csnref|" +
        ".//catalogSeqNumberRef/@item|.//catalogSeqNumberRef/@catalogSeqNumberValue|.//@refcsn";

    private int PrintReference(XmlNode node, string src, Show show, Options opts, TextWriter stdout, TextWriter stderr)
    {
        string name = node.Name;
        string? code = null;

        if (Has(show, Show.Dmc) && name is "dmRef" or "refdm" or "addresdm")
            code = GetDmCode(node, opts);
        else if (Has(show, Show.Pmc) && name is "pmRef" or "refpm")
            code = GetPmCode(node, opts);
        else if (Has(show, Show.Smc) && name == "scormContentPackageRef")
            code = GetSmcCode(node, opts);
        else if (Has(show, Show.Icn) && name == "infoEntityRef")
            code = GetIcn(node);
        else if (Has(show, Show.Com) && name == "commentRef")
            code = GetComCode(node);
        else if (Has(show, Show.Dml) && name == "dmlRef")
            code = GetDmlCode(node);
        else if (Has(show, Show.Icn) && name is "infoEntityIdent" or "boardno")
            code = GetIcnAttr(node, opts);
        else if (Has(show, Show.Epr) && name is "externalPubRef" or "reftp")
            code = GetExternalPubCode(node);
        else if (name is "dispatchFileName" or "ddnfilen")
            code = node.InnerText;
        else if (Has(show, Show.Src) && name is "sourceDmIdent" or "sourcePmIdent")
            code = GetSourceIdent(node, opts);
        else if (Has(show, Show.Rep) && name == "repositorySourceDmIdent")
            code = GetSourceIdent(node, opts);
        else if (Has(show, Show.Ipd) && name is "catalogSeqNumberRef" or "csnref")
            code = GetIpdCode(node, opts);
        else if (Has(show, Show.Csn) && name is "item" or "catalogSeqNumberValue" or "refcsn")
            return GetCsnItem(node, src, opts, stdout, stderr);
        else
            return 0;

        if (code == null)
        {
            return 0;
        }

        string? fname = FindObjectFile(code, opts);
        if (fname != null)
        {
            if (opts.ShowMatched)
            {
                PrintMatched(node, src, code, null, fname, opts, stdout);
            }
            return 0;
        }

        // Unmatched.
        if (opts.ShowUnmatched)
        {
            PrintMatched(node, src, code, null, null, opts, stdout);
        }
        else if (opts.Verbosity >= Verbosity.Normal)
        {
            PrintUnmatched(node, src, code, null, opts, stdout, stderr);
        }

        return 1;
    }

    // ----- Fragment matching (-T) -----

    private int GetCsnItem(XmlNode node, string src, Options opts, TextWriter stdout, TextWriter stderr)
    {
        // CSN item: the parent is the catalogSeqNumberRef/csnref. We list the
        // item code; full IPD-item matching against the target IPD is not
        // ported, so we treat the item as unmatched-but-listable using the same
        // print path as -B for the IPD code.
        XmlNode? csnref = node is XmlAttribute attr ? attr.OwnerElement : node.ParentNode;
        if (csnref == null) return 0;
        string code = GetIpdCode(csnref, opts);

        string id = "Item " +
            (GetAttr(csnref, "item") ?? "") +
            (GetAttr(csnref, "itemVariant") ?? "");
        string? isn = GetAttr(node, "itemSeqNumberValue") ?? GetAttr(node, "refisn");
        if (isn != null) id += " ISN " + isn;

        string? fname = FindObjectFile(code, opts);
        if (fname != null)
        {
            if (opts.ShowMatched)
            {
                PrintMatched(node, src, code, id, fname, opts, stdout);
            }
            return 0;
        }

        if (opts.ShowUnmatched)
        {
            PrintMatched(node, src, code, id, null, opts, stdout);
        }
        else if (opts.Verbosity >= Verbosity.Normal)
        {
            PrintUnmatched(node, src, code, id, opts, stdout, stderr);
        }
        return 1;
    }

    // ----- File matching -----

    private string? FindObjectFile(string code, Options opts)
    {
        if (!opts.MatchReferences)
        {
            return null;
        }

        string? best = FindCsdbObject(opts.Directory, code, opts.Recursive);
        if (best == null)
        {
            return null;
        }
        if (!opts.LooseMatch && !ExactMatch(best, code))
        {
            return null;
        }
        return best;
    }

    // Mirror of find_csdb_object: scan a directory (optionally recursively) for
    // the highest-sorting file whose base name matches the code (case-insensitive,
    // '?' wildcard, prefix match).
    private static string? FindCsdbObject(string dir, string code, bool recursive)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }

        string prefix = dir == "." ? "" : dir.EndsWith('/') ? dir : dir + "/";
        string? best = null;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch (IOException)
        {
            return null;
        }

        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            string cpath = prefix + name;

            if (recursive && Directory.Exists(cpath) && name is not ("." or ".."))
            {
                string? candidate = FindCsdbObject(cpath, code, true);
                if (candidate != null && (best == null || CodeCmp(candidate, best) > 0))
                {
                    best = candidate;
                }
            }
            else if (File.Exists(cpath) && Csdb.StrMatch(code, name))
            {
                if (best == null || CodeCmp(cpath, best) > 0)
                {
                    best = cpath;
                }
            }
        }

        return best;
    }

    // Compare two paths by base name, case-insensitively (mirrors codecmp).
    private static int CodeCmp(string a, string b) =>
        string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);

    // Mirror of exact_match: the code must run up to the extension dot.
    private static bool ExactMatch(string path, string code)
    {
        string baseName = Path.GetFileName(path);
        int dot = baseName.LastIndexOf('.');
        return dot == code.Length;
    }

    // ----- Output helpers -----

    private void PrintMatched(XmlNode? node, string src, string code, string? frag, string? fname,
        Options opts, TextWriter stdout)
    {
        string display = fname ?? code;
        switch (opts.Mode)
        {
            case OutputMode.Src:
                stdout.Write(frag != null
                    ? $"{src}: {display}{FragmentSep}{frag}\n"
                    : $"{src}: {display}\n");
                break;
            case OutputMode.SrcLine:
                stdout.Write(frag != null
                    ? $"{src} ({LineNo(node)}): {display}{FragmentSep}{frag}\n"
                    : $"{src} ({LineNo(node)}): {display}\n");
                break;
            case OutputMode.Xml:
                PrintMatchedXml(node, src, code, frag, fname, "found", stdout);
                break;
            case OutputMode.Custom:
                ProcessFormatStr(stdout, node, src, code, frag, fname, opts.Format!);
                break;
            default:
                stdout.Write(frag != null
                    ? $"{display}{FragmentSep}{frag}\n"
                    : $"{display}\n");
                break;
        }
    }

    private void PrintUnmatched(XmlNode? node, string src, string code, string? frag,
        Options opts, TextWriter stdout, TextWriter stderr)
    {
        switch (opts.Mode)
        {
            case OutputMode.Src:
                stderr.Write(frag != null
                    ? $"{Name}: ERROR: {src}: Unmatched reference: {code}{FragmentSep}{frag}\n"
                    : $"{Name}: ERROR: {src}: Unmatched reference: {code}\n");
                break;
            case OutputMode.SrcLine:
                stderr.Write(frag != null
                    ? $"{Name}: ERROR: {src} ({LineNo(node)}): Unmatched reference: {code}{FragmentSep}{frag}\n"
                    : $"{Name}: ERROR: {src} ({LineNo(node)}): Unmatched reference: {code}\n");
                break;
            case OutputMode.Xml:
                // The C tool's printUnmatchedXml uses printf -> stdout.
                PrintMatchedXml(node, src, code, frag, null, "missing", stdout);
                break;
            case OutputMode.Custom:
                stderr.Write($"{Name}: ERROR: Unmatched reference: ");
                ProcessFormatStr(stderr, node, src, code, frag, null, opts.Format!);
                break;
            default:
                stderr.Write(frag != null
                    ? $"{Name}: ERROR: Unmatched reference: {code}{FragmentSep}{frag}\n"
                    : $"{Name}: ERROR: Unmatched reference: {code}\n");
                break;
        }
    }

    private static void PrintMatchedXml(XmlNode? node, string src, string code, string? frag, string? fname,
        string tag, TextWriter w)
    {
        w.Write($"<{tag}>");
        w.Write("<ref>");
        if (node != null)
        {
            XmlNode outer = node.NodeType == XmlNodeType.Attribute
                ? ((XmlAttribute)node).OwnerElement ?? node
                : node;
            w.Write(outer.OuterXml);
            w.Write('\n');
        }
        w.Write("</ref>");
        string xpath = node != null ? XmlUtils.XPathOf(node) : "";
        w.Write($"<source line=\"{LineNo(node)}\" xpath=\"{Esc(xpath)}\">{Esc(src)}</source>");
        w.Write($"<code>{Esc(code)}</code>");
        if (frag != null) w.Write($"<fragment>{Esc(frag)}</fragment>");
        if (fname != null) w.Write($"<filename>{Esc(fname)}</filename>");
        w.Write($"</{tag}>");
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static long LineNo(XmlNode? node)
    {
        // System.Xml does not retain source line numbers by default; the C tool
        // uses libxml2's xmlGetLineNo. We report 0 (this only affects the -n and
        // -x output formats).
        return node is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
    }

    private static void ProcessFormatStr(TextWriter f, XmlNode? node, string src, string code,
        string? frag, string? fname, string format)
    {
        string display = fname ?? code;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '%')
            {
                if (i + 1 < format.Length && format[i + 1] == '%')
                {
                    f.Write('%');
                    i++;
                    continue;
                }
                int end = format.IndexOf('%', i + 1);
                if (end < 0) break;
                string key = format.Substring(i + 1, end - i - 1);
                switch (key)
                {
                    case "src": f.Write(src); break;
                    case "ref": f.Write(frag != null ? $"{display}{FragmentSep}{frag}" : display); break;
                    case "code": f.Write(code); break;
                    case "fragment": f.Write(frag ?? ""); break;
                    case "file": if (fname != null) f.Write(fname); break;
                    case "line": f.Write(LineNo(node)); break;
                    case "xpath": f.Write(node != null ? XmlUtils.XPathOf(node) : ""); break;
                }
                i = end;
            }
            else if (c == '\\' && i + 1 < format.Length)
            {
                char n = format[i + 1];
                switch (n)
                {
                    case 'n': f.Write('\n'); i++; break;
                    case 't': f.Write('\t'); i++; break;
                    case '0': f.Write('\0'); i++; break;
                    default: f.Write(c); break;
                }
            }
            else
            {
                f.Write(c);
            }
        }
        f.Write('\n');
    }

    private static bool Has(Show show, Show flag) => (show & flag) == flag;

    // ----- Code construction helpers (mirror getDmCode/getPmCode/etc.) -----

    private static string? GetAttr(XmlNode node, string name) =>
        node is XmlElement el && el.HasAttribute(name) ? el.GetAttribute(name) : null;

    private static string? FirstValue(XmlNode? context, string xpath)
    {
        if (context == null) return null;
        XmlNode? n = context.SelectSingleNode(xpath);
        if (n == null) return null;
        return n.NodeType == XmlNodeType.Attribute ? n.Value : n.InnerText;
    }

    private static XmlNode? FirstNode(XmlNode? context, string xpath) =>
        context?.SelectSingleNode(xpath);

    private string GetDmCode(XmlNode dmRef, Options opts)
    {
        XmlNode? identExtension = FirstNode(dmRef, "dmRefIdent/identExtension|dmcextension");
        XmlNode? dmCode = FirstNode(dmRef, "dmRefIdent/dmCode|dmc/avee|avee");
        XmlNode? issueInfo = opts.IgnoreIss ? null : FirstNode(dmRef, "dmRefIdent/issueInfo|issno");
        XmlNode? language = FirstNode(dmRef, "dmRefIdent/language|language");

        var sb = new StringBuilder();

        if (identExtension != null)
        {
            string ep = FirstValue(identExtension, "@extensionProducer|dmeproducer") ?? "";
            string ec = FirstValue(identExtension, "@extensionCode|dmecode") ?? "";
            sb.Append("DME-").Append(ep).Append('-').Append(ec).Append('-');
        }
        else
        {
            sb.Append("DMC-");
        }

        string? modelIdentCode = FirstValue(dmCode, "@modelIdentCode|modelic");
        if (modelIdentCode != null)
        {
            sb.Append(modelIdentCode).Append('-');
            sb.Append(FirstValue(dmCode, "@systemDiffCode|sdc") ?? "").Append('-');
            sb.Append(FirstValue(dmCode, "@systemCode|chapnum") ?? "").Append('-');
            sb.Append(FirstValue(dmCode, "@subSystemCode|section") ?? "");
            sb.Append(FirstValue(dmCode, "@subSubSystemCode|subsect") ?? "").Append('-');
            sb.Append(FirstValue(dmCode, "@assyCode|subject") ?? "").Append('-');
            sb.Append(FirstValue(dmCode, "@disassyCode|discode") ?? "");
            sb.Append(FirstValue(dmCode, "@disassyCodeVariant|discodev") ?? "").Append('-');
            sb.Append(FirstValue(dmCode, "@infoCode|incode") ?? "");
            sb.Append(FirstValue(dmCode, "@infoCodeVariant|incodev") ?? "").Append('-');
            sb.Append(FirstValue(dmCode, "@itemLocationCode|itemloc") ?? "");

            string? learnCode = FirstValue(dmCode, "@learnCode");
            if (learnCode != null)
            {
                sb.Append('-').Append(learnCode);
                sb.Append(FirstValue(dmCode, "@learnEventCode") ?? "");
            }
        }

        AppendIssueAndLanguage(sb, issueInfo, language, opts, defaultInwork: "00");
        return sb.ToString();
    }

    private string GetPmCode(XmlNode pmRef, Options opts)
    {
        XmlNode? identExtension = FirstNode(pmRef, "pmRefIdent/identExtension");
        XmlNode? pmCode = FirstNode(pmRef, "pmRefIdent/pmCode|pmc");
        XmlNode? issueInfo = opts.IgnoreIss ? null : FirstNode(pmRef, "pmRefIdent/issueInfo|issno");
        XmlNode? language = FirstNode(pmRef, "pmRefIdent/language|language");

        var sb = new StringBuilder();
        if (identExtension != null)
        {
            string ep = GetAttr(identExtension, "extensionProducer") ?? "";
            string ec = GetAttr(identExtension, "extensionCode") ?? "";
            sb.Append("PME-").Append(ep).Append('-').Append(ec).Append('-');
        }
        else
        {
            sb.Append("PMC-");
        }

        sb.Append(FirstValue(pmCode, "@modelIdentCode|modelic") ?? "").Append('-');
        sb.Append(FirstValue(pmCode, "@pmIssuer|pmissuer") ?? "").Append('-');
        sb.Append(FirstValue(pmCode, "@pmNumber|pmnumber") ?? "").Append('-');
        sb.Append(FirstValue(pmCode, "@pmVolume|pmvolume") ?? "");

        AppendIssueAndLanguage(sb, issueInfo, language, opts, defaultInwork: null);
        return sb.ToString();
    }

    private string GetSmcCode(XmlNode smcRef, Options opts)
    {
        XmlNode? identExtension = FirstNode(smcRef, "scormContentPackageRefIdent/identExtension");
        XmlNode? smcCode = FirstNode(smcRef, "scormContentPackageRefIdent/scormContentPackageCode");
        XmlNode? issueInfo = opts.IgnoreIss ? null : FirstNode(smcRef, "scormContentPackageRefIdent/issueInfo");
        XmlNode? language = FirstNode(smcRef, "scormContentPackageRefIdent/language");

        var sb = new StringBuilder();
        if (identExtension != null)
        {
            string? ep = GetAttr(identExtension, "extensionProducer");
            string? ec = GetAttr(identExtension, "extensionCode");
            sb.Append("SME-");
            if (ep != null && ec != null)
            {
                sb.Append(ep).Append('-').Append(ec).Append('-');
            }
        }
        else
        {
            sb.Append("SMC-");
        }

        string? mic = GetAttr(smcCode!, "modelIdentCode");
        string? iss = GetAttr(smcCode!, "scormContentPackageIssuer");
        string? num = GetAttr(smcCode!, "scormContentPackageNumber");
        string? vol = GetAttr(smcCode!, "scormContentPackageVolume");
        if (smcCode != null && mic != null && iss != null && num != null && vol != null)
        {
            sb.Append(mic).Append('-').Append(iss).Append('-').Append(num).Append('-').Append(vol);
        }

        AppendIssueAndLanguage(sb, issueInfo, language, opts, defaultInwork: null, requireBoth: true);
        return sb.ToString();
    }

    private void AppendIssueAndLanguage(StringBuilder sb, XmlNode? issueInfo, XmlNode? language,
        Options opts, string? defaultInwork, bool requireBoth = false)
    {
        if (!opts.NoIssue)
        {
            if (issueInfo != null)
            {
                string? issueNumber = FirstValue(issueInfo, "@issueNumber|@issno");
                string? inWork = FirstValue(issueInfo, "@inWork|@inwork");
                if (inWork == null && defaultInwork != null)
                {
                    inWork = defaultInwork;
                }
                if (!requireBoth || (issueNumber != null && inWork != null))
                {
                    sb.Append('_').Append(issueNumber ?? "").Append('-').Append(inWork ?? "");
                }
            }
            else if (language != null)
            {
                sb.Append("_???-??");
            }
        }

        if (language != null)
        {
            string? lang = FirstValue(language, "@languageIsoCode|@language");
            string? country = FirstValue(language, "@countryIsoCode|@country");
            if (!requireBoth || (lang != null && country != null))
            {
                sb.Append('_').Append((lang ?? "").ToUpperInvariant()).Append('-').Append(country ?? "");
            }
        }
    }

    private string GetSourceIdent(XmlNode sourceIdent, Options opts)
    {
        // Wrap the ident in a synthetic dmRef/pmRef and reuse the code builders.
        var doc = new XmlDocument();
        XmlNode imported = doc.ImportNode(sourceIdent, true);
        bool isPm = sourceIdent.Name == "sourcePmIdent";

        XmlElement reff = doc.CreateElement(isPm ? "pmRef" : "dmRef");
        doc.AppendChild(reff);
        XmlElement ident = doc.CreateElement(isPm ? "pmRefIdent" : "dmRefIdent");

        // Copy attributes, then a snapshot of the children (cloning so we do not
        // mutate the collection we are iterating).
        if (imported.Attributes != null)
        {
            foreach (XmlAttribute attr in imported.Attributes)
            {
                ident.SetAttribute(attr.Name, attr.Value);
            }
        }
        foreach (XmlNode child in imported.ChildNodes.Cast<XmlNode>().ToList())
        {
            ident.AppendChild(child.CloneNode(true));
        }

        reff.AppendChild(ident);
        return isPm ? GetPmCode(reff, opts) : GetDmCode(reff, opts);
    }

    private static string GetIcn(XmlNode reff) => GetAttr(reff, "infoEntityRefIdent") ?? "";

    private string GetIcnAttr(XmlNode reff, Options opts)
    {
        // The C code resolves the ICN entity URI; System.Xml entity handling
        // differs, so we use the literal content (matching the common case where
        // the ICN code is stored directly).
        string dst = reff.NodeType == XmlNodeType.Attribute ? reff.Value ?? "" : reff.InnerText;

        if (opts.IgnoreIss)
        {
            int e = dst.LastIndexOf('-');
            int s = e - 3;
            if (e >= 0 && s >= 0)
            {
                dst = dst.Substring(0, s);
            }
        }
        return dst;
    }

    private static string GetComCode(XmlNode reff)
    {
        XmlNode? commentCode = FirstNode(reff, "commentRefIdent/commentCode");
        XmlNode? language = FirstNode(reff, "commentRefIdent/language");

        var sb = new StringBuilder("COM-");
        sb.Append(GetAttr(commentCode!, "modelIdentCode") ?? "").Append('-');
        sb.Append(GetAttr(commentCode!, "senderIdent") ?? "").Append('-');
        sb.Append(GetAttr(commentCode!, "yearOfDataIssue") ?? "").Append('-');
        sb.Append(GetAttr(commentCode!, "seqNumber") ?? "").Append('-');
        sb.Append(GetAttr(commentCode!, "commentType") ?? "");

        if (language != null)
        {
            string lang = (GetAttr(language, "languageIsoCode") ?? "").ToUpperInvariant();
            string country = GetAttr(language, "countryIsoCode") ?? "";
            sb.Append('_').Append(lang).Append('-').Append(country);
        }
        return sb.ToString();
    }

    private static string GetDmlCode(XmlNode reff)
    {
        XmlNode? dmlCode = FirstNode(reff, "dmlRefIdent/dmlCode");
        XmlNode? issueInfo = FirstNode(reff, "dmlRefIdent/issueInfo");

        var sb = new StringBuilder("DML-");
        sb.Append(GetAttr(dmlCode!, "modelIdentCode") ?? "").Append('-');
        sb.Append(GetAttr(dmlCode!, "senderIdent") ?? "").Append('-');
        sb.Append((GetAttr(dmlCode!, "dmlType") ?? "").ToUpperInvariant()).Append('-');
        sb.Append(GetAttr(dmlCode!, "yearOfDataIssue") ?? "").Append('-');
        sb.Append(GetAttr(dmlCode!, "seqNumber") ?? "");

        if (issueInfo != null)
        {
            sb.Append('_').Append(GetAttr(issueInfo, "issueNumber") ?? "");
            sb.Append('-').Append(GetAttr(issueInfo, "inWork") ?? "");
        }
        return sb.ToString();
    }

    private static string GetExternalPubCode(XmlNode reff)
    {
        XmlNode? code = FirstNode(reff,
            "externalPubRefIdent/externalPubCode|externalPubRefIdent/externalPubTitle|pubcode");
        return code != null ? code.InnerText : reff.InnerText;
    }

    // IPD / CSN code construction. Full chapterized-CSN-to-DMC resolution is only
    // performed when all SNS attributes are present on the ref itself; the
    // non-chapterized IPD SNS construction (-b) is not ported.
    private string GetIpdCode(XmlNode reff, Options opts)
    {
        string? csnValue = FirstValue(reff, "@catalogSeqNumberValue|@refcsn");

        string? mic, sdc, sys, sub, subsub, assy, fig, figvar, ilc;

        if (csnValue != null)
        {
            mic = null;
            sdc = null;
            ilc = null;
            ParseCsnValue(csnValue, out sys, out sub, out subsub, out assy, out fig, out figvar, out _, out _);
        }
        else
        {
            mic = GetAttr(reff, "modelIdentCode");
            sdc = GetAttr(reff, "systemDiffCode");
            sys = GetAttr(reff, "systemCode");
            sub = GetAttr(reff, "subSystemCode");
            subsub = GetAttr(reff, "subSubSystemCode");
            assy = GetAttr(reff, "assyCode");
            fig = GetAttr(reff, "figureNumber");
            figvar = GetAttr(reff, "figureNumberVariant");
            ilc = GetAttr(reff, "itemLocationCode");
        }

        // Inherit model/systemDiff from the containing DM for old-style CSNs.
        if (csnValue != null)
        {
            XmlNode? dmCode = FirstNode(reff,
                "ancestor::dmodule/identAndStatusSection/dmAddress/dmIdent/dmCode|ancestor::dmodule/idstatus/dmaddres/dmc/avee");
            if (dmCode != null)
            {
                mic ??= FirstValue(dmCode, "@modelIdentCode|modelic");
                sdc ??= FirstValue(dmCode, "@systemDiffCode|sdc");
                ilc ??= "?";
            }
        }

        if (mic != null && sdc != null && sys != null && sub != null && subsub != null && assy != null && fig != null)
        {
            figvar ??= "0";
            string dcv = FormatFigNumVar(figvar, opts.FigNumVarFormat ?? "%");
            ilc ??= "?";

            var sb = new StringBuilder("DMC-");
            sb.Append(mic).Append('-').Append(sdc).Append('-').Append(sys).Append('-');
            sb.Append(sub).Append(subsub).Append('-').Append(assy).Append('-');
            sb.Append(fig).Append(dcv).Append('-').Append("941").Append('A').Append('-').Append(ilc);
            return sb.ToString();
        }

        // Generic IPD figure name.
        return "Fig " + (fig ?? "??") + (figvar ?? "");
    }

    private static string FormatFigNumVar(string figureNumberVariant, string format)
    {
        var sb = new StringBuilder();
        foreach (char c in format)
        {
            sb.Append(c == '%' ? (figureNumberVariant.Length > 0 ? figureNumberVariant[0] : '%') : c);
        }
        return sb.ToString();
    }

    // Parse an old (< 4.1) style CSN reference value into its components.
    private static void ParseCsnValue(string csn,
        out string? systemCode, out string? subSystemCode, out string? subSubSystemCode,
        out string? assyCode, out string? figureNumber, out string? figureNumberVariant,
        out string? item, out string? itemVariant)
    {
        systemCode = subSystemCode = subSubSystemCode = assyCode = null;
        figureNumber = figureNumberVariant = item = itemVariant = null;

        // Field widths by total length: (system, assy).
        int sysLen, assyLen;
        switch (csn.Length)
        {
            case 16: sysLen = 3; assyLen = 4; break;
            case 15: sysLen = 2; assyLen = 4; break;
            case 14: sysLen = 3; assyLen = 2; break;
            case 13: sysLen = 2; assyLen = 2; break;
            default: return;
        }

        int p = 0;
        string Take(int n) { string s = csn.Substring(p, n); p += n; return s; }
        static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        systemCode = Blank(Take(sysLen));
        subSystemCode = Blank(Take(1));
        subSubSystemCode = Blank(Take(1));
        assyCode = Blank(Take(assyLen));
        figureNumber = Take(2);          // always kept (matches C: not blanked)
        figureNumberVariant = Blank(Take(1));
        item = Take(3);
        itemVariant = Blank(Take(1));
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-aBCcDEfGHiKLlMmNnqrSsTuvxYZ^h?] [-b <SNS>] [-d <dir>] [-t <fmt>] [-k <pattern>] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -a, --all              Print unmatched codes.");
        stdout.WriteLine("  -B, --ipd              List IPD references.");
        stdout.WriteLine("  -C, --com              List comment references.");
        stdout.WriteLine("  -c, --content          Only show references in content section.");
        stdout.WriteLine("  -D, --dm               List data module references.");
        stdout.WriteLine("  -d, --dir <dir>        Directory to search for matches in.");
        stdout.WriteLine("  -E, --epr              List external pub refs.");
        stdout.WriteLine("  -f, --filename         Print the source filename for each reference.");
        stdout.WriteLine("  -G, --icn              List ICN references.");
        stdout.WriteLine("  -i, --ignore-issue     Ignore issue info when matching.");
        stdout.WriteLine("  -K, --csn              List CSN references.");
        stdout.WriteLine("  -L, --dml              List DML references.");
        stdout.WriteLine("  -l, --list             Treat input as list of CSDB objects.");
        stdout.WriteLine("  -M, --no-match         Do not attempt to match references to CSDB objects.");
        stdout.WriteLine("  -m, --strict-match     Be more strict when matching filenames of objects.");
        stdout.WriteLine("  -N, --omit-issue       Assume filenames omit issue info.");
        stdout.WriteLine("  -n, --lineno           Print the source filename and line number for each reference.");
        stdout.WriteLine("  -P, --pm               List publication module references.");
        stdout.WriteLine("  -q, --quiet            Quiet mode.");
        stdout.WriteLine("  -r, --recursive        Search for matches in directories recursively.");
        stdout.WriteLine("  -S, --smc              List SCORM content package references.");
        stdout.WriteLine("  -s, --include-src      Include the source object as a reference.");
        stdout.WriteLine("  -T, --fragment         List referred fragments in other DMs.");
        stdout.WriteLine("  -t, --format <fmt>     The format to use when printing references.");
        stdout.WriteLine("  -u, --unmatched        Show only unmatched references.");
        stdout.WriteLine("  -v, --verbose          Verbose output.");
        stdout.WriteLine("  -x, --xml              Output XML report.");
        stdout.WriteLine("  -Y, --repository       List repository source DMs.");
        stdout.WriteLine("  -Z, --source           List source DM or PM.");
        stdout.WriteLine("  -^, --remove-deleted   List refs with elements marked as \"delete\" removed.");
        stdout.WriteLine("      --version          Show version information.");
        stdout.WriteLine("  <object>               CSDB object to list references in.");
    }
}
