using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-index</c>: flag index keywords in a data module based on a
/// user-defined list of terms (the <c>.indexflags</c> file). Mirrors the C
/// tool's option set, exit codes and output: <c>indexFlag</c> elements are
/// inserted immediately after each matched term inside <c>//para</c> text.
///
/// The C tool relies on two trivial identity-style XSLT transforms
/// (<c>delete.xsl</c> and <c>iss30.xsl</c>). They are reimplemented here in the
/// <see cref="XmlDocument"/> DOM (no EXSLT is involved), so no
/// <see cref="System.Xml.Xsl.XslCompiledTransform"/> is needed. The originals
/// are still embedded under <c>Resources/index/</c> for reference.
/// </summary>
public sealed class IndexTool : ITool
{
    public string Name => "index";
    public string Description => "Flag index keywords in a data module.";
    public string Version => "1.10.0";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitNoList = 1;

    /* Term delimiters (mirror PRE_TERM_DELIM / POST_TERM_DELIM). */
    private const string PreTermDelim = " ";
    private const string PostTermDelim = " .,";

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2 }

    private Verbosity _verbosity = Verbosity.Normal;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool overwrite = false;
        bool ignorecase = false;
        bool delflags = false;
        bool list = false;
        XmlDocument? indexDoc = null;

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
                    case "-D" or "--delete":
                        delflags = true;
                        break;
                    case "-f" or "--overwrite":
                        overwrite = true;
                        break;
                    case "-I" or "--indexflags":
                    {
                        string fname = NextArg(args, ref i, "-I", stderr);
                        // Only the first -I is honoured (mirrors `if (!index_doc)`).
                        indexDoc ??= ReadIndexFlags(fname, stderr);
                        break;
                    }
                    case "-i" or "--ignore-case":
                        ignorecase = true;
                        break;
                    case "-l" or "--list":
                        list = true;
                        break;
                    case "-q" or "--quiet":
                        _verbosity--;
                        break;
                    case "-v" or "--verbose":
                        _verbosity++;
                        break;
                    default:
                        if (a.StartsWith('-') && a.Length > 1 && a != "-")
                        {
                            // Unknown / parser long options (--dtdload, --huge, …)
                            // are accepted and ignored, matching getopt's lenient
                            // handling of the LIBXML2_PARSE_LONGOPT set.
                            break;
                        }
                        files.Add(a);
                        break;
                }
            }

            // Load the default .indexflags when none was supplied and we are not
            // simply deleting flags.
            if (indexDoc == null && !delflags)
            {
                Csdb.FindConfig(Csdb.IndexFlagsFileName, out string fname);
                indexDoc = ReadIndexFlags(fname, stderr);
            }

            if (files.Count > 0)
            {
                foreach (string f in files)
                {
                    if (list)
                    {
                        HandleList(f, delflags, indexDoc, overwrite, ignorecase, stdout, stderr);
                    }
                    else if (delflags)
                    {
                        DeleteIndexFlags(f, overwrite, stdout, stderr);
                    }
                    else
                    {
                        GenIndex(f, indexDoc!, overwrite, ignorecase, stdout, stderr);
                    }
                }
            }
            else if (list)
            {
                HandleList(null, delflags, indexDoc, overwrite, ignorecase, stdout, stderr);
            }
            else if (delflags)
            {
                DeleteIndexFlags("-", false, stdout, stderr);
            }
            else
            {
                GenIndex("-", indexDoc!, false, ignorecase, stdout, stderr);
            }
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }

        return 0;
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

    /// <summary>
    /// Read the index-flags document, or exit with <see cref="ExitNoList"/> when
    /// it cannot be read (mirrors <c>read_index_flags</c>).
    /// </summary>
    private XmlDocument ReadIndexFlags(string fname, TextWriter stderr)
    {
        try
        {
            return XmlUtils.ReadDoc(fname);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read index flags from {fname}");
            }
            throw new ExitException(ExitNoList);
        }
    }

    private void HandleList(string? path, bool delflags, XmlDocument? indexDoc,
        bool overwrite, bool ignorecase, TextWriter stdout, TextWriter stderr)
    {
        TextReader reader;
        if (path != null)
        {
            try
            {
                reader = new StreamReader(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read list: {path}");
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
                // strtok(line, "\t\r\n") -> take up to the first tab/cr/nl.
                int cut = line.IndexOfAny(new[] { '\t', '\r', '\n' });
                string entry = cut < 0 ? line : line[..cut];
                if (entry.Length == 0)
                {
                    continue;
                }

                if (delflags)
                {
                    DeleteIndexFlags(entry, overwrite, stdout, stderr);
                }
                else
                {
                    GenIndex(entry, indexDoc!, overwrite, ignorecase, stdout, stderr);
                }
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

    /* ----- flag generation ----- */

    /// <summary>
    /// Return the lowest defined level in an indexFlag (mirrors
    /// <c>last_level</c>). This is the term matched against the text.
    /// </summary>
    private static string? LastLevel(XmlElement flag)
    {
        foreach (string attr in new[] { "indexLevelFour", "indexLevelThree", "indexLevelTwo", "indexLevelOne" })
        {
            if (flag.HasAttribute(attr))
            {
                return flag.GetAttribute(attr);
            }
        }
        return null;
    }

    /// <summary>Mirror of the C <c>is_term</c> boundary test.</summary>
    private static bool IsTerm(string content, int contentLen, int i, string term, int termLen, bool ignorecase)
    {
        char s = i == 0 ? ' ' : content[i - 1];
        char e = i + termLen >= contentLen - 1 ? ' ' : content[i + termLen];

        bool matches = ignorecase
            ? string.Compare(content, i, term, 0, termLen, StringComparison.OrdinalIgnoreCase) == 0
            : string.CompareOrdinal(content.Substring(i, termLen), term) == 0;

        return PreTermDelim.IndexOf(s) >= 0 && matches && PostTermDelim.IndexOf(e) >= 0;
    }

    /// <summary>
    /// Insert indexFlag copies after each matched term in a single text node
    /// (mirrors <c>gen_index_node</c>).
    /// </summary>
    private static void GenIndexNode(XmlNode node, XmlElement flag, bool ignorecase)
    {
        string? term = LastLevel(flag);
        if (string.IsNullOrEmpty(term))
        {
            return;
        }

        XmlDocument doc = node.OwnerDocument!;
        XmlNode current = node;
        string content = current.Value ?? string.Empty;
        int contentLen = content.Length;
        int termLen = term.Length;

        int i = 0;
        while (i + termLen <= contentLen)
        {
            if (IsTerm(content, contentLen, i, term, termLen, ignorecase))
            {
                string s1 = content.Substring(0, i + termLen);
                string s2 = content.Substring(i + termLen);

                current.Value = s1;

                XmlElement flagCopy = (XmlElement)doc.ImportNode(flag, true);
                XmlNode parent = current.ParentNode!;
                parent.InsertAfter(flagCopy, current);

                XmlText rest = doc.CreateTextNode(s2);
                parent.InsertAfter(rest, flagCopy);

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

    /// <summary>Flag a single term in every applicable text node (mirrors <c>gen_index_flag</c>).</summary>
    private static void GenIndexFlag(XmlElement flag, XmlDocument doc, bool ignorecase)
    {
        // //para/text() — collect first; the node set is modified during flagging.
        XmlNodeList? nodes = doc.SelectNodes("//para/text()");
        if (nodes == null)
        {
            return;
        }

        var textNodes = new List<XmlNode>(nodes.Count);
        foreach (XmlNode n in nodes)
        {
            textNodes.Add(n);
        }

        foreach (XmlNode n in textNodes)
        {
            GenIndexNode(n, flag, ignorecase);
        }
    }

    /// <summary>Insert indexFlags for each term in the index document (mirrors <c>gen_index</c>).</summary>
    private void GenIndex(string path, XmlDocument indexDoc, bool overwrite, bool ignorecase,
        TextWriter stdout, TextWriter stderr)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"s1kd-{Name}: INFO: Adding index flags to {path}...");
        }

        XmlDocument doc;
        try
        {
            doc = path == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read file: {path}");
            }
            return;
        }

        XmlNodeList? flags = indexDoc.SelectNodes("//indexFlag");
        if (flags != null)
        {
            foreach (XmlNode flag in flags)
            {
                if (flag is XmlElement el)
                {
                    GenIndexFlag(el, doc, ignorecase);
                }
            }
        }

        // Issue 3.0 modules have <idstatus> as the first element child of root.
        XmlElement? firstChild = FirstElementChild(doc.DocumentElement);
        if (firstChild != null && firstChild.Name == "idstatus")
        {
            ConvertToIss30(doc);
        }

        SaveOrPrint(doc, path, overwrite, stdout);
    }

    /// <summary>Delete the current index flags from a module (mirrors <c>delete_index_flags</c>).</summary>
    private void DeleteIndexFlags(string path, bool overwrite, TextWriter stdout, TextWriter stderr)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"s1kd-{Name}: INFO: Deleting index flags from {path}...");
        }

        XmlDocument doc;
        try
        {
            doc = path == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read file: {path}");
            }
            return;
        }

        // delete.xsl: identity transform that drops every indexFlag element.
        foreach (XmlElement el in SelectElements(doc, "//indexFlag"))
        {
            el.ParentNode?.RemoveChild(el);
        }

        SaveOrPrint(doc, path, overwrite, stdout);
    }

    /// <summary>
    /// Convert issue 4.x index flags to the issue 3.0 form (mirrors
    /// <c>iss30.xsl</c>): rename <c>indexFlag</c> to <c>indxflag</c> and the
    /// <c>indexLevel{One..Four}</c> attributes to <c>ref{1..4}</c>.
    /// </summary>
    private static void ConvertToIss30(XmlDocument doc)
    {
        foreach (XmlElement flag in SelectElements(doc, "//indexFlag"))
        {
            XmlElement repl = doc.CreateElement("indxflag");

            foreach (var (from, to) in new[]
                     {
                         ("indexLevelOne", "ref1"),
                         ("indexLevelTwo", "ref2"),
                         ("indexLevelThree", "ref3"),
                         ("indexLevelFour", "ref4"),
                     })
            {
                if (flag.HasAttribute(from))
                {
                    repl.SetAttribute(to, flag.GetAttribute(from));
                }
            }

            // Copy any remaining attributes verbatim.
            foreach (XmlAttribute attr in flag.Attributes)
            {
                if (attr.Name is not ("indexLevelOne" or "indexLevelTwo" or "indexLevelThree" or "indexLevelFour"))
                {
                    repl.SetAttribute(attr.Name, attr.Value);
                }
            }

            // Move child nodes across (indexFlags are normally empty).
            while (flag.FirstChild != null)
            {
                XmlNode child = flag.FirstChild;
                flag.RemoveChild(child);
                repl.AppendChild(child);
            }

            flag.ParentNode?.ReplaceChild(repl, flag);
        }
    }

    private void SaveOrPrint(XmlDocument doc, string path, bool overwrite, TextWriter stdout)
    {
        if (overwrite && path != "-")
        {
            XmlUtils.SaveDoc(doc, path);
        }
        else
        {
            stdout.Write(XmlUtils.ToXmlString(doc));
            stdout.Write('\n');
        }
    }

    /* ----- small DOM helpers ----- */

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

    private static List<XmlElement> SelectElements(XmlDocument doc, string xpath)
    {
        var result = new List<XmlElement>();
        XmlNodeList? nodes = doc.SelectNodes(xpath);
        if (nodes != null)
        {
            foreach (XmlNode n in nodes)
            {
                if (n is XmlElement el)
                {
                    result.Add(el);
                }
            }
        }
        return result;
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
        stdout.WriteLine($"  s1kd-{Name} [-I <index>] [-filqv] [<module>...]");
        stdout.WriteLine($"  s1kd-{Name} -D [-filqv] [<module>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -D, --delete              Delete current index flags.");
        stdout.WriteLine("  -f, --overwrite           Overwrite input module(s).");
        stdout.WriteLine("  -h, -?, --help            Show help/usage message.");
        stdout.WriteLine("  -I, --indexflags <index>  Specify a custom .indexflags file");
        stdout.WriteLine("  -i, --ignore-case         Ignore case when flagging terms.");
        stdout.WriteLine("  -l, --list                Input is a list of file names.");
        stdout.WriteLine("  -q, --quiet               Quiet mode.");
        stdout.WriteLine("  -v, --verbose             Verbose output.");
        stdout.WriteLine("  --version                 Show version information.");
    }
}
