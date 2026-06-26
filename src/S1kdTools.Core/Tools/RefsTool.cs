using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-refs</c>: list the references (dependencies) found in CSDB
/// objects (dmRef, pmRef, externalPubRef, infoEntityRef/ICN, commentRef, dmlRef,
/// SCORM content package refs, source/repository idents, IPD/CSN refs, fragments).
///
/// Implemented behaviour mirrors the C tool for the "listing" use cases:
///   type selection (-CDEGLPSBKTZY), source-object column (-f/-n), recursive
///   directory scan (-r), matching to CSDB files in a directory (-d), latest
///   matching, unmatched-only (-u), show-unmatched (-a), fully-qualified
///   filenames (default loose match / -m strict), content-only (-c), no-match
///   (-M), ignore/omit issue (-i/-N), list input (-l), include source object
///   (-s), XML report (-x), custom format (-t), remove-deleted (-^).
///
/// Mutating / advanced modes now ported:
///   update refs (-U / -I) and overwrite (-F), tag unmatched (-X), output valid
///   tree (-o), where-used (-w) and list-recursively (-R), and .externalpubs
///   lookup (-3) for externalPubRef resolution.
///
/// The non-chapterized IPD SNS construction (-b) is now ported: missing SNS
/// components on a CSN/IPD reference are filled in from the supplied SNS (or
/// inherited from the containing DM when the SNS is "-"), so non-chapterized CSN
/// refs resolve to an IPD DMC.
///
/// Still documented as partial: hotspot matching (-H/-j/-J) and exec (-e). Their
/// option flags are parsed for CLI compatibility but the features fall back to
/// plain listing. ICN entity rewriting on update (-U of an infoEntityIdent) is
/// not performed because System.Xml's entity handling differs from libxml2.
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

        // Objects relevant to -w (where-used) mode (mirrors SHOW_WHERE_USED).
        WhereUsed = Com | Dmc | Pmc | Dml | Smc | Icn | Src | Rep | Ipd | Epr,
    }

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2, Debug = 3 }

    // Output mode selector for matched / unmatched references.
    private enum OutputMode { Plain, Src, SrcLine, Xml, Custom, WhereUsed }

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
        public bool ListRecursively;    // -R
        public bool LooseMatch = true;  // -m clears this
        public OutputMode Mode = OutputMode.Plain;
        public string? Format;          // -t
        public bool RemDelete;          // -^
        public bool XmlOutput;          // -x
        public string? FigNumVarFormat = "%"; // -k

        // Non-chapterized IPD SNS (-b). When set, missing SNS components on a
        // CSN/IPD reference are filled in from this SNS (or, when the SNS is
        // "-", inherited from the containing data module).
        public bool NonChapIpdSns;
        public string NonChapIpdSystemCode = "";
        public string NonChapIpdSubSystemCode = "";
        public string NonChapIpdSubSubSystemCode = "";
        public string NonChapIpdAssyCode = "";

        // Mutating modes.
        public bool UpdateRefs;         // -U
        public bool UpdateRefIdent;     // -I (implies UpdateRefs + IgnoreIss)
        public bool OverwriteUpdated;   // -F
        public bool TagUnmatched;       // -X
        public bool OutputTree;         // -o
        public bool FindUsed;           // -w

        // .externalpubs document (-3 / autodiscovered).
        public XmlDocument? ExternalPubs;

        // Recursion bookkeeping for -R (avoid loops).
        public readonly HashSet<string> ListedFiles = new(StringComparer.Ordinal);
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var opts = new Options();
        var inputs = new List<string>();
        Show show = Show.None;
        bool isList = false;
        bool inclSrcFname = false;
        bool inclLineNum = false;
        string? extPubsFname = null;

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
                case "-R" or "--recursively": opts.ListRecursively = true; break;
                case "-l" or "--list": isList = true; break;
                case "-s" or "--include-src": opts.ListSrc = true; break;
                case "-f" or "--filename": inclSrcFname = true; break;
                case "-n" or "--lineno": inclSrcFname = true; inclLineNum = true; break;
                case "-x" or "--xml": opts.XmlOutput = true; break;
                case "-^" or "--remove-deleted": opts.RemDelete = true; break;
                // Mutating modes.
                case "-U" or "--update": opts.UpdateRefs = true; break;
                case "-I" or "--update-issue":
                    opts.UpdateRefs = true;
                    opts.IgnoreIss = true;
                    opts.UpdateRefIdent = true;
                    break;
                case "-F" or "--overwrite": opts.OverwriteUpdated = true; break;
                case "-X" or "--tag-unmatched": opts.TagUnmatched = true; break;
                case "-o" or "--output-valid": opts.OutputTree = true; break;
                case "-w" or "--where-used": opts.FindUsed = true; break;
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
                case "-3" or "--externalpubs":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: {a} requires an argument"); return 2; }
                    extPubsFname = args[i];
                    break;
                case "-b" or "--ipd-sns":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -b requires an argument"); return 2; }
                    if (!ReadNonChapIpdSns(args[i], opts, stderr)) { return ExitBadCsnCode; }
                    opts.NonChapIpdSns = true;
                    break;
                case "-j" or "--hotspot-xpath":
                case "-J" or "--namespace":
                case "-e" or "--exec":
                    // Options that take an argument but whose feature is not ported.
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: {a} requires an argument"); return 2; }
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

        // Load the .externalpubs config (explicit -3 file, or autodiscovered).
        if (extPubsFname != null)
        {
            opts.ExternalPubs = TryReadDoc(extPubsFname);
        }
        else if (Csdb.FindConfig(Csdb.ExternalPubsFileName, out string foundExtPubs))
        {
            opts.ExternalPubs = TryReadDoc(foundExtPubs);
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
        else if (opts.FindUsed)
        {
            // -w prints the source object path for each match (printMatchedWhereUsed).
            opts.Mode = OutputMode.WhereUsed;
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
                    unmatched += DispatchInput(input, isList, show, opts, stdout, stderr);
                }
            }
            else
            {
                unmatched += DispatchInput(isList ? null : "-", isList, show, opts, stdout, stderr);
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

    private int DispatchInput(string? input, bool isList, Show show, Options opts, TextWriter stdout, TextWriter stderr)
    {
        if (isList)
        {
            return opts.FindUsed
                ? ListWhereUsedList(input, show, opts, stdout, stderr)
                : ListReferencesInList(input, show, opts, stdout, stderr);
        }
        return opts.FindUsed
            ? ListWhereUsed(input!, show, opts, stdout, stderr)
            : ListReferences(input!, show, null, Show.None, opts, stdout, stderr);
    }

    private sealed class BadStdinException : Exception { }

    private static XmlDocument? TryReadDoc(string path)
    {
        try
        {
            return XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ----- List input (-l) -----

    private int ListReferencesInList(string? path, Show show, Options opts, TextWriter stdout, TextWriter stderr)
    {
        int unmatched = 0;
        foreach (string trimmed in ReadList(path, stderr))
        {
            unmatched += ListReferences(trimmed, show, null, Show.None, opts, stdout, stderr);
        }
        return unmatched;
    }

    private int ListWhereUsedList(string? path, Show show, Options opts, TextWriter stdout, TextWriter stderr)
    {
        int unmatched = 0;
        foreach (string trimmed in ReadList(path, stderr))
        {
            unmatched += ListWhereUsed(trimmed, show, opts, stdout, stderr);
        }
        return unmatched;
    }

    private IEnumerable<string> ReadList(string? path, TextWriter stderr)
    {
        TextReader reader;
        bool dispose = false;
        if (path == null)
        {
            reader = Console.In;
        }
        else
        {
            StreamReader sr;
            try
            {
                sr = new StreamReader(path);
            }
            catch (IOException)
            {
                stderr.WriteLine($"{Name}: ERROR: Could not read list: {path}");
                yield break;
            }
            reader = sr;
            dispose = true;
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Split('\t')[0].TrimEnd('\r', '\n');
                if (trimmed.Length == 0) continue;
                yield return trimmed;
            }
        }
        finally
        {
            if (dispose) reader.Dispose();
        }
    }

    // ----- Core listing -----

    private int ListReferences(string path, Show show, string? targetRef, Show targetShow,
        Options opts, TextWriter stdout, TextWriter stderr)
    {
        int unmatched = 0;

        // In recursive (-R) mode, track which files have been listed to avoid
        // infinite loops. In -w mode this is handled by ListWhereUsed.
        if (opts.ListRecursively && targetRef == null)
        {
            if (!opts.ListedFiles.Add(path))
            {
                return 0;
            }
        }

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

        // Make a copy of the tree before any extra processing (for -o).
        XmlDocument? validTree = null;
        if (opts.OutputTree)
        {
            validTree = (XmlDocument)doc.CloneNode(true);
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

        if (contextNode != null)
        {
            // Snapshot the matched nodes first: updating/tagging mutates the DOM,
            // so we must not enumerate a live node list while editing it.
            var nodes = contextNode.SelectNodes(RefsXPath);
            if (nodes != null)
            {
                var snapshot = nodes.Cast<XmlNode>().ToList();
                foreach (XmlNode node in snapshot)
                {
                    unmatched += PrintReference(node, path, show, targetRef, targetShow, opts, stdout, stderr);
                }
            }
        }

        // Write valid CSDB object to stdout when there were no unmatched refs.
        if (opts.OutputTree && validTree != null)
        {
            if (unmatched == 0)
            {
                WriteDoc(validTree, stdout);
            }
        }

        // If the object was modified by updating matched refs or tagging
        // unmatched refs, write the changes.
        if (opts.UpdateRefs || opts.TagUnmatched)
        {
            if (opts.OverwriteUpdated && path != "-")
            {
                XmlUtils.SaveDoc(doc, path);
            }
            else
            {
                WriteDoc(doc, stdout);
            }
        }

        if (opts.Verbosity >= Verbosity.Verbose && targetRef == null)
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

    // Serialize a document to a TextWriter (mirrors save_xml_doc to "-").
    private static void WriteDoc(XmlDocument doc, TextWriter stdout)
    {
        stdout.Write(XmlUtils.ToXmlString(doc));
        stdout.Write('\n');
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

    private int PrintReference(XmlNode node, string src, Show show, string? targetRef, Show targetShow,
        Options opts, TextWriter stdout, TextWriter stderr)
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

        // In -w mode, only consider the target ref; replace the code so we match
        // that specific object rather than the latest with the same code.
        if (targetRef != null)
        {
            // Mirrors strnmatch(targetRef, code, strlen(code)): the target ref
            // must agree with the ref's code over the code's length (the target
            // ref may carry extra trailing components such as a language).
            if (!StrNMatch(targetRef, code, code.Length))
            {
                return 0;
            }
            code = targetRef;
        }

        string? fname = FindObjectFile(code, opts);
        if (fname != null)
        {
            if (opts.UpdateRefs)
            {
                UpdateRef(node, code, fname, opts, stderr);
            }
            else if (!opts.TagUnmatched)
            {
                if (opts.ShowMatched)
                {
                    PrintMatched(node, src, code, null, fname, opts, stdout);
                }

                if (opts.ListRecursively)
                {
                    if (targetRef != null)
                    {
                        ListWhereUsed(src, targetShow, opts, stdout, stderr);
                    }
                    else
                    {
                        ListReferences(fname, show, null, Show.None, opts, stdout, stderr);
                    }
                }
            }
            return 0;
        }

        // Unmatched.
        if (opts.TagUnmatched)
        {
            TagUnmatchedRef(node);
        }
        else if (opts.ShowUnmatched)
        {
            PrintMatched(node, src, code, null, null, opts, stdout);
        }
        else if (opts.Verbosity >= Verbosity.Normal)
        {
            PrintUnmatched(node, src, code, null, opts, stdout, stderr);
        }

        // Update metadata for unmatched external pubs (these are resolved from
        // the .externalpubs file, whether matched to a file or not).
        if (opts.UpdateRefs && opts.ExternalPubs != null && name == "externalPubRef")
        {
            UpdateRef(node, code, fname, opts, stderr);
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
            if (opts.ShowMatched && !opts.TagUnmatched)
            {
                PrintMatched(node, src, code, id, fname, opts, stdout);
            }
            return 0;
        }

        if (opts.TagUnmatched)
        {
            TagUnmatchedRef(csnref);
        }
        else if (opts.ShowUnmatched)
        {
            PrintMatched(node, src, code, id, null, opts, stdout);
        }
        else if (opts.Verbosity >= Verbosity.Normal)
        {
            PrintUnmatched(node, src, code, id, opts, stdout, stderr);
        }
        return 1;
    }

    // ----- Tag unmatched (-X) -----

    // Insert a <?unmatched?> processing instruction as the first child of the ref
    // (mirrors tagUnmatchedRef -> add_first_child(ref, xmlNewPI("unmatched"))).
    private static void TagUnmatchedRef(XmlNode reff)
    {
        if (reff is not XmlElement el)
        {
            return;
        }
        XmlDocument doc = el.OwnerDocument!;
        XmlProcessingInstruction pi = doc.CreateProcessingInstruction("unmatched", string.Empty);
        if (el.FirstChild != null)
        {
            el.InsertBefore(pi, el.FirstChild);
        }
        else
        {
            el.AppendChild(pi);
        }
    }

    // ----- Update references (-U / -I) -----

    private void UpdateRef(XmlNode reff, string code, string? fname, Options opts, TextWriter stderr)
    {
        if (opts.Verbosity >= Verbosity.Debug && fname != null)
        {
            stderr.WriteLine($"{Name}: INFO: Updating reference {code} to match {fname}...");
        }

        switch (reff.Name)
        {
            case "dmRef":
                UpdateDmRef((XmlElement)reff, fname, opts);
                break;
            case "pmRef":
                UpdatePmRef((XmlElement)reff, fname, opts);
                break;
            case "refdm":
                UpdateRefdm((XmlElement)reff, fname, opts);
                break;
            case "externalPubRef":
                UpdateExternalPubRef((XmlElement)reff, code, opts);
                break;
            // infoEntityIdent (ICN entity) rewriting is not ported (System.Xml
            // entity handling differs from libxml2).
        }
    }

    private static void UpdateDmRef(XmlElement reff, string? fname, Options opts)
    {
        if (fname == null) return;
        XmlDocument? doc = TryReadDoc(fname);
        if (doc == null) return;

        XmlDocument owner = reff.OwnerDocument!;
        XmlElement? dmRefIdent = (XmlElement?)FirstNode(reff, "dmRefIdent");

        if (opts.UpdateRefIdent && dmRefIdent != null)
        {
            ReplaceChild(dmRefIdent, "issueInfo", ImportIssueInfo(owner, doc, to30: false));
            ReplaceChild(dmRefIdent, "language", ImportLanguage(owner, doc, to30: false));
        }

        // Rebuild dmRefAddressItems.
        XmlNode? existing = FirstNode(reff, "dmRefAddressItems");
        if (existing != null)
        {
            reff.RemoveChild(existing);
        }
        XmlElement addressItems = owner.CreateElement("dmRefAddressItems");
        reff.AppendChild(addressItems);

        string? techName = XmlUtils.XPathFirstValue(doc, null, "//techName|//techname");
        string? infoName = XmlUtils.XPathFirstValue(doc, null, "//infoName|//infoname");
        string? infoNameVariant = XmlUtils.XPathFirstValue(doc, null, "//infoNameVariant");

        XmlElement dmTitle = owner.CreateElement("dmTitle");
        addressItems.AppendChild(dmTitle);
        AppendTextChild(dmTitle, "techName", techName ?? "");
        if (infoName != null) AppendTextChild(dmTitle, "infoName", infoName);
        if (infoNameVariant != null) AppendTextChild(dmTitle, "infoNameVariant", infoNameVariant);

        if (opts.UpdateRefIdent)
        {
            XmlElement? issueDate = ImportIssueDate(owner, doc);
            if (issueDate != null) addressItems.AppendChild(issueDate);
        }
    }

    private static void UpdatePmRef(XmlElement reff, string? fname, Options opts)
    {
        if (fname == null) return;
        XmlDocument? doc = TryReadDoc(fname);
        if (doc == null) return;

        XmlDocument owner = reff.OwnerDocument!;
        XmlElement? pmRefIdent = (XmlElement?)FirstNode(reff, "pmRefIdent");

        if (opts.UpdateRefIdent && pmRefIdent != null)
        {
            ReplaceChild(pmRefIdent, "issueInfo", ImportIssueInfo(owner, doc, to30: false));
            ReplaceChild(pmRefIdent, "language", ImportLanguage(owner, doc, to30: false));
        }

        XmlNode? existing = FirstNode(reff, "pmRefAddressItems");
        if (existing != null)
        {
            reff.RemoveChild(existing);
        }
        XmlElement addressItems = owner.CreateElement("pmRefAddressItems");
        reff.AppendChild(addressItems);

        string? pmTitle = XmlUtils.XPathFirstValue(doc, null, "//pmTitle|//pmtitle");
        AppendTextChild(addressItems, "pmTitle", pmTitle ?? "");

        if (opts.UpdateRefIdent)
        {
            XmlElement? issueDate = ImportIssueDate(owner, doc);
            if (issueDate != null) addressItems.AppendChild(issueDate);
        }
    }

    // refdm is the 3.0-era data module reference. We update its dmtitle child and
    // (for -I) its issno/language using the 3.0 element names.
    private static void UpdateRefdm(XmlElement reff, string? fname, Options opts)
    {
        if (fname == null) return;
        XmlDocument? doc = TryReadDoc(fname);
        if (doc == null) return;

        XmlDocument owner = reff.OwnerDocument!;

        if (opts.UpdateRefIdent)
        {
            XmlElement? newIssno = ImportIssueInfo(owner, doc, to30: true);
            XmlElement? newLanguage = ImportLanguage(owner, doc, to30: true);

            XmlNode? oldIssno = FirstNode(reff, "issno");
            if (newIssno != null)
            {
                XmlNode? anchor = oldIssno ?? FirstNode(reff, "avee");
                if (anchor != null)
                {
                    InsertAfter(reff, anchor, newIssno);
                }
                else
                {
                    reff.AppendChild(newIssno);
                }
            }
            if (oldIssno != null) reff.RemoveChild(oldIssno);

            XmlNode? oldLanguage = FirstNode(reff, "language");
            if (newLanguage != null)
            {
                XmlNode? anchor = oldLanguage ?? newIssno ?? FirstNode(reff, "issno");
                if (anchor != null)
                {
                    InsertAfter(reff, anchor, newLanguage);
                }
            }
            if (oldLanguage != null) reff.RemoveChild(oldLanguage);
        }

        string? techName = XmlUtils.XPathFirstValue(doc, null, "//techName|//techname");
        string? infoName = XmlUtils.XPathFirstValue(doc, null, "//infoName|//infoname");

        XmlElement newTitle = owner.CreateElement("dmtitle");
        XmlNode? oldTitle = FirstNode(reff, "dmtitle");
        XmlNode? anchorTitle = oldTitle ?? FirstNode(reff, "(avee|issno)[last()]");
        if (anchorTitle != null)
        {
            InsertAfter(reff, anchorTitle, newTitle);
        }
        else
        {
            reff.AppendChild(newTitle);
        }
        AppendTextChild(newTitle, "techname", techName ?? "");
        if (infoName != null) AppendTextChild(newTitle, "infoname", infoName);

        if (oldTitle != null) reff.RemoveChild(oldTitle);
    }

    // Replace an externalPubRef with the matching definition from .externalpubs.
    private static void UpdateExternalPubRef(XmlElement reff, string code, Options opts)
    {
        if (opts.ExternalPubs == null) return;

        XmlNode? replacement = XmlUtils.XPathFirstNode(opts.ExternalPubs, null,
            $"//externalPubRef[externalPubRefIdent/externalPubCode={XPathLiteral(code)}]");
        if (replacement == null) return;

        XmlDocument owner = reff.OwnerDocument!;
        XmlNode imported = owner.ImportNode(replacement, true);
        reff.ParentNode?.InsertAfter(imported, reff);
        reff.ParentNode?.RemoveChild(reff);
    }

    // Import the latest object's issueInfo (or issno) into the ref's document,
    // converting between 3.0 (issno) and 4.x (issueInfo) element names as needed.
    private static XmlElement? ImportIssueInfo(XmlDocument owner, XmlDocument srcDoc, bool to30)
    {
        XmlNode? found = XmlUtils.XPathFirstNode(srcDoc, null, "//issueInfo|//issno");
        if (found == null) return null;
        var imported = (XmlElement)owner.ImportNode(found, true);

        bool is30 = imported.Name == "issno";
        if (!to30 && is30)
        {
            // 4.x reference -> 3.0 source: convert issno to issueInfo.
            imported = RenameElement(imported, "issueInfo");
            RenameAttr(imported, "issno", "issueNumber");
            if (imported.HasAttribute("inwork"))
            {
                RenameAttr(imported, "inwork", "inWork");
            }
            else
            {
                imported.SetAttribute("inWork", "00");
            }
            imported.RemoveAttribute("type");
        }
        else if (to30 && !is30)
        {
            // 3.0 reference -> 4.x source: convert issueInfo to issno.
            imported = RenameElement(imported, "issno");
            RenameAttr(imported, "issueNumber", "issno");
            RenameAttr(imported, "inWork", "inwork");
        }
        return imported;
    }

    private static XmlElement? ImportLanguage(XmlDocument owner, XmlDocument srcDoc, bool to30)
    {
        XmlNode? found = XmlUtils.XPathFirstNode(srcDoc, null, "//language");
        if (found == null) return null;
        var imported = (XmlElement)owner.ImportNode(found, true);

        // When converting issueInfo names above we also rename the language
        // attributes. Detect direction from whether 4.x attributes are present.
        bool is30Lang = imported.HasAttribute("language") || imported.HasAttribute("country");
        if (!to30 && is30Lang)
        {
            RenameAttr(imported, "language", "languageIsoCode");
            RenameAttr(imported, "country", "countryIsoCode");
        }
        else if (to30 && !is30Lang)
        {
            RenameAttr(imported, "languageIsoCode", "language");
            RenameAttr(imported, "countryIsoCode", "country");
        }
        return imported;
    }

    private static XmlElement? ImportIssueDate(XmlDocument owner, XmlDocument srcDoc)
    {
        XmlNode? found = XmlUtils.XPathFirstNode(srcDoc, null, "//issueDate|//issdate");
        if (found == null) return null;
        var imported = (XmlElement)owner.ImportNode(found, true);
        if (imported.Name == "issdate")
        {
            imported = RenameElement(imported, "issueDate");
        }
        return imported;
    }

    private static XmlElement RenameElement(XmlElement el, string newName)
    {
        XmlDocument doc = el.OwnerDocument!;
        XmlElement replacement = doc.CreateElement(newName);
        foreach (XmlAttribute attr in el.Attributes)
        {
            replacement.SetAttribute(attr.Name, attr.Value);
        }
        foreach (XmlNode child in el.ChildNodes.Cast<XmlNode>().ToList())
        {
            replacement.AppendChild(child.CloneNode(true));
        }
        return replacement;
    }

    private static void RenameAttr(XmlElement el, string oldName, string newName)
    {
        if (!el.HasAttribute(oldName)) return;
        string value = el.GetAttribute(oldName);
        el.RemoveAttribute(oldName);
        el.SetAttribute(newName, value);
    }

    private static void ReplaceChild(XmlElement parent, string childName, XmlElement? replacement)
    {
        XmlNode? existing = FirstNode(parent, childName);
        if (existing != null)
        {
            parent.RemoveChild(existing);
        }
        if (replacement != null)
        {
            parent.AppendChild(replacement);
        }
    }

    private static void InsertAfter(XmlElement parent, XmlNode anchor, XmlNode newNode)
    {
        parent.InsertAfter(newNode, anchor);
    }

    private static void AppendTextChild(XmlElement parent, string name, string text)
    {
        XmlElement child = parent.OwnerDocument!.CreateElement(name);
        child.AppendChild(parent.OwnerDocument!.CreateTextNode(text));
        parent.AppendChild(child);
    }

    // Build an XPath string literal that safely quotes arbitrary text.
    private static string XPathLiteral(string value)
    {
        if (!value.Contains('\''))
        {
            return "'" + value + "'";
        }
        if (!value.Contains('"'))
        {
            return "\"" + value + "\"";
        }
        // Both quotes present: use concat().
        var parts = value.Split('\'');
        var sb = new StringBuilder("concat(");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append(", \"'\", ");
            sb.Append('\'').Append(parts[i]).Append('\'');
        }
        sb.Append(')');
        return sb.ToString();
    }

    // ----- Where-used (-w) -----

    private int ListWhereUsed(string path, Show show, Options opts, TextWriter stdout, TextWriter stderr)
    {
        if (opts.ListRecursively)
        {
            if (!opts.ListedFiles.Add(path))
            {
                return 0;
            }
        }

        if (opts.Verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"{Name}: INFO: Searching for references to {path}...");
        }

        string code;
        string baseName = Path.GetFileName(path);

        if (Csdb.IsIcn(baseName))
        {
            // ICN target: code is the filename up to the first '.'.
            int dot = baseName.IndexOf('.');
            code = dot < 0 ? baseName : baseName[..dot];
        }
        else
        {
            XmlDocument? doc = TryReadDoc(path);
            if (doc != null)
            {
                if (opts.RemDelete)
                {
                    XmlUtils.RemoveDeleteElements(doc);
                }
                code = CodeFromIdent(doc, opts) ?? "";
            }
            else
            {
                // Interpret the path as a literal code.
                code = path;
            }
        }

        if (code.Length == 0)
        {
            return 1;
        }

        return FindWhereUsed(opts.Directory, code, show, opts, stdout, stderr);
    }

    // Determine the reference code for a whole object from its ident section.
    private static string? CodeFromIdent(XmlDocument doc, Options opts)
    {
        XmlNode? ident = XmlUtils.XPathFirstNode(doc, null,
            "//dmIdent|//pmIdent|//commentIdent|//dmlIdent|//scormContentPackageIdent");
        if (ident == null)
        {
            return null;
        }

        // Wrap a copy of the ident in a synthetic ref element and reuse the code
        // builders (mirrors listWhereUsed's node renaming).
        var tmp = new XmlDocument();
        XmlNode importedIdent = tmp.ImportNode(ident, true);

        var tool = new RefsTool();
        switch (ident.Name)
        {
            case "commentIdent":
            {
                XmlElement reff = tmp.CreateElement("commentRef");
                XmlElement refIdent = RenameTo(tmp, importedIdent, "commentRefIdent");
                reff.AppendChild(refIdent);
                tmp.AppendChild(reff);
                return GetComCode(reff);
            }
            case "dmIdent":
            {
                XmlElement reff = tmp.CreateElement("dmRef");
                XmlElement refIdent = RenameTo(tmp, importedIdent, "dmRefIdent");
                reff.AppendChild(refIdent);
                tmp.AppendChild(reff);
                return tool.GetDmCode(reff, opts);
            }
            case "dmlIdent":
            {
                XmlElement reff = tmp.CreateElement("dmlRef");
                XmlElement refIdent = RenameTo(tmp, importedIdent, "dmlRefIdent");
                reff.AppendChild(refIdent);
                tmp.AppendChild(reff);
                return GetDmlCode(reff);
            }
            case "pmIdent":
            {
                XmlElement reff = tmp.CreateElement("pmRef");
                XmlElement refIdent = RenameTo(tmp, importedIdent, "pmRefIdent");
                reff.AppendChild(refIdent);
                tmp.AppendChild(reff);
                return tool.GetPmCode(reff, opts);
            }
            case "scormContentPackageIdent":
            {
                XmlElement reff = tmp.CreateElement("scormContentPackageRef");
                XmlElement refIdent = RenameTo(tmp, importedIdent, "scormContentPackageRefIdent");
                reff.AppendChild(refIdent);
                tmp.AppendChild(reff);
                return tool.GetSmcCode(reff, opts);
            }
            default:
                return null;
        }
    }

    private static XmlElement RenameTo(XmlDocument doc, XmlNode node, string name)
    {
        XmlElement el = doc.CreateElement(name);
        if (node.Attributes != null)
        {
            foreach (XmlAttribute attr in node.Attributes)
            {
                el.SetAttribute(attr.Name, attr.Value);
            }
        }
        foreach (XmlNode child in node.ChildNodes.Cast<XmlNode>().ToList())
        {
            el.AppendChild(child.CloneNode(true));
        }
        return el;
    }

    // Search objects in a directory for references to a target object.
    private int FindWhereUsed(string dir, string code, Show show, Options opts, TextWriter stdout, TextWriter stderr)
    {
        if (!Directory.Exists(dir))
        {
            return 1;
        }

        string prefix = dir == "." ? "" : dir.EndsWith('/') ? dir : dir + "/";
        int unmatched = 0;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch (IOException)
        {
            return 1;
        }

        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            string cpath = prefix + name;

            if (opts.Recursive && Directory.Exists(cpath) && name is not ("." or ".."))
            {
                unmatched += FindWhereUsed(cpath, code, show, opts, stdout, stderr);
            }
            else if (File.Exists(cpath) && IsUsedTarget(name, show))
            {
                unmatched += ListReferences(cpath, Show.WhereUsed, code, show, opts, stdout, stderr);
            }
        }

        return unmatched;
    }

    private static bool IsUsedTarget(string name, Show show) =>
        (Has(show, Show.Com) && Csdb.IsComment(name)) ||
        (Has(show, Show.Dmc) && Csdb.IsDataModule(name)) ||
        (Has(show, Show.Dml) && Csdb.IsDataManagementList(name)) ||
        (Has(show, Show.Pmc) && Csdb.IsPublicationModule(name)) ||
        (Has(show, Show.Smc) && Csdb.IsScormContentPackage(name));

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

    // Case-insensitive match of pattern against value, comparing at most n
    // characters, with '?' as a single-character wildcard (mirrors strnmatch).
    private static bool StrNMatch(string pattern, string value, int n)
    {
        for (int i = 0; i < pattern.Length && i < n; i++)
        {
            if (pattern[i] == '?')
            {
                continue;
            }
            if (i >= value.Length ||
                char.ToLowerInvariant(pattern[i]) != char.ToLowerInvariant(value[i]))
            {
                return false;
            }
        }
        return true;
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
            case OutputMode.WhereUsed:
                stdout.Write($"{src}\n");
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
            case OutputMode.WhereUsed:
                // -w uses printUnmatchedSrc for unmatched refs.
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

    // Read a non-chapterized IPD SNS code (-b), mirroring readnonChapIpdSns.
    // "-" means the SNS is also relative to the containing DM. Otherwise the
    // form is "SYS-SUBSUBSUB-ASSY", e.g. "ZD-00-35", parsed as system "ZD",
    // subSystem "0", subSubSystem "0", assy "35". Returns false on a bad code.
    private bool ReadNonChapIpdSns(string s, Options opts, TextWriter stderr)
    {
        if (s == "-")
        {
            opts.NonChapIpdSystemCode = "-";
            return true;
        }

        // sscanf "%3[0-9A-Z]-%1[0-9A-Z]%1[0-9A-Z]-%4[0-9A-Z]": four greedy
        // alphanumeric fields with widths 3,1,1,4 separated by '-'.
        if (TryParseNonChapSns(s,
                out string sys, out string sub, out string subsub, out string assy))
        {
            opts.NonChapIpdSystemCode = sys;
            opts.NonChapIpdSubSystemCode = sub;
            opts.NonChapIpdSubSubSystemCode = subsub;
            opts.NonChapIpdAssyCode = assy;
            return true;
        }

        stderr.WriteLine($"{Name}: ERROR: Invalid non-chapterized IPD SNS: {s}");
        return false;
    }

    // Mirror of the sscanf "%3[0-9A-Z]-%1[0-9A-Z]%1[0-9A-Z]-%4[0-9A-Z]" parse:
    // all four conversions must succeed (n == 4) for the SNS to be valid.
    private static bool TryParseNonChapSns(string s,
        out string sys, out string sub, out string subsub, out string assy)
    {
        sys = sub = subsub = assy = "";

        static bool IsField(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z');

        int p = 0;
        // %3[0-9A-Z]: 1..3 chars.
        string Scan(int max)
        {
            int start = p;
            while (p < s.Length && p - start < max && IsField(s[p])) p++;
            return s[start..p];
        }

        sys = Scan(3);
        if (sys.Length == 0 || p >= s.Length || s[p] != '-') return false;
        p++;
        sub = Scan(1);
        if (sub.Length == 0) return false;
        subsub = Scan(1);
        if (subsub.Length == 0 || p >= s.Length || s[p] != '-') return false;
        p++;
        assy = Scan(4);
        return assy.Length != 0;
    }

    // IPD / CSN code construction (mirrors getCsnCode/getIpdCode). When a
    // non-chapterized IPD SNS is supplied (-b), missing SNS components are
    // filled in (from the given SNS, or from the containing DM when the SNS is
    // "-"), allowing non-chapterized CSN refs to resolve to an IPD DMC.
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

        // Apply attributes to non-chapterized or old style CSN refs (these are
        // always interpreted as relative to the current DM).
        if (opts.NonChapIpdSns || csnValue != null)
        {
            XmlNode? dmCode = FirstNode(reff,
                "ancestor::dmodule/identAndStatusSection/dmAddress/dmIdent/dmCode|ancestor::dmodule/idstatus/dmaddres/dmc/avee");
            if (dmCode != null)
            {
                mic ??= FirstValue(dmCode, "@modelIdentCode|modelic");
                sdc ??= FirstValue(dmCode, "@systemDiffCode|sdc");
                ilc ??= "?";

                // If a non-chapterized IPD SNS is given, apply it.
                if (opts.NonChapIpdSns)
                {
                    if (opts.NonChapIpdSystemCode == "-")
                    {
                        // SNS is also relative to the current DM.
                        sys ??= FirstValue(dmCode, "@systemCode|chapnum");
                        sub ??= FirstValue(dmCode, "@subSystemCode|section");
                        subsub ??= FirstValue(dmCode, "@subSubSystemCode|subsect");
                        assy ??= FirstValue(dmCode, "@assyCode|subject");
                    }
                    else
                    {
                        // Construct the SNS from the given code.
                        sys ??= opts.NonChapIpdSystemCode;
                        sub ??= opts.NonChapIpdSubSystemCode;
                        subsub ??= opts.NonChapIpdSubSubSystemCode;
                        assy ??= opts.NonChapIpdAssyCode;
                    }
                }
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
        stdout.WriteLine($"Usage: s1kd-{Name} [-aBCcDEFfGHIiKLlMmNnoPqRrSsTUuvwXxYZ^h?] [-b <SNS>] [-d <dir>] [-t <fmt>] [-k <pattern>] [-3 <file>] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -a, --all              Print unmatched codes.");
        stdout.WriteLine("  -B, --ipd              List IPD references.");
        stdout.WriteLine("  -b, --ipd-sns <SNS>    The SNS for non-chapterized IPDs.");
        stdout.WriteLine("  -C, --com              List comment references.");
        stdout.WriteLine("  -c, --content          Only show references in content section.");
        stdout.WriteLine("  -D, --dm               List data module references.");
        stdout.WriteLine("  -d, --dir <dir>        Directory to search for matches in.");
        stdout.WriteLine("  -E, --epr              List external pub refs.");
        stdout.WriteLine("  -F, --overwrite        Overwrite updated (-U) or tagged (-X) objects.");
        stdout.WriteLine("  -f, --filename         Print the source filename for each reference.");
        stdout.WriteLine("  -G, --icn              List ICN references.");
        stdout.WriteLine("  -I, --update-issue     Update references to the latest matched object (implies -U -i).");
        stdout.WriteLine("  -i, --ignore-issue     Ignore issue info when matching.");
        stdout.WriteLine("  -K, --csn              List CSN references.");
        stdout.WriteLine("  -L, --dml              List DML references.");
        stdout.WriteLine("  -l, --list             Treat input as list of CSDB objects.");
        stdout.WriteLine("  -M, --no-match         Do not attempt to match references to CSDB objects.");
        stdout.WriteLine("  -m, --strict-match     Be more strict when matching filenames of objects.");
        stdout.WriteLine("  -N, --omit-issue       Assume filenames omit issue info.");
        stdout.WriteLine("  -n, --lineno           Print the source filename and line number for each reference.");
        stdout.WriteLine("  -o, --output-valid     Output valid CSDB objects to stdout.");
        stdout.WriteLine("  -P, --pm               List publication module references.");
        stdout.WriteLine("  -q, --quiet            Quiet mode.");
        stdout.WriteLine("  -R, --recursively      List references in matched objects recursively.");
        stdout.WriteLine("  -r, --recursive        Search for matches in directories recursively.");
        stdout.WriteLine("  -S, --smc              List SCORM content package references.");
        stdout.WriteLine("  -s, --include-src      Include the source object as a reference.");
        stdout.WriteLine("  -T, --fragment         List referred fragments in other DMs.");
        stdout.WriteLine("  -t, --format <fmt>     The format to use when printing references.");
        stdout.WriteLine("  -U, --update           Update address items in matched references.");
        stdout.WriteLine("  -u, --unmatched        Show only unmatched references.");
        stdout.WriteLine("  -v, --verbose          Verbose output.");
        stdout.WriteLine("  -w, --where-used       List places where an object is referenced.");
        stdout.WriteLine("  -X, --tag-unmatched    Tag unmatched references.");
        stdout.WriteLine("  -x, --xml              Output XML report.");
        stdout.WriteLine("  -Y, --repository       List repository source DMs.");
        stdout.WriteLine("  -Z, --source           List source DM or PM.");
        stdout.WriteLine("  -3, --externalpubs <file>  Use custom .externalpubs file.");
        stdout.WriteLine("  -^, --remove-deleted   List refs with elements marked as \"delete\" removed.");
        stdout.WriteLine("      --version          Show version information.");
        stdout.WriteLine("  <object>               CSDB object to list references in.");
    }
}
