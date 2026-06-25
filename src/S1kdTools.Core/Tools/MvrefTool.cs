using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-mvref</c>: change all references to one CSDB object (the
/// source) into references to another object (the target) within a set of
/// objects. References are matched by data-module / pub-module code and the
/// matching <c>dmRef</c>/<c>pmRef</c> idents are recoded to the target.
/// </summary>
public sealed class MvrefTool : ITool
{
    public string Name => "mvref";
    public string Description => "Change one reference into another in S1000D CSDB objects.";
    public string Version => "2.6.0";

    private const int ExitEncodingError = 1;
    private const int ExitNoFile = 2;

    private const string AddrPath = "//dmAddress|//dmaddres|//pmAddress|//pmaddres";
    private const string RefsPathContent =
        "//content//dmRef|//content//refdm[*]|//content//pmRef|//content/refpm";
    private const string RefsPath = "//dmRef|//pmRef|//refdm[*]|//refpm";

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2 }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool contentOnly = false;
        string? source = null;
        string? directory = null;
        bool isList = false;
        string? recode = null;
        bool overwrite = false;
        var verbosity = Verbosity.Normal;
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
                case "-c" or "--content":
                    contentOnly = true;
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-q" or "--quiet":
                    if (verbosity > Verbosity.Quiet) verbosity--;
                    break;
                case "-v" or "--verbose":
                    if (verbosity < Verbosity.Verbose) verbosity++;
                    break;
                case "-l" or "--list":
                    isList = true;
                    break;
                case "-s" or "--source":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -s requires an argument"); return ExitNoFile; }
                    source ??= args[i];
                    break;
                case "-d" or "--dir":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -d requires an argument"); return ExitNoFile; }
                    directory ??= args[i];
                    break;
                case "-t" or "--target":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -t requires an argument"); return ExitNoFile; }
                    recode ??= args[i];
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return ExitNoFile;
                    }
                    files.Add(a);
                    break;
            }
        }

        if (recode != null && source == null)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{Name}: ERROR: Source object must be specified with -s to be moved with -m.");
            }
            return ExitNoFile;
        }

        var doc = XmlUtils.NewDocument();
        var addresses = doc.CreateElement("addresses");
        var paths = new List<string>();

        if (directory != null)
        {
            int r = AddDirectory(directory, addresses, verbosity, stderr);
            if (r != 0) return r;
        }

        if (source != null)
        {
            AddAddress(source, addresses, verbosity, stderr);
        }
        else if (files.Count > 0)
        {
            foreach (string file in files)
            {
                if (isList)
                {
                    AddAddressList(file, addresses, paths, verbosity, stderr);
                }
                else
                {
                    AddAddress(file, addresses, verbosity, stderr);
                }
            }
        }
        else if (isList)
        {
            AddAddressList(null, addresses, paths, verbosity, stderr);
        }

        // Pre-load the target (recode) ident once; it is identical for every file.
        XmlNode? recodeIdent = null;
        if (recode != null)
        {
            try
            {
                var recodeDoc = XmlUtils.ReadDoc(recode);
                recodeIdent = recodeDoc.SelectSingleNode(AddrPath);
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                // Mirror C: read_xml_doc failure leaves recodeIdent NULL.
                recodeIdent = null;
            }
        }

        if (directory != null)
        {
            UpdateRefsDirectory(directory, addresses, contentOnly, recodeIdent, overwrite, verbosity, stdout, stderr);
        }
        else if (files.Count > 0)
        {
            if (isList)
            {
                UpdateRefsList(paths, addresses, contentOnly, recodeIdent, overwrite, verbosity, stdout, stderr);
            }
            else
            {
                foreach (string file in files)
                {
                    UpdateRefsFile(file, addresses, contentOnly, recodeIdent, overwrite, verbosity, stdout, stderr);
                }
            }
        }
        else if (isList)
        {
            UpdateRefsList(paths, addresses, contentOnly, recodeIdent, overwrite, verbosity, stdout, stderr);
        }

        return 0;
    }

    // ----- Address registration -----

    private static bool IsS1000D(string fname)
    {
        string[] prefixes = { "DMC-", "DME-", "PMC-", "PME-", "COM-", "IMF-",
            "DDN-", "DML-", "UPF-", "UPE-", "SMC-", "SME-" };
        foreach (string p in prefixes)
        {
            if (fname.StartsWith(p, StringComparison.Ordinal))
            {
                return fname.Length >= 4 &&
                    fname.AsSpan(fname.Length - 4).Equals(".XML", StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    private static void AddAddress(string fname, XmlElement addresses, Verbosity verbosity, TextWriter stderr)
    {
        XmlDocument doc;
        try
        {
            doc = XmlUtils.ReadDoc(fname);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return; // mirror C: read failure is silently skipped
        }

        if (verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"Registering {fname}...");
        }

        XmlNode? address = doc.SelectSingleNode(AddrPath);
        if (address != null)
        {
            var imported = addresses.OwnerDocument!.ImportNode(address, true);
            addresses.AppendChild(imported);
        }
    }

    private static int AddDirectory(string path, XmlElement addresses, Verbosity verbosity, TextWriter stderr)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFiles(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"mvref: ERROR: Directory {path} does not exist.");
            }
            return ExitNoFile;
        }

        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            if (IsS1000D(name))
            {
                AddAddress(Path.Combine(path, name), addresses, verbosity, stderr);
            }
        }
        return 0;
    }

    private static void AddAddressList(string? fname, XmlElement addresses, List<string> paths,
        Verbosity verbosity, TextWriter stderr)
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
                if (verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"mvref: ERROR: Could not read list: {fname}");
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
                string p = line.Split('\t', '\r', '\n')[0];
                if (p.Length == 0) continue;
                AddAddress(p, addresses, verbosity, stderr);
                paths.Add(p);
            }
        }
        finally
        {
            if (fname != null) reader.Dispose();
        }
    }

    // ----- Reference updating -----

    private void UpdateRefsFile(string fname, XmlElement addresses, bool contentOnly,
        XmlNode? recodeIdent, bool overwrite, Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        XmlDocument doc;
        try
        {
            doc = XmlUtils.ReadDoc(fname);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return;
        }

        if (verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"Checking refs in {fname}...");
        }

        var refs = doc.SelectNodes(contentOnly ? RefsPathContent : RefsPath);
        if (refs != null)
        {
            // Snapshot, since updateRef replaces nodes during iteration.
            var refList = new List<XmlNode>(refs.Count);
            foreach (XmlNode r in refs) refList.Add(r);
            foreach (XmlNode r in refList)
            {
                UpdateRef(r, addresses, recodeIdent, verbosity, stderr);
            }
        }

        if (overwrite)
        {
            XmlUtils.SaveDoc(doc, fname);
        }
        else
        {
            stdout.Write(XmlUtils.ToXmlString(doc));
            stdout.Write('\n');
        }
    }

    private void UpdateRefsDirectory(string path, XmlElement addresses, bool contentOnly,
        XmlNode? recodeIdent, bool overwrite, Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFiles(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return;
        }

        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            if (IsS1000D(name))
            {
                UpdateRefsFile(Path.Combine(path, name), addresses, contentOnly, recodeIdent, overwrite, verbosity, stdout, stderr);
            }
        }
    }

    private void UpdateRefsList(List<string> paths, XmlElement addresses, bool contentOnly,
        XmlNode? recodeIdent, bool overwrite, Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        foreach (string p in paths)
        {
            UpdateRefsFile(p, addresses, contentOnly, recodeIdent, overwrite, verbosity, stdout, stderr);
        }
    }

    private void UpdateRef(XmlNode reference, XmlElement addresses, XmlNode? recode,
        Verbosity verbosity, TextWriter stderr)
    {
        for (XmlNode? cur = addresses.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (SameDm(reference, cur, verbosity, stderr))
            {
                RecodeDm(reference, recode, verbosity, stderr);
                return;
            }
            if (SamePm(reference, cur, verbosity, stderr))
            {
                RecodePm(reference, recode, verbosity, stderr);
                return;
            }
        }
    }

    private void RecodeDm(XmlNode reference, XmlNode? recode, Verbosity verbosity, TextWriter stderr)
    {
        XmlNode? dmIdent = First(recode, "dmIdent|self::dmaddres");
        XmlNode? dmCode = First(dmIdent, "dmCode|.//avee");
        XmlNode? issueInfo = First(dmIdent, "issueInfo|issno");
        XmlNode? language = FindChild(dmIdent, "language");

        XmlNode? dmRefIdent = First(reference, "dmRefIdent|self::refdm");
        XmlNode? dmRefCode = First(dmRefIdent, "dmCode|.//avee");
        XmlNode? refIssueInfo = First(dmRefIdent, "issueInfo|issno");
        XmlNode? refLanguage = FindChild(dmRefIdent, "language");

        if (verbosity >= Verbosity.Verbose && dmIdent != null)
        {
            var code = new StringBuilder();
            GetDmCode(code, dmIdent, refIssueInfo != null, refLanguage != null);
            stderr.WriteLine($"      Recoding to {code}...");
        }

        if (dmRefCode != null) ReplaceNode(dmRefCode, dmCode);
        if (refIssueInfo != null) ReplaceNode(refIssueInfo, issueInfo);
        if (refLanguage != null) ReplaceNode(refLanguage, language);

        XmlNode? dmAddressItems = First(recode, "dmAddressItems|self::dmaddres");
        XmlNode? issueDate = FindChild(dmAddressItems, "issueDate");
        XmlNode? dmTitle = First(dmAddressItems, "dmTitle|dmtitle");

        XmlNode? dmRefAddressItems = First(reference, "dmRefAddressItems|self::refdm");
        XmlNode? dmRefIssueDate = FindChild(dmRefAddressItems, "issueDate");
        XmlNode? dmRefTitle = First(dmRefAddressItems, "dmTitle|dmtitle");

        if (dmRefIssueDate != null) ReplaceNode(dmRefIssueDate, issueDate);
        if (dmRefTitle != null) ReplaceNode(dmRefTitle, dmTitle);
    }

    private void RecodePm(XmlNode reference, XmlNode? recode, Verbosity verbosity, TextWriter stderr)
    {
        XmlNode? pmIdent = First(recode, "pmIdent|self::pmaddres");
        XmlNode? pmCode = First(pmIdent, "pmCode|pmc");
        XmlNode? issueInfo = First(pmIdent, "issueInfo|issno");
        XmlNode? language = FindChild(pmIdent, "language");

        XmlNode? pmRefIdent = First(reference, "pmRefIdent|self::refpm");
        XmlNode? pmRefCode = First(pmRefIdent, "pmCode|pmc");
        XmlNode? refIssueInfo = First(pmRefIdent, "issueInfo|issno");
        XmlNode? refLanguage = FindChild(pmRefIdent, "language");

        if (verbosity >= Verbosity.Verbose && pmIdent != null)
        {
            var code = new StringBuilder();
            GetPmCode(code, pmIdent, refIssueInfo != null, refLanguage != null);
            stderr.WriteLine($"      Recoding to {code}...");
        }

        if (pmRefCode != null) ReplaceNode(pmRefCode, pmCode);
        if (refIssueInfo != null) ReplaceNode(refIssueInfo, issueInfo);
        if (refLanguage != null) ReplaceNode(refLanguage, language);

        XmlNode? pmAddressItems = First(recode, "pmAddressItems|self::pmaddres");
        XmlNode? issueDate = First(pmAddressItems, "issueDate|issdate");
        XmlNode? pmTitle = First(pmAddressItems, "pmTitle|pmtitle");

        XmlNode? pmRefAddressItems = First(reference, "pmRefAddressItems|self::refpm");
        XmlNode? pmRefIssueDate = First(pmRefAddressItems, "issueDate|issdate");
        XmlNode? pmRefTitle = First(pmRefAddressItems, "pmTitle|pmtitle");

        if (pmRefIssueDate != null) ReplaceNode(pmRefIssueDate, issueDate);
        if (pmRefTitle != null) ReplaceNode(pmRefTitle, pmTitle);
    }

    // ----- Matching -----

    private static bool IsDmRef(XmlNode r) => r.LocalName is "dmRef" or "refdm";
    private static bool IsDmAddress(XmlNode a) => a.LocalName is "dmAddress" or "dmaddres";
    private static bool IsPmRef(XmlNode r) => r.LocalName is "pmRef" or "refpm";
    private static bool IsPmAddress(XmlNode a) => a.LocalName is "pmAddress" or "pmaddres";

    private bool SameDm(XmlNode reference, XmlNode address, Verbosity verbosity, TextWriter stderr)
    {
        if (!IsDmRef(reference) || !IsDmAddress(address))
        {
            return false;
        }

        XmlNode? refIdent = First(reference, "dmRefIdent|self::refdm");
        XmlNode? addIdent = First(address, "dmIdent|self::dmaddres");

        bool withIssue = First(reference, ".//issueInfo|.//issno") != null;
        bool withLang = First(reference, ".//language") != null;

        var refcode = new StringBuilder();
        var addcode = new StringBuilder();
        GetDmCode(refcode, refIdent, withIssue, withLang);
        GetDmCode(addcode, addIdent, withIssue, withLang);

        bool match = refcode.ToString() == addcode.ToString();
        if (verbosity >= Verbosity.Verbose && match)
        {
            stderr.WriteLine($"    Updating reference to data module {addcode}...");
        }
        return match;
    }

    private bool SamePm(XmlNode reference, XmlNode address, Verbosity verbosity, TextWriter stderr)
    {
        if (!IsPmRef(reference) || !IsPmAddress(address))
        {
            return false;
        }

        XmlNode? refIdent = First(reference, "pmRefIdent|self::refpm");
        XmlNode? addIdent = First(address, "pmIdent|self::pmaddres");

        bool withIssue = First(refIdent, "issueInfo|issno") != null;
        bool withLang = FindChild(refIdent, "language") != null;

        var refcode = new StringBuilder();
        var addcode = new StringBuilder();
        GetPmCode(refcode, refIdent, withIssue, withLang);
        GetPmCode(addcode, addIdent, withIssue, withLang);

        bool match = refcode.ToString() == addcode.ToString();
        if (verbosity >= Verbosity.Verbose && match)
        {
            stderr.WriteLine($"    Updating reference to pub module {addcode}...");
        }
        return match;
    }

    // ----- Code string construction (mirrors getDmCode / getPmCode) -----

    private static void GetDmCode(StringBuilder dst, XmlNode? ident, bool withIssue, bool withLang)
    {
        dst.Clear();
        if (ident == null) return;

        XmlNode? identExtension = First(ident, "identExtension|dmcextension");
        XmlNode? dmCode = First(ident, "dmCode|.//avee");
        XmlNode? issueInfo = First(ident, "issueInfo|issno");
        XmlNode? language = FindChild(ident, "language");

        if (identExtension != null)
        {
            string extensionProducer = Str(identExtension, "@extensionProducer|dmeproducer");
            string extensionCode = Str(identExtension, "@extensionCode|dmecode");
            dst.Append($"{extensionProducer}-{extensionCode}-");
        }

        string modelIdentCode = Str(dmCode, "@modelIdentCode|modelic");
        string systemDiffCode = Str(dmCode, "@systemDiffCode|sdc");
        string systemCode = Str(dmCode, "@systemCode|chapnum");
        string subSystemCode = Str(dmCode, "@subSystemCode|section");
        string subSubSystemCode = Str(dmCode, "@subSubSystemCode|subsect");
        string assyCode = Str(dmCode, "@assyCode|subject");
        string disassyCode = Str(dmCode, "@disassyCode|discode");
        string disassyCodeVariant = Str(dmCode, "@disassyCodeVariant|discodev");
        string infoCode = Str(dmCode, "@infoCode|incode");
        string infoCodeVariant = Str(dmCode, "@infoCodeVariant|incodev");
        string itemLocationCode = Str(dmCode, "@itemLocationCode|itemloc");
        string? learnCode = StrOrNull(dmCode, "@learnCode");
        string? learnEventCode = StrOrNull(dmCode, "@learnEventCode");

        dst.Append($"{modelIdentCode}-{systemDiffCode}-{systemCode}-{subSystemCode}{subSubSystemCode}-{assyCode}-{disassyCode}{disassyCodeVariant}-{infoCode}{infoCodeVariant}-{itemLocationCode}");

        if (learnCode != null && learnEventCode != null)
        {
            dst.Append($"-{learnCode}{learnEventCode}");
        }

        if (withIssue && issueInfo != null)
        {
            string issueNumber = Str(issueInfo, "@issueNumber|@issno");
            string? inWork = StrOrNull(issueInfo, "@inWork|@inwork");
            dst.Append($"_{issueNumber}-{inWork ?? "00"}");
        }

        if (withLang && language != null)
        {
            string languageIsoCode = Str(language, "@languageIsoCode|@language");
            string countryIsoCode = Str(language, "@countryIsoCode|@country");
            dst.Append($"_{languageIsoCode}-{countryIsoCode}");
        }
    }

    private static void GetPmCode(StringBuilder dst, XmlNode? ident, bool withIssue, bool withLang)
    {
        dst.Clear();
        if (ident == null) return;

        XmlNode? identExtension = FindChild(ident, "identExtension");
        XmlNode? pmCode = First(ident, "pmCode|pmc");
        XmlNode? issueInfo = First(ident, "issueInfo|issno");
        XmlNode? language = FindChild(ident, "language");

        if (identExtension != null)
        {
            string extensionProducer = Attr(identExtension, "extensionProducer");
            string extensionCode = Attr(identExtension, "extensionCode");
            dst.Append($"{extensionProducer}-{extensionCode}-");
        }

        string modelIdentCode = Str(pmCode, "@modelIdentCode|modelic");
        string pmIssuer = Str(pmCode, "@pmIssuer|pmissuer");
        string pmNumber = Str(pmCode, "@pmNumber|pmnumber");
        string pmVolume = Str(pmCode, "@pmVolume|pmvolume");

        dst.Append($"{modelIdentCode}-{pmIssuer}-{pmNumber}-{pmVolume}");

        if (withIssue && issueInfo != null)
        {
            string issueNumber = Str(issueInfo, "@issueNumber|@issno");
            string? inWork = StrOrNull(issueInfo, "@inWork|@inwork");
            dst.Append($"_{issueNumber}-{inWork ?? "00"}");
        }

        if (withLang && language != null)
        {
            string languageIsoCode = Str(language, "@languageIsoCode|@language");
            string countryIsoCode = Str(language, "@countryIsoCode|@country");
            dst.Append($"_{languageIsoCode}-{countryIsoCode}");
        }
    }

    // ----- Node helpers (mirror firstXPathNode/findChild/replaceNode) -----

    private static XmlNode? First(XmlNode? context, string xpath) =>
        context?.SelectSingleNode(xpath);

    private static XmlNode? FindChild(XmlNode? parent, string name)
    {
        if (parent == null) return null;
        for (XmlNode? cur = parent.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.LocalName == name)
            {
                return cur;
            }
        }
        return null;
    }

    /// <summary>String content of the first XPath match, or "" (mirrors firstXPathString,
    /// where xmlNodeGetContent(NULL) yields an empty string).</summary>
    private static string Str(XmlNode? context, string xpath)
    {
        XmlNode? n = context?.SelectSingleNode(xpath);
        if (n == null) return string.Empty;
        return n.NodeType == XmlNodeType.Attribute ? n.Value ?? string.Empty : n.InnerText;
    }

    private static string? StrOrNull(XmlNode? context, string xpath)
    {
        XmlNode? n = context?.SelectSingleNode(xpath);
        if (n == null) return null;
        return n.NodeType == XmlNodeType.Attribute ? n.Value ?? string.Empty : n.InnerText;
    }

    private static string Attr(XmlNode node, string name)
    {
        return (node as XmlElement)?.GetAttribute(name) ?? string.Empty;
    }

    /// <summary>
    /// Replace node <paramref name="a"/> with a deep copy of <paramref name="b"/>,
    /// renamed to <paramref name="a"/>'s name (mirrors replaceNode). When
    /// <paramref name="b"/> is null nothing is copied and <paramref name="a"/> is
    /// removed (matches xmlCopyNode(NULL) producing an empty/no-op replacement).
    /// </summary>
    private static void ReplaceNode(XmlNode a, XmlNode? b)
    {
        XmlNode? parent = a.ParentNode;
        if (parent == null) return;

        if (b == null)
        {
            parent.RemoveChild(a);
            return;
        }

        XmlDocument owner = a.OwnerDocument!;
        XmlNode copy = owner.ImportNode(b, true);

        // Rename the copy to a's name, preserving namespace, by recreating the element.
        XmlNode renamed = RenameNode(copy, a, owner);
        parent.ReplaceChild(renamed, a);
    }

    private static XmlNode RenameNode(XmlNode copy, XmlNode a, XmlDocument owner)
    {
        if (copy.NodeType != XmlNodeType.Element || copy.Name == a.Name)
        {
            return copy;
        }

        XmlElement el = owner.CreateElement(a.Prefix, a.LocalName, a.NamespaceURI);
        var src = (XmlElement)copy;
        foreach (XmlAttribute attr in src.Attributes)
        {
            el.SetAttributeNode((XmlAttribute)attr.CloneNode(true));
        }
        for (XmlNode? child = src.FirstChild; child != null; child = child.NextSibling)
        {
            el.AppendChild(child.CloneNode(true));
        }
        return el;
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-d <dir>] [-s <source>] [-t <target>] [-cflqvh?] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -c, --content          Only move references in content section of targets.");
        stdout.WriteLine("  -d, --dir <dir>        Update data modules in directory <dir>.");
        stdout.WriteLine("  -f, --overwrite        Overwrite input objects.");
        stdout.WriteLine("  -h, -?, --help         Show help/usage message.");
        stdout.WriteLine("  -l, --list             Input is a list of data module filenames.");
        stdout.WriteLine("  -q, --quiet            Quiet mode.");
        stdout.WriteLine("  -s, --source <source>  Source object.");
        stdout.WriteLine("  -t, --target <target>  Change refs to <source> into refs to <target>.");
        stdout.WriteLine("  -v, --verbose          Verbose output.");
        stdout.WriteLine("      --version          Show version information.");
        stdout.WriteLine("  <object>...            Objects to change refs in.");
    }
}
