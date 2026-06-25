using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-flatten</c>: flatten a publication module by resolving each
/// referenced data module / publication module to its matching CSDB file and
/// inlining (or rewriting) the reference into a single deliverable document.
///
/// <para>The port walks the PM DOM exactly like the C tool: it descends through
/// <c>content</c> -&gt; <c>pmEntry</c> structures and, for every <c>dmRef</c> /
/// <c>pmRef</c> (and their legacy <c>refdm</c> / <c>refpm</c> spellings), builds
/// the canonical CSDB file name from the code, searches the configured
/// directories for the latest matching file, and either copies the referenced
/// root element in place of the reference, emits an XInclude element, or rewrites
/// the reference, depending on the options.</para>
///
/// <para>Supported options mirror the C tool: <c>-c/--containers</c>,
/// <c>-D/--remove</c>, <c>-d/--dir</c>, <c>-f/--overwrite</c>,
/// <c>-I/--include</c>, <c>-i/--ignore-issue</c>, <c>-l/--list</c>,
/// <c>-m/--modify</c>, <c>-N/--omit-issue</c>, <c>-P/--only-pm-refs</c>,
/// <c>-p/--simple</c>, <c>-q/--quiet</c>, <c>-R/--recursively</c>,
/// <c>-r/--recursive</c>, <c>-u/--unique</c>, <c>-v/--verbose</c>,
/// <c>-x/--use-xinclude</c>.</para>
///
/// <para>The C tool's <c>-u/--unique</c> duplicate-removal is implemented with
/// three EXSLT-using XSLT stylesheets (<c>remdups1.xsl</c>, <c>remdups2.xsl</c>,
/// <c>remove_empty_pmentries.xsl</c>). Rather than port the EXSLT shim, this
/// option is reproduced directly in DOM code (see <see cref="RemoveDuplicateRefs"/>):
/// duplicate <c>dmRef</c>/<c>pmRef</c> elements (by serialized content) are
/// removed and empty <c>pmEntry</c> elements are pruned, matching the observable
/// behaviour of the stylesheets.</para>
/// </summary>
public sealed class FlattenTool : ITool
{
    public string Name => "flatten";
    public string Description => "Flatten a publication module for distribution.";

    // Matches VERSION in reference/tools/s1kd-flatten/s1kd-flatten.c.
    public string Version => "4.0.0";

