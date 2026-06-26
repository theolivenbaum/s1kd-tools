using System.Text;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-acronyms</c>: manage acronyms in S1000D data modules in one
/// of three ways:
/// <list type="bullet">
///   <item>Generate a list of the unique acronyms used across data modules
///         (text or XML output, optionally as a <c>definitionList</c> or
///         <c>table</c>).</item>
///   <item>Mark up acronyms automatically from an <c>.acronyms</c> list,
///         inserting <c>acronym</c> elements around matched terms.</item>
///   <item>Remove existing acronym markup (flatten to the term, or preformat to
///         "definition (term)").</item>
/// </list>
///
/// The imperative term-matching/insertion logic (<c>markupAcronymInNode</c>) is
/// ported directly onto the <see cref="XmlDocument"/> DOM. The supporting
/// transforms — extraction, dedup, sorting, type filtering, list/table
/// formatting, deletion, preformatting, the issue-3.0 downgrade and the
/// two-pass term/id resolution — are driven by the original stylesheets, run
/// through <see cref="XslCompiledTransform"/>. All of them are plain XSLT 1.0
/// (no EXSLT), so no extension-object shim is required. The originals are
/// embedded under <c>Resources/acronyms/</c>.
/// </summary>
public sealed class AcronymsTool : ITool
{
    public string Name => "acronyms";
    public string Description => "Manage acronyms in S1000D data modules.";
    public string Version => "2.0.0";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitNoList = 1;

    /* Default text nodes searched for acronyms (mirrors ACRO_MARKUP_XPATH). */
    private const string DefaultMarkupXPath =
        "//para/text()|//notePara/text()|//warningAndCautionPara/text()|" +
        "//attentionListItemPara/text()|//title/text()|//listItemTerm/text()|" +
        "//term/text()|//termTitle/text()|//emphasis/text()|//changeInline/text()|" +
        "//change/text()";

    /* Characters that must precede / follow a candidate to be an acronym term. */
    private const string PreAcronymDelim = " (/\"'\n";
    private const string PostAcronymDelim = " .,)/?!:\"'\n";

