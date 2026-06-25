using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-syncrefs</c>: synchronize references in a data module.
/// Copies all external references (dmRef/refdm, pmRef/reftp, externalPubRef)
/// found within the content of a data module, dedupes and sorts them, and uses
/// them to (re)generate the <c>&lt;refs&gt;</c> element. If a <c>&lt;refs&gt;</c>
/// element already exists it is replaced.
/// </summary>
public sealed class SyncrefsTool : ITool
{
    public string Name => "syncrefs";
    public string Description => "Synchronize references in a data module.";
    public string Version => "1.9.0";

    // Order of references; used as a prefix in the sort key (matches the C defines).
    private const string DM = "0"; // dmRef
    private const string PM = "1"; // pmRef
    private const string EP = "2"; // externalPubRef

    private const int ExitInvalidDm = 1;
    // EXIT_MAX_REFS (2) from the C source is unreachable here: the .NET list grows
    // automatically rather than bounding references against a fixed buffer.

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2 }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool onlyDelete = false;
        bool overwrite = false;
        bool islist = false;
        var verbosity = Verbosity.Normal;
        string outPath = "-";
        var files = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return 0;
                case "--version":
                    stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
                    return 0;
                case "-d" or "--delete":
                    onlyDelete = true;
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-l" or "--list":
                    islist = true;
                    break;
                case "-o" or "--out":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -o requires an argument"); return 2; }
                    outPath = args[i];
                    break;
                case "-q" or "--quiet":
                    if (verbosity > Verbosity.Quiet) verbosity--;
                    break;
                case "-v" or "--verbose":
                    if (verbosity < Verbosity.Verbose) verbosity++;
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return 2;
                    }
                    files.Add(a);
                    break;
            }
        }

        int status = 0;
        if (files.Count > 0)
        {
            foreach (string f in files)
            {
                int r = islist
                    ? SyncRefsList(f, outPath, overwrite, onlyDelete, verbosity, stdout, stderr)
                    : SyncRefsFile(f, outPath, overwrite, onlyDelete, verbosity, stdout, stderr);
                if (r != 0) status = r;
            }
        }
        else if (islist)
        {
            status = SyncRefsList(null, outPath, overwrite, onlyDelete, verbosity, stdout, stderr);
        }
        else
        {
            // Default: read single data module from stdin; never overwrite stdin.
            status = SyncRefsFile("-", outPath, false, onlyDelete, verbosity, stdout, stderr);
        }

        return status;
    }

    private int SyncRefsList(string? path, string outPath, bool overwrite, bool onlyDelete,
        Verbosity verbosity, TextWriter stdout, TextWriter stderr)
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
                if (verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{Name}: ERROR: Could not read list: {path}");
                }
                return 0;
            }
        }
        else
        {
            reader = Console.In;
        }

        int status = 0;
        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // Mirror strtok(line, "\t\r\n"): take the first token.
                string trimmed = line.Split('\t', '\r', '\n')[0];
                if (trimmed.Length == 0) continue;
                int r = SyncRefsFile(trimmed, outPath, overwrite, onlyDelete, verbosity, stdout, stderr);
                if (r != 0) status = r;
            }
        }
        finally
        {
            if (path != null) reader.Dispose();
        }

        return status;
    }

    private int SyncRefsFile(string path, string outPath, bool overwrite, bool onlyDelete,
        Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        if (verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine(onlyDelete
                ? $"{Name}: INFO: Deleting refs table in {path}..."
                : $"{Name}: INFO: Synchronizing references in {path}...");
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
            // The C tool silently returns when read_xml_doc fails.
            return 0;
        }

        XmlElement? dmodule = doc.DocumentElement;
        if (dmodule == null)
        {
            return 0;
        }

        int rc = SyncRefs(dmodule, onlyDelete, verbosity, stderr);
        if (rc != 0)
        {
            return rc;
        }

        if (overwrite && path != "-")
        {
            XmlUtils.SaveDoc(doc, path);
        }
        else if (outPath != "-")
        {
            XmlUtils.SaveDoc(doc, outPath);
        }
        else
        {
            stdout.Write(XmlUtils.ToXmlString(doc));
            stdout.Write('\n');
        }

        return 0;
    }

    /// <summary>
    /// Replace the <c>&lt;refs&gt;</c> element of the data module with one
    /// rebuilt from the references found in its content. Returns an exit code
    /// (0 on success; <see cref="ExitInvalidDm"/> for an invalid module).
    /// </summary>
    private int SyncRefs(XmlElement dmodule, bool onlyDelete, Verbosity verbosity, TextWriter stderr)
    {
        XmlElement? content = FindChild(dmodule, "content");
        if (content == null)
        {
            if (!onlyDelete)
            {
                if (verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{Name}: ERROR: Invalid data module.");
                }
                return ExitInvalidDm;
            }
            return 0;
        }

        XmlElement? oldRefs = FindChild(content, "refs");
        XmlNode? refgrp = null;

        if (oldRefs != null)
        {
            refgrp = XmlUtils.XPathFirstNode(null, oldRefs, "norefs|refdms|reftp|rdandrt");
            oldRefs.ParentNode?.RemoveChild(oldRefs);
        }

        if (onlyDelete)
        {
            return 0;
        }

        XmlElement? searchable = LastElementChild(content);
        if (searchable == null)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{Name}: ERROR: Invalid data module.");
            }
            return ExitInvalidDm;
        }

        var refs = new List<Ref>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        FindRefs(searchable, refs, seen);

        if (refs.Count < 1)
        {
            return 0;
        }

        XmlDocument doc = dmodule.OwnerDocument;
        XmlElement newRefs = doc.CreateElement("refs");

        XmlNode? firstContentChild = content.FirstChild;
        if (firstContentChild != null)
        {
            content.InsertBefore(newRefs, firstContentChild);
        }
        else
        {
            content.AppendChild(newRefs);
        }

        XmlElement? refdms = null, reftp = null, rdandrt = null;
        if (refgrp != null)
        {
            refdms = (XmlElement)newRefs.AppendChild(doc.CreateElement("refdms"))!;
            reftp = (XmlElement)newRefs.AppendChild(doc.CreateElement("reftp"))!;
            rdandrt = (XmlElement)newRefs.AppendChild(doc.CreateElement("rdandrt"))!;
        }

        // Stable sort by code (matches qsort behaviour on a deduped set).
        refs.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));

        foreach (Ref r in refs)
        {
            if (refgrp != null)
            {
                if (r.Node.LocalName == "refdm")
                {
                    AppendCopyWithoutId(refdms!, r.Node);
                    AppendCopyWithoutId(rdandrt!, r.Node);
                }
                else if (r.Node.LocalName == "reftp")
                {
                    AppendCopyWithoutId(reftp!, r.Node);
                    AppendCopyWithoutId(reftp!, r.Node);
                }
            }
            else
            {
                AppendCopyWithoutId(newRefs, r.Node);
            }
        }

        if (refgrp != null)
        {
            if (!refdms!.HasChildNodes)
            {
                refdms.ParentNode?.RemoveChild(refdms);
                rdandrt!.ParentNode?.RemoveChild(rdandrt);
            }
            else if (!reftp!.HasChildNodes)
            {
                reftp.ParentNode?.RemoveChild(reftp);
                rdandrt!.ParentNode?.RemoveChild(rdandrt);
            }
            else
            {
                rdandrt!.ParentNode?.RemoveChild(rdandrt);
            }
        }

        return 0;
    }

    private static void AppendCopyWithoutId(XmlElement parent, XmlNode node)
    {
        XmlNode copy = parent.OwnerDocument.ImportNode(node, deep: true);
        if (copy is XmlElement el && el.HasAttribute("id"))
        {
            el.RemoveAttribute("id");
        }
        parent.AppendChild(copy);
    }

    private sealed class Ref
    {
        public required string Code { get; init; }
        public required XmlElement Node { get; init; }
    }

    private static bool IsRef(XmlNode node)
    {
        if (node.NodeType != XmlNodeType.Element) return false;
        return node.LocalName is "dmRef" or "refdm" or "pmRef" or "reftp" or "externalPubRef";
    }

    private static void FindRefs(XmlNode node, List<Ref> refs, HashSet<string> seen)
    {
        if (IsRef(node))
        {
            string code = CopyCode((XmlElement)node);
            if (seen.Add(code))
            {
                refs.Add(new Ref { Code = code, Node = (XmlElement)node });
            }
            return;
        }

        for (XmlNode? cur = node.FirstChild; cur != null; cur = cur.NextSibling)
        {
            FindRefs(cur, refs, seen);
        }
    }

    /// <summary>
    /// Build the sort/dedupe key for a reference. Mirrors <c>copy_code</c> in the
    /// C source, including the type prefix and the field layout.
    /// </summary>
    private static string CopyCode(XmlElement reference)
    {
        string name = reference.LocalName;

        if (name is "dmRef" or "refdm")
        {
            XmlNode? code = XmlUtils.XPathFirstNode(null, reference, ".//dmCode|.//avee");
            string mic = Val(code, "@modelIdentCode|modelic");
            string sdc = Val(code, "@systemDiffCode|sdc");
            string sc = Val(code, "@systemCode|chapnum");
            string ssc = Val(code, "@subSystemCode|section");
            string sssc = Val(code, "@subSubSystemCode|subsect");
            string ac = Val(code, "@assyCode|subject");
            string dc = Val(code, "@disassyCode|discode");
            string dcv = Val(code, "@disassyCodeVariant|discodev");
            string ic = Val(code, "@infoCode|incode");
            string icv = Val(code, "@infoCodeVariant|incodev");
            string ilc = Val(code, "@itemLocationCode|itemloc");
            string lc = Val(code, "@learnCode");
            string lec = Val(code, "@learnEventCode");

            string learn = (lc.Length > 0 && lec.Length > 0) ? $"-{lc}{lec}" : "";

            return $"{DM}{mic}-{sdc}-{sc}-{ssc}{sssc}-{ac}-{dc}{dcv}-{ic}{icv}-{ilc}{learn}";
        }

        if (name is "pmRef" or "reftp")
        {
            XmlNode? code = XmlUtils.XPathFirstNode(null, reference, ".//pmCode|.//pmc");
            string mic = Attr(code, "modelIdentCode");
            string issuer = Attr(code, "pmIssuer");
            string number = Attr(code, "pmNumber");
            string volume = Attr(code, "pmVolume");
            return $"{PM}{mic}-{issuer}-{number}-{volume}";
        }

        if (name == "externalPubRef")
        {
            XmlNode? code = XmlUtils.XPathFirstNode(null, reference, ".//externalPubCode");
            XmlNode? title = XmlUtils.XPathFirstNode(null, reference, ".//externalPubTitle");
            if (code != null)
            {
                return $"{EP}{code.InnerText}";
            }
            if (title != null)
            {
                return $"{EP}{title.InnerText}";
            }
            // C leaves dst uninitialized when neither is present; we use a stable default.
            return EP;
        }

        return "";
    }

    private static string Val(XmlNode? context, string xpath)
    {
        if (context == null) return "";
        XmlNode? n = context.SelectSingleNode(xpath);
        if (n == null) return "";
        return n.NodeType == XmlNodeType.Attribute ? n.Value ?? "" : n.InnerText;
    }

    private static string Attr(XmlNode? node, string name)
    {
        if (node is XmlElement el)
        {
            return el.HasAttribute(name) ? el.GetAttribute(name) : "";
        }
        return "";
    }

    private static XmlElement? FindChild(XmlNode parent, string name)
    {
        for (XmlNode? cur = parent.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType == XmlNodeType.Element && cur.LocalName == name)
            {
                return (XmlElement)cur;
            }
        }
        return null;
    }

    private static XmlElement? LastElementChild(XmlNode parent)
    {
        for (XmlNode? cur = parent.LastChild; cur != null; cur = cur.PreviousSibling)
        {
            if (cur.NodeType == XmlNodeType.Element)
            {
                return (XmlElement)cur;
            }
        }
        return null;
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-dflqvh?] [-o <out>] [<dms>]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -d, --delete     Delete the references table.");
        stdout.WriteLine("  -f, --overwrite  Overwrite the data modules automatically.");
        stdout.WriteLine("  -h, -?, --help   Show help/usage message.");
        stdout.WriteLine("  -l, --list       Treat input as list of CSDB objects.");
        stdout.WriteLine("  -o, --out <out>  Output to <out> instead of stdout.");
        stdout.WriteLine("  -q, --quiet      Quiet mode.");
        stdout.WriteLine("  -v, --verbose    Verbose output.");
        stdout.WriteLine("      --version    Show version information.");
        stdout.WriteLine("  <dms>            Any number of data modules. Otherwise, read from stdin.");
    }
}