    private const int ExitSuccess = 0;
    private const int ExitBadPm = 1;       // EXIT_BAD_PM
    private const int ExitBadUsage = 2;

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2, Debug = 3 }

    // --- Option state (mirrors the C file-scope statics) --------------------
    private bool _xinclude;
    private bool _noIssue;
    private bool _ignoreIss;
    private bool _usePubFmt;
    private bool _flattenRef = true;   // cleared by -m
    private bool _flattenContainer;
    private bool _recursive;           // -R
    private bool _recursiveSearch;     // -r
    private bool _removeUnresolved;    // -D
    private bool _onlyPmRefs;          // -P
    private Verbosity _verbosity = Verbosity.Normal;

    private readonly List<string> _searchPaths = new();
    private TextWriter _stderr = TextWriter.Null;

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        _stderr = stderr;

        bool overwrite = false;
        bool removeDups = false;
        bool isList = false;
        string? searchDir = ".";
        var includePaths = new List<string>();
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
                    stdout.WriteLine($"{Name} (s1kd-tools) {Version}");
                    return ExitSuccess;
                case "-c" or "--containers": _flattenContainer = true; break;
                case "-D" or "--remove": _removeUnresolved = true; break;
                case "-d" or "--dir":
                    if (!TakeArg(args, ref i, out searchDir)) return ArgError(stderr, a);
                    break;
                case "-f" or "--overwrite": overwrite = true; break;
                case "-x" or "--use-xinclude": _xinclude = true; break;
                case "-m" or "--modify": _flattenRef = false; break;
                case "-N" or "--omit-issue": _noIssue = true; break;
                case "-P" or "--only-pm-refs": _onlyPmRefs = true; break;
                case "-p" or "--simple": _usePubFmt = true; break;
                case "-q" or "--quiet": _verbosity--; break;
                case "-R" or "--recursively": _recursive = true; break;
                case "-r" or "--recursive": _recursiveSearch = true; break;
                case "-u" or "--unique": removeDups = true; break;
                case "-v" or "--verbose": _verbosity++; break;
                case "-I" or "--include":
                    if (!TakeArg(args, ref i, out string? inc)) return ArgError(stderr, a);
                    includePaths.Add(inc!);
                    break;
                case "-i" or "--ignore-issue": _ignoreIss = true; break;
                case "-l" or "--list": isList = true; _usePubFmt = true; break;
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

        // Build the ordered search-path list: -I paths first, then -d (default ".").
        _searchPaths.AddRange(includePaths);
        _searchPaths.Add(searchDir ?? ".");

        XmlDocument pubDoc;

        if (_usePubFmt)
        {
            pubDoc = XmlUtils.NewDocument();
            XmlElement pub = pubDoc.CreateElement("publication");
            pubDoc.AppendChild(pub);

            if (files.Count > 0)
            {
                foreach (string f in files)
                {
                    if (isList)
                    {
                        FlattenList(f, pub);
                    }
                    else
                    {
                        FlattenFile(pub, f);
                    }
                }
            }
            else if (isList)
            {
                FlattenList(null, pub);
            }
            else
            {
                FlattenFile(pub, "-");
            }
        }
        else
        {
            string pmFname = files.Count > 0 ? files[0] : "-";

            try
            {
                pubDoc = pmFname == "-"
                    ? XmlUtils.ReadStream(Console.OpenStandardInput())
                    : XmlUtils.ReadDoc(pmFname);
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                stderr.WriteLine($"{Name}: ERROR: Bad publication module: {pmFname}");
                return ExitBadPm;
            }

            XmlElement? pm = pubDoc.DocumentElement;
            XmlElement? content = pm == null ? null : FindChild(pm, "content");

            if (content == null)
            {
                stderr.WriteLine($"{Name}: ERROR: Bad publication module: {pmFname}");
                return ExitBadPm;
            }

            // Walk the content's pmEntry children.
            XmlNode? cur = content.FirstChild;
            while (cur != null)
            {
                XmlNode? next = cur.NextSibling;
                if (cur is XmlElement el && (el.LocalName == "pmEntry" || el.LocalName == "pmentry"))
                {
                    FlattenPmEntry(el);
                }
                cur = next;
            }
        }

        if (removeDups)
        {
            if (_verbosity >= Verbosity.Verbose)
            {
                stderr.WriteLine($"{Name}: INFO: Removing duplicate references...");
            }
            RemoveDuplicateRefs(pubDoc);
        }

        // Output: overwrite the PM file only in non-pub mode with -f; else stdout.
        if (overwrite && !_usePubFmt && files.Count > 0 && files[0] != "-")
        {
            try
            {
                XmlUtils.SaveDoc(pubDoc, files[0]);
            }
            catch (IOException)
            {
                stderr.WriteLine($"{Name}: ERROR: {files[0]} does not have write permission.");
                return ExitBadUsage;
            }
        }
        else
        {
            stdout.Write(XmlUtils.ToXmlString(pubDoc));
            stdout.Write('\n');
        }

        return ExitSuccess;
    }

    // --- pmEntry traversal --------------------------------------------------

    private void FlattenPmEntry(XmlElement pmEntry)
    {
        XmlNode? cur = pmEntry.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;

            if (cur is XmlElement el)
            {
                switch (el.LocalName)
                {
                    case "dmRef" or "refdm":
                        FlattenDmRef(el);
                        break;
                    case "pmRef" or "refpm":
                        FlattenPmRef(el);
                        break;
                    case "pmEntry" or "pmentry":
                        FlattenPmEntry(el);
                        break;
                }
            }

            cur = next;
        }

        // Remove the pmEntry if it has no element children left, or its only
        // remaining (last) element child is a (pmEntry)title.
        XmlElement? lastEl = LastElementChild(pmEntry);
        if (lastEl == null ||
            lastEl.LocalName == "pmEntryTitle" || lastEl.LocalName == "title")
        {
            pmEntry.ParentNode?.RemoveChild(pmEntry);
        }
    }

    // --- pmRef flattening ---------------------------------------------------

    private void FlattenPmRef(XmlElement pmRef)
    {
        if (!(_flattenRef || _removeUnresolved || _recursive))
        {
            return;
        }

        XmlNode? pmCode = XPath(pmRef, ".//pmCode|.//pmc");
        XmlNode? issueInfo = _ignoreIss ? null : XPath(pmRef, ".//issueInfo|.//issno");
        XmlNode? language = XPath(pmRef, ".//language");

        string modelIdentCode = Str(pmCode, "@modelIdentCode|modelic");
        string pmIssuer = Str(pmCode, "@pmIssuer|pmissuer");
        string pmNumber = Str(pmCode, "@pmNumber|pmnumber");
        string pmVolume = Str(pmCode, "@pmVolume|pmvolume");

        string pmc = $"{modelIdentCode}-{pmIssuer}-{pmNumber}-{pmVolume}";
        string pmFname = $"PMC-{pmc}";
        pmFname = AppendIssueAndLanguage(pmFname, issueInfo, language);

        bool found = TryResolve(pmFname, isPm: true, out string fsName);

        if (found)
        {
            if (_flattenRef)
            {
                if (_verbosity >= Verbosity.Verbose)
                {
                    _stderr.WriteLine($"{Name}: INFO: Including {fsName}...");
                }

                if (_xinclude && !_recursive)
                {
                    XmlElement xi = CreateXInclude(pmRef.OwnerDocument, fsName);
                    if (_usePubFmt)
                    {
                        pmRef.OwnerDocument.DocumentElement!.AppendChild(xi);
                    }
                    else
                    {
                        pmRef.ParentNode!.InsertBefore(xi, pmRef);
                    }
                }
                else
                {
                    XmlDocument subpm = XmlUtils.ReadDoc(fsName);

                    if (_recursive)
                    {
                        XmlNode? subContent = subpm.SelectSingleNode("//content");
                        if (subContent is XmlElement contentEl)
                        {
                            FlattenPmEntry(contentEl);
                        }
                    }

                    XmlNode imported = pmRef.OwnerDocument.ImportNode(subpm.DocumentElement!, true);
                    pmRef.ParentNode!.InsertBefore(imported, pmRef);
                }
            }
        }
        else if (_removeUnresolved)
        {
            if (_verbosity >= Verbosity.Verbose)
            {
                _stderr.WriteLine($"{Name}: INFO: Removing {pmFname}...");
            }
        }
        else
        {
            if (_verbosity >= Verbosity.Normal)
            {
                _stderr.WriteLine($"{Name}: WARNING: Could not read referenced object: {pmFname}");
            }
        }

        if ((found && (_flattenRef || _recursive)) || (!found && _removeUnresolved))
        {
            pmRef.ParentNode?.RemoveChild(pmRef);
        }
    }

    // --- dmRef flattening ---------------------------------------------------

    private void FlattenDmRef(XmlElement dmRef)
    {
        if (_onlyPmRefs || !(_flattenRef || _removeUnresolved || _flattenContainer))
        {
            return;
        }

        XmlNode? dmCode = XPath(dmRef, ".//dmCode|.//avee");
        XmlNode? issueInfo = _ignoreIss ? null : XPath(dmRef, ".//issueInfo|.//issno");
        XmlNode? language = XPath(dmRef, ".//language");

        string dmc = string.Concat(
            Str(dmCode, "@modelIdentCode|modelic"), "-",
            Str(dmCode, "@systemDiffCode|sdc"), "-",
            Str(dmCode, "@systemCode|chapnum"), "-",
            Str(dmCode, "@subSystemCode|section"),
            Str(dmCode, "@subSubSystemCode|subsect"), "-",
            Str(dmCode, "@assyCode|subject"), "-",
            Str(dmCode, "@disassyCode|discode"),
            Str(dmCode, "@disassyCodeVariant|discodev"), "-",
            Str(dmCode, "@infoCode|incode"),
            Str(dmCode, "@infoCodeVariant|incodev"), "-",
            Str(dmCode, "@itemLocationCode|itemloc"));

        string dmFname = $"DMC-{dmc}";
        dmFname = AppendIssueAndLanguage(dmFname, issueInfo, language);

        bool found = TryResolve(dmFname, isPm: false, out string fsName);

        if (found)
        {
            // Flatten a container data module by copying the dmRefs inside the
            // container directly into the publication module.
            if (_flattenContainer)
            {
                XmlDocument doc = XmlUtils.ReadDoc(fsName);
                XmlNode? refs = doc.SelectSingleNode("//container/refs");
                if (refs is XmlElement refsEl)
                {
                    // First, flatten the dmRefs in the container itself.
                    FlattenPmEntry(refsEl);

                    // Copy each child element from the container, in reverse, as
                    // the next sibling of the dmRef (preserving original order).
                    for (XmlNode? c = refsEl.LastChild; c != null; c = c.PreviousSibling)
                    {
                        if (c.NodeType != XmlNodeType.Element)
                        {
                            continue;
                        }
                        XmlNode imported = dmRef.OwnerDocument.ImportNode(c, true);
                        dmRef.ParentNode!.InsertAfter(imported, dmRef);
                    }
                }
            }

            if (_flattenRef)
            {
                if (_verbosity >= Verbosity.Verbose)
                {
                    _stderr.WriteLine($"{Name}: INFO: Including {fsName}...");
                }

                if (_xinclude)
                {
                    XmlElement xi = CreateXInclude(dmRef.OwnerDocument, fsName);
                    if (_usePubFmt)
                    {
                        dmRef.OwnerDocument.DocumentElement!.AppendChild(xi);
                    }
                    else
                    {
                        dmRef.ParentNode!.InsertBefore(xi, dmRef);
                    }
                }
                else
                {
                    XmlDocument doc = XmlUtils.ReadDoc(fsName);
                    XmlElement dmodule = doc.DocumentElement!;

                    string applic = dmRef.GetAttribute("applicRefId");
                    if (!string.IsNullOrEmpty(applic))
                    {
                        dmodule.SetAttribute("applicRefId", applic);
                    }

                    XmlNode imported = dmRef.OwnerDocument.ImportNode(dmodule, true);
                    dmRef.ParentNode!.InsertBefore(imported, dmRef);
                }
            }
        }
        else if (_removeUnresolved)
        {
            if (_verbosity >= Verbosity.Verbose)
            {
                _stderr.WriteLine($"{Name}: INFO: Removing {dmFname}...");
            }
        }
        else
        {
            if (_verbosity >= Verbosity.Normal)
            {
                _stderr.WriteLine($"{Name}: WARNING: Could not read referenced object: {dmFname}");
            }
        }

        if ((found && _flattenRef) || (!found && _removeUnresolved))
        {
            dmRef.ParentNode?.RemoveChild(dmRef);
        }
    }

    // --- pub-format (-p / -l) helpers --------------------------------------

    private void FlattenFile(XmlElement pub, string fname)
    {
        if (_xinclude)
        {
            XmlElement xi = CreateXInclude(pub.OwnerDocument, fname);
            pub.AppendChild(xi);
        }
        else
        {
            XmlDocument doc = fname == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(fname);
            XmlNode imported = pub.OwnerDocument.ImportNode(doc.DocumentElement!, true);
            pub.AppendChild(imported);
        }
    }

    private void FlattenList(string? path, XmlElement pub)
    {
        TextReader reader;
        if (path != null)
        {
            try
            {
                reader = new StreamReader(path);
            }
            catch (IOException)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    _stderr.WriteLine($"{Name}: ERROR: Could not read list: {path}");
                }
                return;
            }
        }
        else
        {
            reader = Console.In;
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Split('\t')[0].TrimEnd('\r', '\n');
                if (trimmed.Length == 0)
                {
                    continue;
                }
                FlattenFile(pub, trimmed);
            }
        }
        finally
        {
            if (path != null)
            {
                reader.Dispose();
            }
        }
    }

    // --- CSDB object resolution --------------------------------------------

    /// <summary>
    /// Search the configured paths (in order) for the latest file matching the
    /// CSDB code name. Mirrors the C tool's loop over <c>search_paths</c>
    /// combined with <c>find_csdb_object</c>. Returns the file-system path of the
    /// first directory in which a match is found.
    /// </summary>
    private bool TryResolve(string code, bool isPm, out string fsName)
    {
        foreach (string path in _searchPaths)
        {
            if (_verbosity >= Verbosity.Debug)
            {
                _stderr.WriteLine($"{Name}: INFO: Searching for {code} in '{path}' ...");
            }

            if (FindCsdbObject(path, code, isPm, _recursiveSearch, out fsName))
            {
                if (_verbosity >= Verbosity.Debug)
                {
                    _stderr.WriteLine($"{Name}: INFO: Found {fsName}");
                }
                return true;
            }
        }

        fsName = string.Empty;
        return false;
    }

    /// <summary>
    /// Port of <c>find_csdb_object</c>: within <paramref name="dir"/>, find the
    /// file whose base name begins with <paramref name="code"/> (case-insensitive,
    /// with <c>?</c> wildcards), passes the type predicate, and has the
    /// lexicographically greatest base name (i.e. the latest issue). When
    /// recursive, sub-directories are searched too and the overall greatest base
    /// name wins.
    /// </summary>
    private static bool FindCsdbObject(string dir, string code, bool isPm, bool recursive, out string result)
    {
        result = string.Empty;

        if (!Directory.Exists(dir))
        {
            return false;
        }

        bool found = false;
        string best = string.Empty;
        string prefix = dir == "." ? "" : dir.EndsWith('/') || dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + "/";

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            string cpath = prefix + name;

            if (recursive && Directory.Exists(entry))
            {
                if (FindCsdbObject(cpath, code, isPm, true, out string tmp) &&
                    (!found || CompareBase(tmp, best) > 0))
                {
                    best = tmp;
                    found = true;
                }
            }
            else if (IsType(name, isPm) && Csdb.StrMatch(code, name))
            {
                if (!found || CompareBase(cpath, best) > 0)
                {
                    best = cpath;
                    found = true;
                }
            }
        }

        if (found)
        {
            result = best;
        }
        return found;
    }

    private static bool IsType(string name, bool isPm) =>
        isPm ? Csdb.IsPublicationModule(name) : Csdb.IsDataModule(name);

    private static int CompareBase(string a, string b) =>
        string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);

    // --- File-name building -------------------------------------------------

    /// <summary>
    /// Append the issue/inwork and language fields to a CSDB code-name, mirroring
    /// the C logic shared by dmRef and pmRef handling.
    /// </summary>
    private string AppendIssueAndLanguage(string fname, XmlNode? issueInfo, XmlNode? language)
    {
        if (!_noIssue)
        {
            if (issueInfo != null)
            {
                string issueNumber = Str(issueInfo, "@issueNumber|@issno");
                string inWork = Str(issueInfo, "@inWork|@inwork");
                fname = $"{fname}_{issueNumber}-{(inWork.Length > 0 ? inWork : "00")}";
            }
            else if (language != null)
            {
                fname = $"{fname}_???-??";
            }
        }

        if (language != null)
        {
            string languageIsoCode = Str(language, "@languageIsoCode|@language").ToUpperInvariant();
            string countryIsoCode = Str(language, "@countryIsoCode|@country");
            fname = $"{fname}_{languageIsoCode}-{countryIsoCode}";
        }

        return fname;
    }

    // --- Duplicate-reference removal (replaces remdups*.xsl) ----------------

    /// <summary>
    /// Remove duplicate references (by serialized content) keeping the first
    /// occurrence, then prune empty pmEntry elements. This reproduces the
    /// observable effect of remdups1.xsl, remdups2.xsl and
    /// remove_empty_pmentries.xsl without requiring the EXSLT shim.
    /// </summary>
    private static void RemoveDuplicateRefs(XmlDocument doc)
    {
        if (doc.DocumentElement == null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var refs = doc.DocumentElement
            .SelectNodes(".//*[local-name()='dmRef' or local-name()='pmRef' or " +
                         "local-name()='refdm' or local-name()='refpm']")!
            .OfType<XmlElement>()
            .ToList();

        foreach (XmlElement r in refs)
        {
            string key = r.LocalName + "|" + r.OuterXml;
            if (!seen.Add(key))
            {
                r.ParentNode?.RemoveChild(r);
            }
        }

        // Prune empty pmEntry elements (no element children, or only a title),
        // repeatedly to handle nesting, mirroring remove_empty_pmentries.xsl.
        bool removedAny;
        do
        {
            removedAny = false;
            var entries = doc.DocumentElement
                .SelectNodes(".//*[local-name()='pmEntry' or local-name()='pmentry']")!
                .OfType<XmlElement>()
                .ToList();

            foreach (XmlElement e in entries)
            {
                XmlElement? lastEl = LastElementChild(e);
                if (lastEl == null || lastEl.LocalName == "pmEntryTitle" || lastEl.LocalName == "title")
                {
                    e.ParentNode?.RemoveChild(e);
                    removedAny = true;
                }
            }
        } while (removedAny);
    }

    // --- DOM helpers --------------------------------------------------------

    private static XmlElement? FindChild(XmlElement parent, string localName)
    {
        foreach (XmlNode n in parent.ChildNodes)
        {
            if (n is XmlElement el && el.LocalName == localName)
            {
                return el;
            }
        }
        return null;
    }

    private static XmlElement? LastElementChild(XmlElement parent)
    {
        for (XmlNode? n = parent.LastChild; n != null; n = n.PreviousSibling)
        {
            if (n is XmlElement el)
            {
                return el;
            }
        }
        return null;
    }

    private static XmlNode? XPath(XmlNode context, string xpath) => context.SelectSingleNode(xpath);

    /// <summary>
    /// Evaluate an XPath relative to <paramref name="context"/> and return the
    /// string value of the first match (attribute value or element text), or the
    /// empty string. Mirrors <c>first_xpath_string</c>.
    /// </summary>
    private static string Str(XmlNode? context, string xpath)
    {
        if (context == null)
        {
            return string.Empty;
        }
        XmlNode? n = context.SelectSingleNode(xpath);
        if (n == null)
        {
            return string.Empty;
        }
        return n.NodeType == XmlNodeType.Attribute ? n.Value ?? string.Empty : n.InnerText;
    }

    private static XmlElement CreateXInclude(XmlDocument doc, string href)
    {
        XmlElement xi = doc.CreateElement("xi", "include", "http://www.w3.org/2001/XInclude");
        xi.SetAttribute("href", href);
        return xi;
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
        stdout.WriteLine($"Usage: s1kd-{Name} [-d <dir>] [-I <path>] [-cDfilmNPpqRruvxh?] <pubmodule> [<dmodule>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -c, --containers      Flatten referenced container data modules.");
        stdout.WriteLine("  -D, --remove          Remove unresolved references.");
        stdout.WriteLine("  -d, --dir <dir>       Directory to start search in.");
        stdout.WriteLine("  -f, --overwrite       Overwrite publication module.");
        stdout.WriteLine("  -h, -?, --help        Show help/usage message.");
        stdout.WriteLine("  -I, --include <path>  Search <path> for referenced objects.");
        stdout.WriteLine("  -i, --ignore-issue    Always match the latest issue of an object found.");
        stdout.WriteLine("  -l, --list            Treat input as a list of objects.");
        stdout.WriteLine("  -m, --modify          Modiy references without flattening them.");
        stdout.WriteLine("  -N, --omit-issue      Assume issue/inwork numbers are omitted.");
        stdout.WriteLine("  -P, --only-pm-refs    Only flatten PM refs.");
        stdout.WriteLine("  -p, --simple          Output a simple, flat XML file.");
        stdout.WriteLine("  -q, --quiet           Quiet mode.");
        stdout.WriteLine("  -R, --recursively     Recursively flatten referenced PMs.");
        stdout.WriteLine("  -r, --recursive       Search directories recursively.");
        stdout.WriteLine("  -u, --unique          Remove duplicate references.");
        stdout.WriteLine("  -v, --verbose         Verbose output.");
        stdout.WriteLine("  -x, --use-xinclude    Use XInclude references.");
        stdout.WriteLine("      --version          Show version information.");
    }
}