    private enum XmlFormatKind { Basic, DefList, Table }

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2 }

    private Verbosity _verbosity = Verbosity.Normal;
    private bool _prettyPrint;
    private int _minimumSpaces = 2;
    private XmlFormatKind _xmlFormat = XmlFormatKind.Basic;
    private bool _interactive;
    private bool _alwaysAsk;
    private bool _deferChoice;
    private bool _remDelete;
    private string _markupXPath = DefaultMarkupXPath;

    /// <summary>Default choices accumulated during interactive markup.</summary>
    private XmlDocument _defaultChoicesDoc = null!;
    private XmlElement _defaultChoices = null!;

    /// <summary>Where interactive prompts are read from (mirrors <c>stdin</c>).</summary>
    private TextReader _input = null!;

    /// <summary>Where interactive prompts are written to (mirrors <c>printf</c> to stdout).</summary>
    private TextWriter _promptOut = null!;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    /// <summary>Entry point used by the CLI; interactive prompts read from the
    /// process's standard input.</summary>
    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr) =>
        Run(args, stdout, stderr, Console.In);

    /// <summary>Overload allowing an interactive input reader to be injected (for
    /// testing the <c>-i</c>/<c>-I</c> prompting in-process).</summary>
    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, TextReader input)
    {
        _input = input;
        _promptOut = stdout;
        bool xmlOut = false;
        string? types = null;
        string outPath = "-";
        string? markup = null;
        bool overwrite = false;
        bool list = false;
        bool delete = false;
        bool preformat = false;
        bool markupXPathSet = false;

        _defaultChoicesDoc = XmlUtils.NewDocument();
        _defaultChoices = _defaultChoicesDoc.CreateElement("acronyms");
        _defaultChoicesDoc.AppendChild(_defaultChoices);

        var files = new List<string>();

        try
        {
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "-h" or "-?" or "--help":
                        ShowHelp(stdout);
                        return 0;
                    case "--version":
                        ShowVersion(stdout);
                        return 0;
                    case "-P" or "--preformat":
                        preformat = true;
                        break;
                    case "-p" or "--pretty":
                        _prettyPrint = true;
                        break;
                    case "-q" or "--quiet":
                        _verbosity--;
                        break;
                    case "-n" or "--width":
                        _minimumSpaces = ParseInt(NextArg(args, ref i, "-n", stderr));
                        break;
                    case "-x" or "--xml":
                        xmlOut = true;
                        break;
                    case "-D" or "--delete":
                        delete = true;
                        break;
                    case "-d" or "--deflist":
                        xmlOut = true;
                        _xmlFormat = XmlFormatKind.DefList;
                        break;
                    case "-t" or "--table":
                        xmlOut = true;
                        _xmlFormat = XmlFormatKind.Table;
                        break;
                    case "-T" or "--types":
                        types = NextArg(args, ref i, "-T", stderr);
                        break;
                    case "-o" or "--out":
                        outPath = NextArg(args, ref i, "-o", stderr);
                        break;
                    case "-m" or "--markup":
                        Csdb.FindConfig(Csdb.AcronymsFileName, out markup);
                        break;
                    case "-M" or "--acronym-list":
                        markup = NextArg(args, ref i, "-M", stderr);
                        break;
                    case "-i" or "--interactive":
                        _interactive = true;
                        break;
                    case "-I" or "--always-ask":
                        _interactive = true;
                        _alwaysAsk = true;
                        break;
                    case "-f" or "--overwrite":
                        overwrite = true;
                        break;
                    case "-l" or "--list":
                        list = true;
                        break;
                    case "-!" or "--defer-choice":
                        _interactive = true;
                        _deferChoice = true;
                        break;
                    case "-X" or "--select":
                    {
                        string xp = NextArg(args, ref i, "-X", stderr);
                        // Only the first -X is honoured (mirrors `if (!acro_markup_xpath)`).
                        if (!markupXPathSet)
                        {
                            _markupXPath = xp;
                            markupXPathSet = true;
                        }
                        break;
                    }
                    case "-v" or "--verbose":
                        _verbosity++;
                        break;
                    case "-^" or "--remove-deleted":
                        _remDelete = true;
                        break;
                    default:
                        if (a.StartsWith('-') && a.Length > 1 && a != "-")
                        {
                            // Unknown / libxml2 parser long options are accepted
                            // and ignored, matching getopt's lenient handling.
                            break;
                        }
                        files.Add(a);
                        break;
                }
            }

            if (preformat)
            {
                RunPreformat(files, list, overwrite, outPath, stdout, stderr);
            }
            else if (delete)
            {
                RunDelete(files, list, overwrite, outPath, stdout, stderr);
            }
            else if (markup != null)
            {
                RunMarkup(files, markup, list, overwrite, outPath, stdout, stderr);
            }
            else
            {
                RunFind(files, list, xmlOut, types, outPath, stdout, stderr);
            }
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }

        return 0;
    }

    /* ======================================================================
     * Find / list mode
     * ====================================================================== */

    private void RunFind(List<string> files, bool list, bool xmlOut, string? types,
        string outPath, TextWriter stdout, TextWriter stderr)
    {
        var doc = XmlUtils.NewDocument();
        XmlElement acronyms = doc.CreateElement("acronyms");
        doc.AppendChild(acronyms);

        if (files.Count == 0)
        {
            if (list)
            {
                FindAcronymsInList(acronyms, null, stderr);
            }
            else
            {
                FindAcronymsInFile(acronyms, "-", stderr);
            }
        }

        foreach (string f in files)
        {
            if (list)
            {
                FindAcronymsInList(acronyms, f, stderr);
            }
            else
            {
                FindAcronymsInFile(acronyms, f, stderr);
            }
        }

        doc = TransformDoc(doc, "acronyms/unique.xsl");

        if (types != null)
        {
            doc = TransformDoc(doc, "acronyms/types.xsl",
                ("types", types));
        }

        if (xmlOut)
        {
            doc = _xmlFormat switch
            {
                XmlFormatKind.DefList => TransformDoc(doc, "acronyms/list.xsl"),
                XmlFormatKind.Table => TransformDoc(doc, "acronyms/table.xsl"),
                _ => doc,
            };

            SaveXml(doc, outPath, _prettyPrint, stdout);
        }
        else
        {
            PrintAcronyms(doc.DocumentElement!, outPath, stdout);
        }
    }

    private void FindAcronymsInFile(XmlElement acronyms, string path, TextWriter stderr)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"s1kd-{Name}: INFO: Searching for acronyms in {path}...");
        }

        XmlDocument doc;
        try
        {
            doc = ReadInput(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read file: {path}");
            }
            return;
        }

        if (_remDelete)
        {
            XmlUtils.RemoveDeleteElements(doc);
        }

        XmlDocument result = TransformDoc(doc, "acronyms/acronyms.xsl");
        CombineAcronymLists(acronyms, result.DocumentElement!);
    }

    private void FindAcronymsInList(XmlElement acronyms, string? fname, TextWriter stderr)
    {
        foreach (string entry in ReadFileList(fname, stderr))
        {
            FindAcronymsInFile(acronyms, entry, stderr);
        }
    }

    /// <summary>Copy every <c>acronym</c> child of <paramref name="src"/> into
    /// <paramref name="dst"/> (mirrors <c>combineAcronymLists</c>).</summary>
    private static void CombineAcronymLists(XmlElement dst, XmlNode src)
    {
        XmlDocument owner = dst.OwnerDocument!;
        for (XmlNode? cur = src.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType == XmlNodeType.Element && cur.Name == "acronym")
            {
                dst.AppendChild(owner.ImportNode(cur, true));
            }
        }
    }

    /// <summary>Plain-text rendering of an acronyms document (mirrors <c>printAcronyms</c>).</summary>
    private void PrintAcronyms(XmlNode acronyms, string outPath, TextWriter stdout)
    {
        var sb = new StringBuilder();
        int longest = _prettyPrint ? LongestAcronymTerm(acronyms) : 0;

        for (XmlNode? cur = acronyms.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType != XmlNodeType.Element || cur.Name != "acronym")
            {
                continue;
            }

            var acr = (XmlElement)cur;
            string term = FindChild(acr, "acronymTerm")?.InnerText ?? string.Empty;
            string defn = FindChild(acr, "acronymDefinition")?.InnerText ?? string.Empty;
            string? type = acr.HasAttribute("acronymType") ? acr.GetAttribute("acronymType") : null;

            if (_prettyPrint)
            {
                int nspaces = longest - term.Length + _minimumSpaces;
                sb.Append(term);
                sb.Append(' ', nspaces);
                sb.Append(type ?? "    ");
                sb.Append(' ', _minimumSpaces);
                sb.Append(defn);
                sb.Append('\n');
            }
            else
            {
                sb.Append(term).Append('\t');
                sb.Append(type ?? "    ").Append('\t');
                sb.Append(defn).Append('\n');
            }
        }

        if (outPath == "-")
        {
            stdout.Write(sb.ToString());
        }
        else
        {
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        }
    }

    private static int LongestAcronymTerm(XmlNode acronyms)
    {
        int longest = 0;
        for (XmlNode? cur = acronyms.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType == XmlNodeType.Element && cur.Name == "acronym")
            {
                string term = FindChild((XmlElement)cur, "acronymTerm")?.InnerText ?? string.Empty;
                longest = Math.Max(longest, term.Length);
            }
        }
        return longest;
    }

    /* ======================================================================
     * Markup mode
     * ====================================================================== */

    private void RunMarkup(List<string> files, string markup, bool list, bool overwrite,
        string outPath, TextWriter stdout, TextWriter stderr)
    {
        XmlDocument acronymsDoc;
        try
        {
            acronymsDoc = XmlUtils.ReadDoc(markup);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read acronyms list: {markup}");
            }
            throw new ExitException(ExitNoList);
        }

        acronymsDoc = TransformDoc(acronymsDoc, "acronyms/sort.xsl");

        // The acronym list may be a terminology repository, otherwise use the root.
        XmlNode acronyms =
            acronymsDoc.SelectSingleNode("//terminologyRepository")
            ?? (XmlNode)acronymsDoc.DocumentElement!;

        if (files.Count == 0)
        {
            if (list)
            {
                MarkupAcronymsInList(null, acronyms, outPath, overwrite, stdout, stderr);
            }
            else
            {
                MarkupAcronymsInFile("-", acronyms, outPath, stdout, stderr);
            }
        }

        foreach (string f in files)
        {
            if (list)
            {
                MarkupAcronymsInList(f, acronyms, outPath, overwrite, stdout, stderr);
            }
            else if (overwrite)
            {
                MarkupAcronymsInFile(f, acronyms, f, stdout, stderr);
            }
            else
            {
                MarkupAcronymsInFile(f, acronyms, outPath, stdout, stderr);
            }
        }
    }

    private void MarkupAcronymsInList(string? fname, XmlNode acronyms, string outPath,
        bool overwrite, TextWriter stdout, TextWriter stderr)
    {
        foreach (string entry in ReadFileList(fname, stderr))
        {
            if (overwrite)
            {
                MarkupAcronymsInFile(entry, acronyms, entry, stdout, stderr);
            }
            else
            {
                MarkupAcronymsInFile(entry, acronyms, outPath, stdout, stderr);
            }
        }
    }

    private void MarkupAcronymsInFile(string path, XmlNode acronyms, string outPath,
        TextWriter stdout, TextWriter stderr)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"s1kd-{Name}: INFO: Marking up acronyms in {path}...");
        }

        XmlDocument doc;
        try
        {
            doc = ReadInput(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read file: {path}");
            }
            return;
        }

        MarkupAcronyms(doc, acronyms);

        // Two-pass term/id resolution (term.xsl then id.xsl), preserving the
        // original root attributes/doctype as the C does via xmlCopyDoc.
        doc = MatchAcronymTerms(doc);

        // Issue 3.0 modules have <idstatus> as the first element child of root.
        XmlElement? firstChild = FirstElementChild(doc.DocumentElement);
        if (firstChild != null && firstChild.Name == "idstatus")
        {
            doc = TransformDoc(doc, "acronyms/30.xsl");
        }

        SaveOrPrint(doc, outPath, stdout);
    }

    /// <summary>Walk every acronym definition and mark up matching terms in the
    /// document's text nodes (mirrors <c>markupAcronyms</c>).</summary>
    private void MarkupAcronyms(XmlDocument doc, XmlNode acronyms)
    {
        for (XmlNode? cur = acronyms.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType != XmlNodeType.Element ||
                (cur.Name != "acronym" && cur.Name != "terminologySpec"))
            {
                continue;
            }

            XmlNode? termNode = cur.SelectSingleNode("acronymTerm|terminologyTerm");
            string? term = termNode?.InnerText;
            if (string.IsNullOrEmpty(term))
            {
                continue;
            }

            XmlNodeList? nodes = doc.SelectNodes(_markupXPath);
            if (nodes == null)
            {
                continue;
            }

            // Snapshot, since the node set is mutated while marking up.
            var textNodes = new List<XmlNode>(nodes.Count);
            foreach (XmlNode n in nodes)
            {
                textNodes.Add(n);
            }

            foreach (XmlNode n in textNodes)
            {
                MarkupAcronymInNode(n, cur, term);
            }
        }
    }

    /// <summary>Mirror of the C <c>isAcronymTerm</c> boundary test.</summary>
    private static bool IsAcronymTerm(string content, int contentLen, int i, string term, int termLen)
    {
        char s = i == 0 ? ' ' : content[i - 1];
        char e = i + termLen >= contentLen ? ' ' : content[i + termLen];

        return PreAcronymDelim.IndexOf(s) >= 0 &&
               string.CompareOrdinal(content.Substring(i, termLen), term) == 0 &&
               PostAcronymDelim.IndexOf(e) >= 0;
    }

    /// <summary>Insert acronym markup around every occurrence of a term in a
    /// single text node (mirrors <c>markupAcronymInNode</c>).</summary>
    private void MarkupAcronymInNode(XmlNode node, XmlNode acronym, string term)
    {
        string? content = node.Value;
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        XmlDocument doc = node.OwnerDocument!;
        int termLen = term.Length;
        XmlNode current = node;
        int contentLen = content.Length;

        int i = 0;
        while (i + termLen <= contentLen)
        {
            if (IsAcronymTerm(content, contentLen, i, term, termLen))
            {
                string s1 = content.Substring(0, i);
                string s2 = content.Substring(i + termLen);

                XmlNode? acr = acronym;
                if (_interactive)
                {
                    acr = ChooseAcronym(acronym, term, content);
                }

                current.Value = s1;

                XmlNode parent = current.ParentNode!;
                XmlNode inserted;
                if (acr != null)
                {
                    inserted = doc.ImportNode(acr, true);
                    parent.InsertAfter(inserted, current);
                }
                else
                {
                    XmlElement ignored = doc.CreateElement("ignoredAcronym");
                    ignored.AppendChild(doc.CreateTextNode(term));
                    parent.InsertAfter(ignored, current);
                    inserted = ignored;
                }

                XmlText rest = doc.CreateTextNode(s2);
                parent.InsertAfter(rest, inserted);

                current = rest;
                content = s2;
                contentLen = content.Length;
                i = 0;
            }
            else
            {
                ++i;
            }
        }
    }

    /// <summary>
    /// Resolve the acronym to insert at a particular occurrence (mirrors
    /// <c>chooseAcronym</c>). Honours previously-recorded default choices and,
    /// when <c>--defer-choice</c> is set, emits a <c>chooseAcronym</c> element
    /// listing all candidate definitions. Otherwise, when there is more than one
    /// candidate definition (or <c>-I</c> is in effect), the user is prompted via
    /// <see cref="_input"/> to choose a definition, ignore the occurrence, or
    /// apply the choice to all remaining occurrences of the term.
    /// </summary>
    private XmlNode? ChooseAcronym(XmlNode acronym, string term, string content)
    {
        XmlNode? chosen = acronym;
        bool noDefault = true;

        // Look first among recorded default choices.
        XmlNode? defaultMatch = SelectAcronymByTerm(_defaultChoices, term);
        XmlNodeList candidates;
        if (defaultMatch != null)
        {
            noDefault = false;
            chosen = (defaultMatch is XmlElement de && de.HasAttribute("ignore"))
                ? null
                : defaultMatch;
            candidates = SelectAcronymsByTerm(_defaultChoices.OwnerDocument!, term);
        }
        else
        {
            candidates = SelectAcronymsByTerm(acronym.OwnerDocument!, term);
        }

        if (_deferChoice)
        {
            XmlDocument doc = acronym.OwnerDocument!;
            XmlElement choose = doc.CreateElement("chooseAcronym");
            foreach (XmlNode c in candidates)
            {
                choose.AppendChild(doc.ImportNode(c, true));
            }
            return choose;
        }

        if (noDefault && (_alwaysAsk || candidates.Count > 1))
        {
            chosen = PromptForAcronym(candidates, term, content);
        }

        return chosen;
    }

    /// <summary>Interactively prompt the user to choose how to mark up one
    /// occurrence of an acronym term (mirrors the prompting block in the C
    /// <c>chooseAcronym</c>). Returns the chosen acronym node, or <c>null</c> to
    /// ignore the occurrence. Reads character-by-character from <see cref="_input"/>;
    /// when input is exhausted (EOF / non-interactive) the occurrence is ignored,
    /// matching the C behaviour where <c>getchar()</c> returns EOF.</summary>
    private XmlNode? PromptForAcronym(XmlNodeList candidates, string term, string content)
    {
        _promptOut.Write($"Found acronym term {term} in the following context:\n\n");
        _promptOut.Write($"{content}\n\n");
        _promptOut.Write("Choose definition:\n");

        for (int i = 0; i < candidates.Count && i < 9; ++i)
        {
            XmlNode? definition = candidates[i]!.SelectSingleNode("acronymDefinition");
            _promptOut.Write($"{i + 1}) {definition?.InnerText ?? string.Empty}\n");
        }

        _promptOut.Write("s) Ignore this one\n");
        _promptOut.Write("\n");
        _promptOut.Write("Add 'a' after your choice to apply to all remaining occurrences of this acronym\n");
        _promptOut.Write("\n");
        _promptOut.Write("Choice: ");
        _promptOut.Flush();

        // First character: a digit selects that definition; anything else (or
        // EOF) ignores the occurrence.
        int c = _input.Read();
        XmlNode? chosen;
        if (c >= '0' && c <= '9')
        {
            int index = c - '0' - 1;
            chosen = index >= 0 && index < candidates.Count ? candidates[index] : null;
        }
        else
        {
            chosen = null;
        }

        // If the choice is followed by 'a', apply it to all remaining
        // occurrences of this acronym (record a default choice).
        if (_input.Peek() == 'a')
        {
            _input.Read();
            if (chosen != null)
            {
                _defaultChoices.AppendChild(_defaultChoices.OwnerDocument!.ImportNode(chosen, true));
            }
            else
            {
                XmlDocument doc = _defaultChoices.OwnerDocument!;
                XmlElement n = doc.CreateElement("acronym");
                n.SetAttribute("ignore", "1");
                XmlElement t = doc.CreateElement("acronymTerm");
                t.AppendChild(doc.CreateTextNode(term));
                n.AppendChild(t);
                _defaultChoices.AppendChild(n);
            }
        }

        // Consume the rest of the line.
        int rest;
        while ((rest = _input.Read()) != -1 && rest != '\n')
        {
            // discard
        }

        _promptOut.Write('\n');

        return chosen;
    }

    private static XmlNode? SelectAcronymByTerm(XmlElement root, string term)
    {
        foreach (XmlNode n in SelectAcronymsByTerm(root.OwnerDocument!, term))
        {
            return n;
        }
        return null;
    }

    private static XmlNodeList SelectAcronymsByTerm(XmlDocument doc, string term)
    {
        // XPath cannot embed arbitrary strings safely; match in code instead.
        var nodes = new List<XmlNode>();
        XmlNodeList? all = doc.SelectNodes("//acronym");
        if (all != null)
        {
            foreach (XmlNode n in all)
            {
                XmlNode? t = n.SelectSingleNode("acronymTerm");
                if (t != null && t.InnerText == term)
                {
                    nodes.Add(n);
                }
            }
        }
        return new NodeListWrapper(nodes);
    }

    /// <summary>Two-pass term/id resolution (mirrors <c>matchAcronymTerms</c>):
    /// apply term.xsl then id.xsl, then graft the transformed root back into a
    /// copy of the original document so its doctype/root attributes survive.</summary>
    private XmlDocument MatchAcronymTerms(XmlDocument doc)
    {
        var orig = (XmlDocument)doc.CloneNode(true);

        XmlDocument res = TransformDoc(doc, "acronyms/term.xsl");
        res = TransformDoc(res, "acronyms/id.xsl");

        XmlNode importedRoot = orig.ImportNode(res.DocumentElement!, true);
        orig.ReplaceChild(importedRoot, orig.DocumentElement!);
        return orig;
    }

    /* ======================================================================
     * Delete / preformat modes
     * ====================================================================== */

    private void RunDelete(List<string> files, bool list, bool overwrite, string outPath,
        TextWriter stdout, TextWriter stderr)
    {
        TransformFiles(files, list, overwrite, outPath, "acronyms/delete.xsl",
            "Deleting acronym markup in", stdout, stderr);
    }

    private void RunPreformat(List<string> files, bool list, bool overwrite, string outPath,
        TextWriter stdout, TextWriter stderr)
    {
        TransformFiles(files, list, overwrite, outPath, "acronyms/prefmt.xsl",
            "Preformatting acronyms in", stdout, stderr);
    }

    private void TransformFiles(List<string> files, bool list, bool overwrite, string outPath,
        string stylesheet, string info, TextWriter stdout, TextWriter stderr)
    {
        if (files.Count == 0)
        {
            if (list)
            {
                TransformInList(null, overwrite, outPath, stylesheet, info, stdout, stderr);
            }
            else
            {
                TransformInFile("-", outPath, stylesheet, info, stdout, stderr);
            }
        }

        foreach (string f in files)
        {
            if (list)
            {
                TransformInList(f, overwrite, outPath, stylesheet, info, stdout, stderr);
            }
            else if (overwrite)
            {
                TransformInFile(f, f, stylesheet, info, stdout, stderr);
            }
            else
            {
                TransformInFile(f, outPath, stylesheet, info, stdout, stderr);
            }
        }
    }

    private void TransformInList(string? fname, bool overwrite, string outPath,
        string stylesheet, string info, TextWriter stdout, TextWriter stderr)
    {
        foreach (string entry in ReadFileList(fname, stderr))
        {
            if (overwrite)
            {
                TransformInFile(entry, entry, stylesheet, info, stdout, stderr);
            }
            else
            {
                TransformInFile(entry, outPath, stylesheet, info, stdout, stderr);
            }
        }
    }

    private void TransformInFile(string path, string outPath, string stylesheet, string info,
        TextWriter stdout, TextWriter stderr)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"s1kd-{Name}: INFO: {info} {path}...");
        }

        XmlDocument doc;
        try
        {
            doc = ReadInput(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read file: {path}");
            }
            return;
        }

        // The C grafts the transformed root back into the original document so the
        // doctype/root attributes survive (transformDoc).
        var orig = (XmlDocument)doc.CloneNode(true);
        XmlDocument res = TransformDoc(doc, stylesheet);
        XmlNode importedRoot = orig.ImportNode(res.DocumentElement!, true);
        orig.ReplaceChild(importedRoot, orig.DocumentElement!);

        SaveOrPrint(orig, outPath, stdout);
    }

    /* ======================================================================
     * Shared helpers
     * ====================================================================== */

    /// <summary>Read a file or stdin (when path is "-").</summary>
    private static XmlDocument ReadInput(string path) =>
        path == "-"
            ? XmlUtils.ReadStream(Console.OpenStandardInput())
            : XmlUtils.ReadDoc(path);

    /// <summary>Yield the file names in a list file (or stdin), taking the text
    /// up to the first tab/cr/nl on each line (mirrors <c>strtok</c>).</summary>
    private IEnumerable<string> ReadFileList(string? fname, TextWriter stderr)
    {
        TextReader reader;
        if (fname != null)
        {
            try
            {
                reader = new StreamReader(fname);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read list file: {fname}");
                }
                yield break;
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
                int cut = line.IndexOfAny(new[] { '\t', '\r', '\n' });
                string entry = cut < 0 ? line : line[..cut];
                if (entry.Length == 0)
                {
                    continue;
                }
                yield return entry;
            }
        }
        finally
        {
            if (fname != null)
            {
                reader.Dispose();
            }
        }
    }

    private void SaveOrPrint(XmlDocument doc, string outPath, TextWriter stdout)
    {
        if (outPath != "-")
        {
            XmlUtils.SaveDoc(doc, outPath);
        }
        else
        {
            stdout.Write(XmlUtils.ToXmlString(doc));
            stdout.Write('\n');
        }
    }

    private void SaveXml(XmlDocument doc, string outPath, bool pretty, TextWriter stdout)
    {
        string xml = pretty ? ToPrettyXmlString(doc) : XmlUtils.ToXmlString(doc);
        if (outPath != "-")
        {
            File.WriteAllText(outPath, xml, new UTF8Encoding(false));
        }
        else
        {
            stdout.Write(xml);
            stdout.Write('\n');
        }
    }

    private static string ToPrettyXmlString(XmlDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
        };
        using var ms = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(ms, settings))
        {
            doc.Save(writer);
        }
        return new UTF8Encoding(false).GetString(ms.ToArray());
    }

    private static XmlElement? FirstElementChild(XmlNode? node)
    {
        for (XmlNode? n = node?.FirstChild; n != null; n = n.NextSibling)
        {
            if (n is XmlElement el)
            {
                return el;
            }
        }
        return null;
    }

    private static XmlNode? FindChild(XmlElement parent, string name)
    {
        for (XmlNode? cur = parent.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType == XmlNodeType.Element && cur.Name == name)
            {
                return cur;
            }
        }
        return null;
    }

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: {opt} requires an argument");
            throw new ExitException(2);
        }
        return args[i];
    }

    private static int ParseInt(string s) =>
        int.TryParse(s, out int n) ? n : 0;

    /* ----- XSLT plumbing ----- */

    private static XmlDocument TransformDoc(XmlDocument doc, string resourcePath,
        params (string name, string value)[] parameters)
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

        XsltArgumentList? argList = null;
        if (parameters.Length > 0)
        {
            argList = new XsltArgumentList();
            foreach (var (name, value) in parameters)
            {
                argList.AddParam(name, string.Empty, value);
            }
        }

        var output = XmlUtils.NewDocument();
        using (var ms = new MemoryStream())
        {
            var writerSettings = new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Auto,
                OmitXmlDeclaration = true,
            };
            using (XmlWriter writer = XmlWriter.Create(ms, writerSettings))
            {
                xslt.Transform(doc, argList, writer);
            }
            ms.Position = 0;
            using XmlReader resultReader = XmlReader.Create(ms, readerSettings);
            output.Load(resultReader);
        }

        return output;
    }

    /* ----- help / version ----- */

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage:");
        stdout.WriteLine($"  s1kd-{Name} -h?");
        stdout.WriteLine($"  s1kd-{Name} [-dlpqtvx^] [-n <#>] [-o <file>] [-T <types>] [<dmodule>...]");
        stdout.WriteLine($"  s1kd-{Name} [-flqv] [-i|-I|-!] [-m|-M <list>] [-o <file>] [-X <xpath>] [<dmodule>...]");
        stdout.WriteLine($"  s1kd-{Name} [-D|-P] [-flqv] [-o <file>] [<dmodule>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -D, --delete               Remove acronym markup.");
        stdout.WriteLine("  -d, --deflist              Format XML output as definitionList.");
        stdout.WriteLine("  -f, --overwrite            Overwrite data modules when marking up acronyms.");
        stdout.WriteLine("  -h, -?, --help             Show usage message.");
        stdout.WriteLine("  -I, --always-ask           Prompt for all acronyms in interactive mode.");
        stdout.WriteLine("  -i, --interactive          Markup acronyms in interactive mode.");
        stdout.WriteLine("  -l, --list                 Input is a list of file names.");
        stdout.WriteLine("  -M, --acronym-list <list>  Markup acronyms from specified list.");
        stdout.WriteLine("  -m, --markup               Markup acronyms from .acronyms file.");
        stdout.WriteLine("  -n, --width <#>            Minimum spaces after term in pretty printed output.");
        stdout.WriteLine("  -o, --out <file>           Output to <file> instead of stdout.");
        stdout.WriteLine("  -P, --preformat            Remove acronym markup by preformatting it.");
        stdout.WriteLine("  -p, --pretty               Pretty print text/XML output.");
        stdout.WriteLine("  -q, --quiet                Quiet mode.");
        stdout.WriteLine("  -T, --types <types>        Only search for acronyms of these types.");
        stdout.WriteLine("  -t, --table                Format XML output as table.");
        stdout.WriteLine("  -v, --verbose              Verbose output.");
        stdout.WriteLine("  -X, --select <xpath>       Use custom XPath to markup elements.");
        stdout.WriteLine("  -x, --xml                  Output XML instead of text.");
        stdout.WriteLine("  -^, --remove-deleted       List acronyms with elements marked as \"delete\" removed.");
        stdout.WriteLine("  --version                  Show version information.");
        stdout.WriteLine("  <dmodule>                  Data module(s) to process.");
    }

    /// <summary>Minimal <see cref="XmlNodeList"/> over a fixed list of nodes.</summary>
    private sealed class NodeListWrapper(List<XmlNode> nodes) : XmlNodeList
    {
        public override int Count => nodes.Count;

        public override XmlNode? Item(int index) =>
            index >= 0 && index < nodes.Count ? nodes[index] : null;

        public override System.Collections.IEnumerator GetEnumerator() => nodes.GetEnumerator();
    }
}
