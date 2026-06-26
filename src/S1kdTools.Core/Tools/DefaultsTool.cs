using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-defaults</c>: manage and convert the <c>.defaults</c>,
/// <c>.dmtypes</c> and <c>.fmtypes</c> configuration files used by the other
/// s1kd-tools. Converts between the simple text and XML representations,
/// generates a default set, and can derive files from a BREX data module via a
/// <c>.brexmap</c> mapping.
/// </summary>
/// <remarks>
/// The text&lt;-&gt;XML conversions and BREX/brexmap mapping are re-implemented
/// directly with the <see cref="XmlDocument"/> DOM (no XSLT), mirroring the
/// semantics of the upstream stylesheets (sort.xsl, xml-*-to-text.xsl,
/// brexmap-defaults.xsl, brexmap-dmtypes.xsl).
/// </remarks>
public sealed class DefaultsTool : ITool
{
    public string Name => "defaults";
    public string Description => ".defaults, .dmtypes and .fmtypes files management tool.";
    public string Version => "3.0.0";

    private const int ExitNoFile = 2;   // EXIT_NO_FILE
    private const int ExitOsError = 3;  // EXIT_OS_ERROR

    private enum Format { Text, Xml }
    private enum FileKind { None, Defaults, DmTypes, FmTypes }

    /// <summary>The built-in default <c>.defaults</c> content (defaults.xml).</summary>
    private const string DefaultsXml =
        "<?xml version=\"1.0\"?>\n" +
        "<defaults>\n" +
        "  <default ident=\"countryIsoCode\" value=\"ZZ\"/>\n" +
        "  <default ident=\"inWork\" value=\"01\"/>\n" +
        "  <default ident=\"issue\" value=\"6\"/>\n" +
        "  <default ident=\"issueNumber\" value=\"000\"/>\n" +
        "  <default ident=\"languageIsoCode\" value=\"und\"/>\n" +
        "  <default ident=\"securityClassification\" value=\"01\"/>\n" +
        "</defaults>\n";

    /// <summary>The built-in default <c>.brexmap</c> content (common/brexmap.xml).</summary>
    private const string BrexMapXml =
        "<?xml version=\"1.0\"?>\n" +
        "<brexMap>\n" +
        "  <dmtypes path=\"//@infoCode\"/>\n" +
        "  <default path=\"//@countryIsoCode\" ident=\"countryIsoCode\"/>\n" +
        "  <default path=\"//@languageIsoCode\" ident=\"languageIsoCode\"/>\n" +
        "  <default path=\"//@modelIdentCode\" ident=\"modelIdentCode\"/>\n" +
        "  <default path=\"//@systemDiffCode\" ident=\"systemDiffCode\"/>\n" +
        "  <default path=\"//responsiblePartnerCompany/@enterpriseCode\" ident=\"responsiblePartnerCompanyCode\"/>\n" +
        "  <default path=\"//responsiblePartnerCompany/enterpriseName\" ident=\"responsiblePartnerCompany\"/>\n" +
        "  <default path=\"//originator/@enterpriseCode\" ident=\"originatorCode\"/>\n" +
        "  <default path=\"//originator/enterpriseName\" ident=\"originator\"/>\n" +
        "  <default path=\"//@skillLevelCode\" ident=\"skillLevelCode\"/>\n" +
        "  <default path=\"//@securityClassification\" ident=\"securityClassification\"/>\n" +
        "</brexMap>\n";

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var fmt = Format.Xml;
        var f = FileKind.None;
        string? fname = null;
        bool overwrite = false;
        bool initialize = false;
        bool sort = false;
        XmlDocument? brex = null;
        XmlDocument? brexmap = null;
        string? dir = null;

        // user_defs: <defs><default ident=.. value=../>...</defs>
        var userDefsDoc = new XmlDocument();
        var userDefs = userDefsDoc.CreateElement("defs");
        userDefsDoc.AppendChild(userDefs);
        XmlElement? cur = null;

        var files = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--version":
                    stdout.WriteLine($"{Name} ({Version})");
                    return 0;
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return 0;
                case "-b" or "--brex":
                    if (++i >= args.Count) { return MissingArg("-b", stderr); }
                    if (brex == null)
                    {
                        try { brex = XmlUtils.ReadDoc(args[i]); }
                        catch (Exception ex) when (ex is IOException or XmlException) { brex = null; }
                    }
                    break;
                case "-D" or "--dmtypes":
                    f = FileKind.DmTypes;
                    fname ??= Csdb.DmTypesFileName;
                    break;
                case "-d" or "--defaults":
                    f = FileKind.Defaults;
                    fname ??= Csdb.DefaultsFileName;
                    break;
                case "-F" or "--fmtypes":
                    f = FileKind.FmTypes;
                    fname ??= Csdb.FmTypesFileName;
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-i" or "--init":
                    initialize = true;
                    break;
                case "-J" or "--dump-brexmap":
                    stdout.Write(BrexMapXml);
                    return 0;
                case "-j" or "--brexmap":
                    if (++i >= args.Count) { return MissingArg("-j", stderr); }
                    if (brexmap == null)
                    {
                        try { brexmap = XmlUtils.ReadDoc(args[i]); }
                        catch (Exception ex) when (ex is IOException or XmlException) { brexmap = null; }
                    }
                    break;
                case "-n" or "--name":
                    if (++i >= args.Count) { return MissingArg("-n", stderr); }
                    cur = userDefsDoc.CreateElement("default");
                    cur.SetAttribute("ident", args[i]);
                    userDefs.AppendChild(cur);
                    break;
                case "-o" or "--dir":
                    if (++i >= args.Count) { return MissingArg("-o", stderr); }
                    dir = args[i];
                    break;
                case "-s" or "--sort":
                    sort = true;
                    break;
                case "-t" or "--text":
                    fmt = Format.Text;
                    break;
                case "-v" or "--value":
                    if (++i >= args.Count) { return MissingArg("-v", stderr); }
                    cur?.SetAttribute("value", args[i]);
                    break;
                default:
                    // Handle bundled short options (e.g. -dts) like getopt.
                    if (a.Length > 1 && a[0] == '-' && a[1] != '-' && a != "-")
                    {
                        int? rc = ParseBundled(a, args, ref i, ref fmt, ref f, ref fname, ref overwrite,
                            ref initialize, ref sort, ref brex, ref brexmap, ref dir, userDefsDoc, userDefs,
                            ref cur, stdout, stderr);
                        if (rc.HasValue) { return rc.Value; }
                        break;
                    }
                    files.Add(a);
                    break;
            }
        }

        fname ??= Csdb.DefaultsFileName;
        brexmap ??= ReadDefaultBrexMap();

        // Change/create working directory. This runs in-process, so the
        // original working directory is restored before returning to avoid
        // leaking the change to the rest of the process.
        string? cwd0 = null;
        if (dir != null)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"{Name}: ERROR: Could not create directory {dir}: {ex.Message}");
                return ExitOsError;
            }

            try
            {
                cwd0 = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(dir);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"{Name}: ERROR: Could not change to directory {dir}: {ex.Message}");
                return ExitOsError;
            }
        }

        try
        {
            if (initialize)
            {
                return Initialize(fmt, overwrite, brex, brexmap, userDefs, stdout, stderr);
            }

            if (files.Count > 0)
            {
                int status = 0;
                foreach (string file in files)
                {
                    int r = ConvertOrDump(fmt, f, file, overwrite, sort, brex, brexmap, userDefs, stdout, stderr);
                    if (r != 0) { status = r; }
                }
                return status;
            }

            return ConvertOrDump(fmt, f, fname, overwrite, sort, brex, brexmap, userDefs, stdout, stderr);
        }
        finally
        {
            if (cwd0 != null)
            {
                Directory.SetCurrentDirectory(cwd0);
            }
        }
    }

    private int MissingArg(string opt, TextWriter stderr)
    {
        stderr.WriteLine($"{Name}: ERROR: {opt} requires an argument");
        return ExitNoFile;
    }

    /// <summary>
    /// Parse a bundled short-option cluster such as <c>-dts</c> or <c>-df</c>.
    /// Options taking an argument consume the remainder of the cluster or the
    /// next argv element (mirroring getopt). Returns a non-null exit code to
    /// terminate, or null to continue parsing.
    /// </summary>
    private int? ParseBundled(string cluster, IReadOnlyList<string> args, ref int i,
        ref Format fmt, ref FileKind f, ref string? fname, ref bool overwrite, ref bool initialize,
        ref bool sort, ref XmlDocument? brex, ref XmlDocument? brexmap, ref string? dir,
        XmlDocument userDefsDoc, XmlElement userDefs, ref XmlElement? cur,
        TextWriter stdout, TextWriter stderr)
    {
        for (int k = 1; k < cluster.Length; k++)
        {
            char c = cluster[k];
            switch (c)
            {
                case 'D': f = FileKind.DmTypes; fname ??= Csdb.DmTypesFileName; break;
                case 'd': f = FileKind.Defaults; fname ??= Csdb.DefaultsFileName; break;
                case 'F': f = FileKind.FmTypes; fname ??= Csdb.FmTypesFileName; break;
                case 'f': overwrite = true; break;
                case 'i': initialize = true; break;
                case 's': sort = true; break;
                case 't': fmt = Format.Text; break;
                case 'h' or '?': ShowHelp(stdout); return 0;
                case 'J': stdout.Write(BrexMapXml); return 0;
                case 'b' or 'j' or 'n' or 'o' or 'v':
                {
                    // Argument is the rest of the cluster, or the next argv item.
                    string arg;
                    if (k + 1 < cluster.Length)
                    {
                        arg = cluster[(k + 1)..];
                        k = cluster.Length; // consume rest
                    }
                    else
                    {
                        if (++i >= args.Count) { return MissingArg($"-{c}", stderr); }
                        arg = args[i];
                    }

                    switch (c)
                    {
                        case 'b':
                            if (brex == null)
                            {
                                try { brex = XmlUtils.ReadDoc(arg); }
                                catch (Exception ex) when (ex is IOException or XmlException) { brex = null; }
                            }
                            break;
                        case 'j':
                            if (brexmap == null)
                            {
                                try { brexmap = XmlUtils.ReadDoc(arg); }
                                catch (Exception ex) when (ex is IOException or XmlException) { brexmap = null; }
                            }
                            break;
                        case 'n':
                            cur = userDefsDoc.CreateElement("default");
                            cur.SetAttribute("ident", arg);
                            userDefs.AppendChild(cur);
                            break;
                        case 'o': dir = arg; break;
                        case 'v': cur?.SetAttribute("value", arg); break;
                    }
                    break;
                }
                default:
                    stderr.WriteLine($"{Name}: ERROR: Unknown option: -{c}");
                    return ExitNoFile;
            }
        }
        return null;
    }

    // ----- Conversion dispatch -------------------------------------------------

    private int ConvertOrDump(Format fmt, FileKind f, string fname, bool overwrite, bool sort,
        XmlDocument? brex, XmlDocument? brexmap, XmlElement userDefs, TextWriter stdout, TextWriter stderr)
    {
        // When converting a named file (not just dumping built-in defaults) the
        // source must exist, unless it is generated from a BREX (except fmtypes).
        if (f != FileKind.None && (brex == null || f == FileKind.FmTypes)
            && fname != "-" && !File.Exists(fname))
        {
            stderr.WriteLine($"{Name}: ERROR: Could not open file: {fname}");
            return ExitNoFile;
        }

        if (fmt == Format.Text)
        {
            if (f == FileKind.None)
            {
                DumpDefaultsText(fname, overwrite, brex, brexmap, userDefs, stdout);
            }
            else
            {
                XmlToText(fname, f, overwrite, sort, brex, brexmap, userDefs, stdout);
            }
        }
        else
        {
            if (f == FileKind.None)
            {
                DumpDefaultsXml(fname, overwrite, brex, brexmap, userDefs, stdout);
            }
            else
            {
                TextToXml(fname, f, overwrite, sort, brex, brexmap, userDefs, stdout);
            }
        }

        return 0;
    }

    // ----- Built-in defaults dumping ------------------------------------------

    private XmlDocument BuildDefaultsDoc(XmlDocument? brex, XmlDocument? brexmap, XmlElement userDefs)
    {
        XmlDocument doc;
        if (brex != null)
        {
            doc = NewDefaultsFromBrex(brex, brexmap!);
        }
        else
        {
            doc = XmlUtils.ReadMem(DefaultsXml);
            SetDefaultsFromEnvironment(doc);
            SetUserDefaults(doc, userDefs);
        }
        return doc;
    }

    private void DumpDefaultsXml(string fname, bool overwrite, XmlDocument? brex, XmlDocument? brexmap,
        XmlElement userDefs, TextWriter stdout)
    {
        XmlDocument doc = BuildDefaultsDoc(brex, brexmap, userDefs);
        if (overwrite && fname != "-")
        {
            SaveXmlDoc(doc, fname);
        }
        else
        {
            stdout.Write(SerializeXml(doc));
        }
    }

    private void DumpDefaultsText(string fname, bool overwrite, XmlDocument? brex, XmlDocument? brexmap,
        XmlElement userDefs, TextWriter stdout)
    {
        XmlDocument doc = BuildDefaultsDoc(brex, brexmap, userDefs);
        string text = XmlDefaultsToText(doc);
        if (overwrite && fname != "-")
        {
            File.WriteAllText(fname, text, new UTF8Encoding(false));
        }
        else
        {
            stdout.Write(text);
        }
    }

    // ----- XML <-> text both directions ---------------------------------------

    private void XmlToText(string path, FileKind f, bool overwrite, bool sort,
        XmlDocument? brex, XmlDocument? brexmap, XmlElement userDefs, TextWriter stdout)
    {
        XmlDocument? doc = TryReadXml(path);
        doc ??= SimpleTextToXml(path, f, sort, brex, brexmap);

        if (f == FileKind.Defaults)
        {
            SetUserDefaults(doc, userDefs);
        }

        string text = f switch
        {
            FileKind.DmTypes => XmlDmTypesToText(doc),
            FileKind.FmTypes => XmlFmTypesToText(doc),
            _ => XmlDefaultsToText(doc),
        };

        if (text.Length > 0)
        {
            if (overwrite && path != "-")
            {
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            else
            {
                stdout.Write(text);
            }
        }
    }

    private void TextToXml(string path, FileKind f, bool overwrite, bool sort,
        XmlDocument? brex, XmlDocument? brexmap, XmlElement userDefs, TextWriter stdout)
    {
        XmlDocument doc = SimpleTextToXml(path, f, sort, brex, brexmap);

        if (f == FileKind.Defaults)
        {
            SetUserDefaults(doc, userDefs);
        }

        if (sort)
        {
            SortEntries(doc);
        }

        if (overwrite && path != "-")
        {
            SaveXmlDoc(doc, path);
        }
        else
        {
            stdout.Write(SerializeXml(doc));
        }
    }

    /// <summary>
    /// Produce the XML form of a config file: from a BREX DM if requested,
    /// otherwise by parsing the simple text file (or reading it if it already is
    /// XML). Mirrors <c>simple_text_to_xml</c>.
    /// </summary>
    private XmlDocument SimpleTextToXml(string path, FileKind f, bool sort,
        XmlDocument? brex, XmlDocument? brexmap)
    {
        XmlDocument doc = f switch
        {
            FileKind.DmTypes => brex != null ? NewDmTypesFromBrex(brex, brexmap!) : TextDmTypesToXml(path),
            FileKind.FmTypes => TextFmTypesToXml(path),
            _ => brex != null ? NewDefaultsFromBrex(brex, brexmap!) : TextDefaultsToXml(path),
        };

        if (sort)
        {
            SortEntries(doc);
        }

        return doc;
    }

    /// <summary>Read a file as XML; null if not parseable as XML.</summary>
    private static XmlDocument? TryReadXml(string path)
    {
        if (path == "-")
        {
            return null;
        }
        try
        {
            return XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return null;
        }
    }

    // ----- Text -> XML parsers -------------------------------------------------

    private XmlDocument TextDefaultsToXml(string path)
    {
        var doc = new XmlDocument();
        var root = doc.CreateElement("defaults");
        doc.AppendChild(root);

        foreach (string line in ReadLines(path))
        {
            // sscanf("%31s %255[^\n]"): key = first whitespace-delimited token,
            // val = rest of line (after the run of whitespace) up to newline.
            // sscanf("%31s %255[^\n]"): key (max 31), value (rest, max 255).
            string[] parts = SplitFields(line, 31, 255);
            if (parts.Length != 2)
            {
                continue;
            }
            var def = doc.CreateElement("default");
            def.SetAttribute("ident", parts[0]);
            def.SetAttribute("value", parts[1]);
            root.AppendChild(def);
        }

        return doc;
    }

    private XmlDocument TextDmTypesToXml(string path)
    {
        var doc = new XmlDocument();
        var root = doc.CreateElement("dmtypes");
        doc.AppendChild(root);

        foreach (string line in ReadLines(path))
        {
            // sscanf("%5s %63s %255[^\n]"): code, schema, optional infoName.
            string[] parts = SplitFields(line, 5, 63, 255);
            if (parts.Length < 2)
            {
                continue;
            }
            var type = doc.CreateElement("type");
            type.SetAttribute("infoCode", parts[0]);
            type.SetAttribute("schema", parts[1]);
            if (parts.Length > 2)
            {
                type.SetAttribute("infoName", parts[2]);
            }
            root.AppendChild(type);
        }

        return doc;
    }

    private XmlDocument TextFmTypesToXml(string path)
    {
        var doc = new XmlDocument();
        var root = doc.CreateElement("fmtypes");
        doc.AppendChild(root);

        foreach (string line in ReadLines(path))
        {
            // sscanf("%4s %31s %1023[^\n]"): code, type, optional xsl.
            string[] parts = SplitFields(line, 4, 31, 1023);
            if (parts.Length < 2)
            {
                continue;
            }
            var fm = doc.CreateElement("fm");
            fm.SetAttribute("infoCode", parts[0]);
            fm.SetAttribute("type", parts[1]);
            if (parts.Length == 3)
            {
                fm.SetAttribute("xsl", parts[2]);
            }
            root.AppendChild(fm);
        }

        return doc;
    }

    // ----- XML -> text serializers (mirror xml-*-to-text.xsl) -----------------

    private static string XmlDefaultsToText(XmlDocument doc)
    {
        var sb = new StringBuilder();
        XmlNode? root = doc.DocumentElement;
        if (root == null) return "";
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node is XmlElement el && el.Name == "default")
            {
                sb.Append(el.GetAttribute("ident"));
                sb.Append('\t');
                sb.Append(el.GetAttribute("value"));
                sb.Append('\n');
            }
            else if (node.NodeType == XmlNodeType.Comment)
            {
                sb.Append('#').Append(node.Value).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string XmlDmTypesToText(XmlDocument doc)
    {
        var sb = new StringBuilder();
        XmlNode? root = doc.DocumentElement;
        if (root == null) return "";
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node is XmlElement el && el.Name == "type")
            {
                sb.Append(el.GetAttribute("infoCode"));
                sb.Append('\t');
                sb.Append(el.GetAttribute("schema"));
                if (el.HasAttribute("infoName"))
                {
                    sb.Append('\t');
                    sb.Append(el.GetAttribute("infoName"));
                }
                if (el.HasAttribute("infoNameVariant"))
                {
                    sb.Append(", ");
                    sb.Append(el.GetAttribute("infoNameVariant"));
                }
                sb.Append('\n');
            }
            else if (node.NodeType == XmlNodeType.Comment)
            {
                sb.Append('#').Append(node.Value).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string XmlFmTypesToText(XmlDocument doc)
    {
        var sb = new StringBuilder();
        XmlNode? root = doc.DocumentElement;
        if (root == null) return "";
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node is XmlElement el && el.Name == "fm")
            {
                sb.Append(el.GetAttribute("infoCode"));
                sb.Append('\t');
                sb.Append(el.GetAttribute("type"));
                if (el.HasAttribute("xsl"))
                {
                    sb.Append('\t');
                    sb.Append(el.GetAttribute("xsl"));
                }
                sb.Append('\n');
            }
            else if (node.NodeType == XmlNodeType.Comment)
            {
                sb.Append('#').Append(node.Value).Append('\n');
            }
        }
        return sb.ToString();
    }

    // ----- Sorting (mirror sort.xsl) ------------------------------------------

    /// <summary>
    /// Sort top-level entries (<c>default</c>/<c>type</c>/<c>fm</c>) by their
    /// <c>@ident</c>/<c>@infoCode</c> key, then re-append comments, in place.
    /// </summary>
    private static void SortEntries(XmlDocument doc)
    {
        XmlElement? root = doc.DocumentElement;
        if (root == null) return;
        if (root.Name is not ("defaults" or "dmtypes" or "fmtypes")) return;

        var entries = new List<XmlElement>();
        var comments = new List<XmlNode>();
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node is XmlElement el && el.Name is "default" or "type" or "fm")
            {
                entries.Add(el);
            }
            else if (node.NodeType == XmlNodeType.Comment)
            {
                comments.Add(node);
            }
        }

        // Stable sort by the key attribute (XSLT data-type=text => string sort).
        var sorted = entries
            .Select((e, idx) => (e, idx))
            .OrderBy(t => SortKey(t.e), StringComparer.Ordinal)
            .ThenBy(t => t.idx)
            .Select(t => t.e)
            .ToList();

        // Rebuild children: sorted entries first, then comments (matches the
        // stylesheet which applies entries then comments).
        while (root.FirstChild != null)
        {
            root.RemoveChild(root.FirstChild);
        }
        foreach (var e in sorted) { root.AppendChild(e); }
        foreach (var c in comments) { root.AppendChild(c); }
    }

    private static string SortKey(XmlElement el)
    {
        if (el.HasAttribute("ident")) return el.GetAttribute("ident");
        if (el.HasAttribute("infoCode")) return el.GetAttribute("infoCode");
        return "";
    }

    // ----- Environment / user defaults ----------------------------------------

    /// <summary>Set language/country ISO codes from the LANG env var.</summary>
    private static void SetDefaultsFromEnvironment(XmlDocument doc)
    {
        string? env = Environment.GetEnvironmentVariable("LANG");
        if (string.IsNullOrEmpty(env))
        {
            return;
        }

        // strtok(lang, "_") then strtok(NULL, ".") => language, country.
        string lang = env;
        int us = lang.IndexOf('_');
        string? langL = us < 0 ? (lang.Length > 0 ? lang : null) : lang[..us];
        string? langC = null;
        if (us >= 0)
        {
            string after = lang[(us + 1)..];
            int dot = after.IndexOf('.');
            langC = dot < 0 ? (after.Length > 0 ? after : null) : after[..dot];
        }

        var liso = doc.SelectSingleNode("//default[@ident = 'languageIsoCode']") as XmlElement;
        var ciso = doc.SelectSingleNode("//default[@ident = 'countryIsoCode']") as XmlElement;

        if (!string.IsNullOrEmpty(langL)) { liso?.SetAttribute("value", langL); }
        if (!string.IsNullOrEmpty(langC)) { ciso?.SetAttribute("value", langC); }
    }

    /// <summary>Apply -n/-v user overrides, adding new entries when missing.</summary>
    private static void SetUserDefaults(XmlDocument doc, XmlElement userDefs)
    {
        XmlElement? root = doc.DocumentElement;
        if (root == null) return;

        foreach (XmlNode child in userDefs.ChildNodes)
        {
            if (child is not XmlElement def)
            {
                continue;
            }
            string ident = def.GetAttribute("ident");
            string value = def.GetAttribute("value");

            XmlElement? existing = null;
            foreach (XmlNode n in root.ChildNodes)
            {
                if (n is XmlElement e && e.Name == "default" && e.GetAttribute("ident") == ident)
                {
                    existing = e;
                    break;
                }
            }

            if (existing == null)
            {
                XmlNode imported = doc.ImportNode(def, true);
                root.AppendChild(imported);
            }
            else
            {
                existing.SetAttribute("value", value);
            }
        }
    }

    // ----- BREX -> defaults/dmtypes (mirror brexmap-*.xsl) --------------------

    /// <summary>
    /// Build a <c>.defaults</c> XML doc from a BREX DM using the brexmap. For
    /// each structureObjectRule, find the brexmap <c>default</c> entry matching
    /// its objectPath (or @id), and emit a <c>default</c> with the rule's first
    /// allowed objectValue. Results are sorted. Mirrors brexmap-defaults.xsl +
    /// sort.
    /// </summary>
    private XmlDocument NewDefaultsFromBrex(XmlDocument brex, XmlDocument brexmap)
    {
        var doc = new XmlDocument();
        var root = doc.CreateElement("defaults");
        doc.AppendChild(root);

        var mappings = brexmap.SelectNodes("//default")?.Cast<XmlElement>().ToList() ?? new List<XmlElement>();

        foreach (XmlElement rule in EnumerateStructureObjectRules(brex))
        {
            string? ident = MatchIdent(rule, mappings);
            if (string.IsNullOrEmpty(ident))
            {
                continue;
            }
            var objectValue = FirstChildElement(rule, "objectValue");
            if (objectValue == null)
            {
                continue;
            }
            var def = doc.CreateElement("default");
            def.SetAttribute("ident", ident);
            def.SetAttribute("value", objectValue.GetAttribute("valueAllowed"));
            root.AppendChild(def);
        }

        SortEntries(doc);
        return doc;
    }

    /// <summary>
    /// Build a <c>.dmtypes</c> XML doc from a BREX DM using the brexmap dmtypes
    /// mapping: select the matching structureObjectRule(s) and emit a
    /// <c>type</c> per objectValue (infoCode = @valueAllowed, infoName = text).
    /// Mirrors brexmap-dmtypes.xsl + sort.
    /// </summary>
    private XmlDocument NewDmTypesFromBrex(XmlDocument brex, XmlDocument brexmap)
    {
        var doc = new XmlDocument();
        var root = doc.CreateElement("dmtypes");
        doc.AppendChild(root);

        var dmtypes = brexmap.SelectSingleNode("//dmtypes") as XmlElement;
        if (dmtypes != null)
        {
            foreach (XmlElement rule in SelectRulesForDmTypes(brex, dmtypes))
            {
                foreach (XmlElement ov in ChildElements(rule, "objectValue"))
                {
                    var type = doc.CreateElement("type");
                    type.SetAttribute("infoCode", ov.GetAttribute("valueAllowed"));
                    type.SetAttribute("infoName", ov.InnerText);
                    root.AppendChild(type);
                }
            }
        }

        SortEntries(doc);
        return doc;
    }

    private static IEnumerable<XmlElement> EnumerateStructureObjectRules(XmlDocument brex)
    {
        var nodes = brex.SelectNodes("//structureObjectRule");
        if (nodes == null) yield break;
        foreach (XmlNode n in nodes)
        {
            if (n is XmlElement e) yield return e;
        }
    }

    /// <summary>
    /// Find the ident for a structureObjectRule by matching brexmap default
    /// entries: prefer @id, otherwise objectPath = @path. Mirrors the
    /// xsl:choose generated by brexmap-defaults.xsl.
    /// </summary>
    private static string? MatchIdent(XmlElement rule, List<XmlElement> mappings)
    {
        string? id = rule.HasAttribute("id") ? rule.GetAttribute("id") : null;
        string? objectPath = FirstChildElement(rule, "objectPath")?.InnerText;

        foreach (XmlElement m in mappings)
        {
            if (m.HasAttribute("id"))
            {
                if (id != null && id == m.GetAttribute("id"))
                {
                    return m.GetAttribute("ident");
                }
            }
            else if (m.HasAttribute("path"))
            {
                if (objectPath != null && objectPath == m.GetAttribute("path"))
                {
                    return m.GetAttribute("ident");
                }
            }
        }
        return null;
    }

    private static IEnumerable<XmlElement> SelectRulesForDmTypes(XmlDocument brex, XmlElement dmtypes)
    {
        // brexmap-dmtypes.xsl builds: //structureObjectRule[@id='..'] or
        // //structureObjectRule[objectPath='..'].
        foreach (XmlElement rule in EnumerateStructureObjectRules(brex))
        {
            if (dmtypes.HasAttribute("id"))
            {
                if (rule.GetAttribute("id") == dmtypes.GetAttribute("id"))
                {
                    yield return rule;
                }
            }
            else if (dmtypes.HasAttribute("path"))
            {
                string? op = FirstChildElement(rule, "objectPath")?.InnerText;
                if (op == dmtypes.GetAttribute("path"))
                {
                    yield return rule;
                }
            }
        }
    }

    private static XmlElement? FirstChildElement(XmlElement parent, string name)
    {
        foreach (XmlNode n in parent.ChildNodes)
        {
            if (n is XmlElement e && e.Name == name) return e;
        }
        return null;
    }

    private static IEnumerable<XmlElement> ChildElements(XmlElement parent, string name)
    {
        foreach (XmlNode n in parent.ChildNodes)
        {
            if (n is XmlElement e && e.Name == name) yield return e;
        }
    }

    // ----- Initialize ----------------------------------------------------------

    private int Initialize(Format fmt, bool overwrite, XmlDocument? brex, XmlDocument? brexmap,
        XmlElement userDefs, TextWriter stdout, TextWriter stderr)
    {
        // Mirrors the C: dump_defaults_text (-.) / dump_defaults_xml (-,) and the
        // s1kd-newdm / s1kd-fmgen dump options. The C main returns 0 regardless
        // of whether the newdm/fmgen sub-commands succeed (it only warns), so the
        // overall init status stays 0.
        int status = 0;

        // .defaults
        if (overwrite || !File.Exists(Csdb.DefaultsFileName))
        {
            XmlDocument doc = BuildDefaultsDoc(brex, brexmap, userDefs);
            if (fmt == Format.Text)
            {
                File.WriteAllText(Csdb.DefaultsFileName, XmlDefaultsToText(doc), new UTF8Encoding(false));
            }
            else
            {
                SaveXmlDoc(doc, Csdb.DefaultsFileName);
            }
        }

        // The dump option passed to s1kd-newdm / s1kd-fmgen: -. (text) or -, (xml).
        string dumpOpt = fmt == Format.Text ? "-." : "-,";

        // .dmtypes
        if (overwrite || !File.Exists(Csdb.DmTypesFileName))
        {
            if (brex != null)
            {
                XmlDocument dmtypes = NewDmTypesFromBrex(brex, brexmap!);
                if (fmt == Format.Text)
                {
                    File.WriteAllText(Csdb.DmTypesFileName, XmlDmTypesToText(dmtypes), new UTF8Encoding(false));
                }
                else
                {
                    SaveXmlDoc(dmtypes, Csdb.DmTypesFileName);
                }
            }
            else if (!RunToolToFile("newdm", dumpOpt, Csdb.DmTypesFileName, stderr))
            {
                // Mirrors C S_DMTYPES_ERR (warning only; does not fail the run).
                stderr.WriteLine($"{Name}: ERROR: Could not create {Csdb.DmTypesFileName} file.");
            }
        }

        // .fmtypes
        if (overwrite || !File.Exists(Csdb.FmTypesFileName))
        {
            if (!RunToolToFile("fmgen", dumpOpt, Csdb.FmTypesFileName, stderr))
            {
                // Mirrors C S_FMTYPES_ERR (warning only; does not fail the run).
                stderr.WriteLine($"{Name}: ERROR: Could not create {Csdb.FmTypesFileName} file.");
            }
        }

        return status;
    }

    /// <summary>
    /// Drive a ported <c>new*</c>/generator tool in-process, capturing its stdout
    /// dump into <paramref name="destFile"/>. Replaces the C tool's
    /// <c>system("s1kd-newdm -. &gt; .dmtypes")</c> / <c>system("s1kd-fmgen ...")</c>.
    /// Returns true on success (tool resolved, exited 0, file written), false on
    /// any failure (mirroring a non-zero <c>system()</c> result).
    /// </summary>
    private static bool RunToolToFile(string toolName, string dumpOpt, string destFile, TextWriter stderr)
    {
        ITool? tool = ToolRegistry.Resolve(toolName);
        if (tool == null)
        {
            return false;
        }

        var captured = new StringWriter();
        int rc;
        try
        {
            rc = tool.Run(new[] { dumpOpt }, captured, stderr);
        }
        catch (Exception)
        {
            return false;
        }

        if (rc != 0)
        {
            return false;
        }

        try
        {
            File.WriteAllText(destFile, captured.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return true;
    }

    // ----- brexmap loading -----------------------------------------------------

    private XmlDocument ReadDefaultBrexMap()
    {
        if (Csdb.FindConfig(Csdb.BrexMapFileName, out string path))
        {
            try { return XmlUtils.ReadDoc(path); }
            catch (Exception ex) when (ex is IOException or XmlException) { /* fall through */ }
        }
        return XmlUtils.ReadMem(BrexMapXml);
    }

    // ----- IO helpers ----------------------------------------------------------

    private static IEnumerable<string> ReadLines(string path)
    {
        if (path == "-")
        {
            using var reader = new StreamReader(Console.OpenStandardInput());
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                yield return line;
            }
            yield break;
        }

        foreach (string line in File.ReadLines(path))
        {
            yield return line;
        }
    }

    /// <summary>
    /// Split a line into fields mirroring a C <c>sscanf</c> call. Each entry of
    /// <paramref name="widths"/> is the maximum field width of the corresponding
    /// <c>%Ns</c> directive (the last directive is treated as <c>%N[^\n]</c>,
    /// i.e. it captures the remainder of the line up to its width).
    /// <para>
    /// Crucially, a <c>%Ns</c> token that hits its width limit stops mid-run,
    /// and the leftover characters remain in the stream for the next directive
    /// (after the usual leading-whitespace skip) — matching C exactly.
    /// </para>
    /// </summary>
    private static string[] SplitFields(string line, params int[] widths)
    {
        var fields = new List<string>();
        int i = 0;
        int n = line.Length;
        int last = widths.Length - 1;

        for (int fld = 0; fld < widths.Length; fld++)
        {
            while (i < n && IsSpace(line[i])) i++;
            if (i >= n) break;

            int width = widths[fld];

            if (fld == last)
            {
                // %N[^\n]: take the rest of the line, bounded by width.
                int end = Math.Min(n, i + width);
                fields.Add(line[i..end]);
                break;
            }

            // %Ns: non-whitespace token, bounded by width.
            int start = i;
            while (i < n && !IsSpace(line[i]) && (i - start) < width) i++;
            fields.Add(line[start..i]);
        }

        return fields.ToArray();
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n' or '\v' or '\f';

    /// <summary>
    /// Serialize to a string using a libxml2-style declaration
    /// (<c>&lt;?xml version="1.0"?&gt;</c>, no encoding attribute) followed by a
    /// trailing newline, matching <c>save_xml_doc</c> to stdout.
    /// </summary>
    private static string SerializeXml(XmlDocument doc)
    {
        return Libxml2Serialize(doc);
    }

    private static void SaveXmlDoc(XmlDocument doc, string path)
    {
        File.WriteAllText(path, Libxml2Serialize(doc), new UTF8Encoding(false));
    }

    /// <summary>
    /// Produce output close to libxml2's default formatting: a
    /// <c>&lt;?xml version="1.0"?&gt;</c> declaration, two-space indentation for
    /// generated docs, and a trailing newline.
    /// </summary>
    private static string Libxml2Serialize(XmlDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = true, // we emit our own declaration below
            NewLineChars = "\n",
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            // Write only the document element (and any top-level comments),
            // skipping the existing XML declaration node.
            foreach (XmlNode node in doc.ChildNodes)
            {
                // Skip the XML declaration (re-emitted below) and any top-level
                // whitespace/text nodes that the writer would reject.
                if (node.NodeType is XmlNodeType.XmlDeclaration
                    or XmlNodeType.Whitespace
                    or XmlNodeType.SignificantWhitespace
                    or XmlNodeType.Text)
                {
                    continue;
                }
                node.WriteTo(writer);
            }
        }

        return "<?xml version=\"1.0\"?>\n" + sb + "\n";
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: s1kd-defaults [-Ddfisth?] [-b <BREX>] [-j <map>] [-n <name> -v <value> ...] [-o <dir>] [<file>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -b, --brex <BREX>    Create from a BREX DM.");
        stdout.WriteLine("  -D, --dmtypes        Convert a .dmtypes file.");
        stdout.WriteLine("  -d, --defaults       Convert a .defaults file.");
        stdout.WriteLine("  -F, --fmtypes        Convert a .fmtypes file.");
        stdout.WriteLine("  -f, --overwrite      Overwrite an existing file.");
        stdout.WriteLine("  -h, -?, --help       Show usage message.");
        stdout.WriteLine("  -i, --init           Initialize a new CSDB.");
        stdout.WriteLine("  -J, --dump-brexmap   Dump default .brexmap file.");
        stdout.WriteLine("  -j, --brexmap <map>  Use a custom .brexmap file.");
        stdout.WriteLine("  -n, --name <name>    Default to set a value for with -v.");
        stdout.WriteLine("  -o, --dir <dir>      Use <dir> instead of current directory.");
        stdout.WriteLine("  -s, --sort           Sort entries.");
        stdout.WriteLine("  -t, --text           Output in the simple text format.");
        stdout.WriteLine("  -v, --value <value>  Value for default specified with -n.");
        stdout.WriteLine("  --version  Show version information.");
    }
}
