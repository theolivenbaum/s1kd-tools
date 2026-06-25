using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-icncatalog</c>: manage a catalog of ICNs for a project and
/// resolve ICN references in CSDB objects against it. Resolving an ICN places
/// the actual filename (URI) of the ICN into the SYSTEM ID of its ENTITY
/// declaration, optionally switching the NOTATION it references.
/// </summary>
/// <remarks>
/// Mirrors <c>reference/tools/s1kd-icncatalog/s1kd-icncatalog.c</c>. The catalog
/// (<c>.icncatalog</c>, see <see cref="Csdb.IcnCatalogFileName"/>) is an
/// <c>&lt;icnCatalog&gt;</c> document containing <c>&lt;notation&gt;</c>,
/// <c>&lt;media&gt;</c> and <c>&lt;icn&gt;</c> elements.
///
/// Catalog management (-a/-d/-C) and resolution are both implemented here. The C
/// tool relies on libxml2's DOM entity API (xmlGetDocEntity / xmlAddDocEntity);
/// .NET's <see cref="XmlDocument"/> does not expose mutable entity declarations,
/// so the internal DTD subset is parsed/rewritten as text (the same approach as
/// <see cref="AddIcnTool"/>). No XSLT is required.
/// </remarks>
public sealed class IcnCatalogTool : ITool
{
    public string Name => "icncatalog";
    public string Description => "Manage the catalog used to resolve ICNs.";
    public string Version => "3.3.2";

    private enum Verbosity { Quiet, Normal, Verbose }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool overwrite = false;
        string? icnsFname = null;
        bool createNew = false;
        string? media = null;
        bool isList = false;
        var verbosity = Verbosity.Normal;

        // Pending catalog edits. Each entry mirrors the <icn> element the C tool
        // builds up under its "add"/"del" scratch nodes.
        var add = new List<CatalogIcn>();
        var del = new List<CatalogIcn>();
        CatalogIcn? cur = null; // last -a/-d entry, target of -n/-t/-u
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
                    ShowVersion(stdout);
                    return 0;
                case "-a" or "--add":
                    if (++i >= args.Count) { return MissingArg(a, stderr); }
                    cur = new CatalogIcn { InfoEntityIdent = args[i] };
                    add.Add(cur);
                    break;
                case "-d" or "--del":
                    if (++i >= args.Count) { return MissingArg(a, stderr); }
                    cur = new CatalogIcn { InfoEntityIdent = args[i] };
                    del.Add(cur);
                    break;
                case "-C" or "--create":
                    createNew = true;
                    break;
                case "-c" or "--catalog":
                    if (++i >= args.Count) { return MissingArg(a, stderr); }
                    icnsFname ??= args[i];
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-l" or "--list":
                    isList = true;
                    break;
                case "-m" or "--media":
                    if (++i >= args.Count) { return MissingArg(a, stderr); }
                    media ??= args[i];
                    break;
                case "-n" or "--ndata":
                    if (++i >= args.Count) { return MissingArg(a, stderr); }
                    if (cur != null) { cur.Notation = args[i]; }
                    break;
                case "-t" or "--type":
                    if (++i >= args.Count) { return MissingArg(a, stderr); }
                    if (cur != null) { cur.Type = args[i]; }
                    break;
                case "-u" or "--uri":
                    if (++i >= args.Count) { return MissingArg(a, stderr); }
                    if (cur != null) { cur.Uri = args[i]; }
                    break;
                case "-q" or "--quiet":
                    verbosity = Verbosity.Quiet;
                    break;
                case "-v" or "--verbose":
                    verbosity = Verbosity.Verbose;
                    break;
                default:
                    if (a.Length > 1 && a[0] == '-' && a != "-")
                    {
                        if (verbosity > Verbosity.Quiet)
                        {
                            stderr.WriteLine($"s1kd-{Name}: ERROR: Unknown option: {a}");
                        }
                        return 2;
                    }
                    files.Add(a);
                    break;
            }
        }

        // Locate the catalog file if one was not given explicitly (find_config).
        if (icnsFname == null)
        {
            Csdb.FindConfig(Csdb.IcnCatalogFileName, out string found);
            icnsFname = found;
        }

        // Load the catalog: a fresh empty one when creating or when the file is
        // absent, otherwise the existing file.
        XmlDocument icns;
        if (createNew || !File.Exists(icnsFname))
        {
            icns = EmbeddedResources.LoadXml("icncatalog/icncatalog.xml");
        }
        else
        {
            try
            {
                icns = XmlUtils.ReadDoc(icnsFname);
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                if (verbosity > Verbosity.Quiet)
                {
                    stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read catalog {icnsFname}: {ex.Message}");
                }
                return 1;
            }
        }

        if (add.Count > 0 || del.Count > 0)
        {
            if (add.Count > 0) { AddIcns(icns, add, media); }
            if (del.Count > 0) { DelIcns(icns, del, media); }
            if (overwrite) { XmlUtils.SaveDoc(icns, icnsFname); }
            else { WriteCatalog(icns, stdout); }
        }
        else if (files.Count > 0)
        {
            foreach (string file in files)
            {
                if (isList)
                {
                    ResolveIcnsInList(file, icns, overwrite, media, verbosity, stdout, stderr);
                }
                else
                {
                    ResolveIcnsInFile(file, icns, overwrite, media, verbosity, stdout, stderr);
                }
            }
        }
        else if (createNew)
        {
            if (overwrite) { XmlUtils.SaveDoc(icns, icnsFname); }
            else { WriteCatalog(icns, stdout); }
        }
        else if (isList)
        {
            ResolveIcnsInList(null, icns, overwrite, media, verbosity, stdout, stderr);
        }
        else
        {
            ResolveIcnsInFile("-", icns, false, media, verbosity, stdout, stderr);
        }

        return 0;
    }

    private int MissingArg(string opt, TextWriter stderr)
    {
        stderr.WriteLine($"s1kd-{Name}: ERROR: {opt} requires an argument.");
        return 2;
    }

    // ---- Catalog management -------------------------------------------------

    /// <summary>Append the queued ICN entries to the catalog (mirrors add_icns).</summary>
    private static void AddIcns(XmlDocument icns, List<CatalogIcn> add, string? media)
    {
        XmlNode? root = media != null
            ? icns.SelectSingleNode($"/icnCatalog/media[@name='{XPathLiteral(media)}']")
            : icns.DocumentElement;

        if (root == null)
        {
            return;
        }

        foreach (CatalogIcn icn in add)
        {
            XmlElement el = icns.CreateElement("icn");
            el.SetAttribute("infoEntityIdent", icn.InfoEntityIdent);
            if (icn.Type != null) { el.SetAttribute("type", icn.Type); }
            if (icn.Uri != null) { el.SetAttribute("uri", icn.Uri); }
            if (icn.Notation != null) { el.SetAttribute("notation", icn.Notation); }
            root.AppendChild(el);
        }
    }

    /// <summary>Remove ICN entries from the catalog by ident (mirrors del_icns).</summary>
    private static void DelIcns(XmlDocument icns, List<CatalogIcn> del, string? media)
    {
        foreach (CatalogIcn icn in del)
        {
            string id = XPathLiteral(icn.InfoEntityIdent);
            string xpath = media != null
                ? $"/icnCatalog/media[@name='{XPathLiteral(media)}']/icn[@infoEntityIdent='{id}']"
                : $"/icnCatalog/icn[@infoEntityIdent='{id}']";
            XmlNode? node = icns.SelectSingleNode(xpath);
            node?.ParentNode?.RemoveChild(node);
        }
    }

    private static void WriteCatalog(XmlDocument icns, TextWriter stdout)
    {
        stdout.Write(XmlUtils.ToXmlString(icns));
        stdout.Write('\n');
    }

    // ---- Resolution ---------------------------------------------------------

    private void ResolveIcnsInList(string? path, XmlDocument icns, bool overwrite,
        string? media, Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        TextReader reader;
        bool dispose = false;
        if (path != null)
        {
            try
            {
                reader = new StreamReader(File.OpenRead(path));
                dispose = true;
            }
            catch (Exception ex) when (ex is IOException)
            {
                if (verbosity > Verbosity.Quiet)
                {
                    stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read list: {path}");
                }
                return;
            }
        }
        else
        {
            reader = new StreamReader(Console.OpenStandardInput());
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string fname = line.Trim('\t', '\r', '\n', ' ');
                if (fname.Length == 0)
                {
                    continue;
                }
                ResolveIcnsInFile(fname, icns, overwrite, media, verbosity, stdout, stderr);
            }
        }
        finally
        {
            if (dispose)
            {
                reader.Dispose();
            }
        }
    }

    private void ResolveIcnsInFile(string fname, XmlDocument icns, bool overwrite,
        string? media, Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        if (verbosity == Verbosity.Verbose)
        {
            stderr.WriteLine($"s1kd-{Name}: INFO: Resolving ICN references in {fname}...");
        }

        XmlDocument doc;
        try
        {
            doc = fname == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(fname);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            if (verbosity > Verbosity.Quiet)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read {fname}: {ex.Message}");
            }
            return;
        }

        string xpath = media != null
            ? $"/icnCatalog/media[@name='{XPathLiteral(media)}']/icn"
            : "/icnCatalog/icn";

        var decls = DtdSubset.Parse(doc);
        decls.IndexCatalogNotations(icns);

        XmlNodeList? entries = icns.SelectNodes(xpath);
        if (entries != null)
        {
            foreach (XmlNode node in entries)
            {
                if (node is not XmlElement entry)
                {
                    continue;
                }
                string? type = GetAttr(entry, "type");
                string ident = GetAttr(entry, "infoEntityIdent") ?? string.Empty;
                string uri = GetAttr(entry, "uri") ?? string.Empty;
                string? notation = GetAttr(entry, "notation");

                if (type == "pattern")
                {
                    ResolvePatternIcn(doc, decls, ident, uri, notation, verbosity, stderr);
                }
                else
                {
                    ResolveIcn(doc, decls, ident, uri, notation);
                }
            }
        }

        decls.Apply(doc);

        if (overwrite && fname != "-")
        {
            string serialized = DtdSubset.SerializeWithDtd(doc);
            File.WriteAllText(fname, serialized, new UTF8Encoding(false));
        }
        else
        {
            stdout.Write(DtdSubset.SerializeWithDtd(doc));
            stdout.Write('\n');
        }
    }

    /// <summary>Resolve a single (non-pattern) catalog entry (mirrors resolve_icn).</summary>
    private static void ResolveIcn(XmlDocument doc, DtdSubset decls, string ident, string uri, string? notation)
    {
        EntityDecl? e = decls.GetEntity(ident);

        if (e != null)
        {
            ReplaceEntity(decls, e, ident, uri, notation);
        }
        else if (IcnIsUsed(doc, ident))
        {
            if (notation != null)
            {
                AddNotationRef(decls, notation);
                decls.SetEntity(new EntityDecl(ident, uri, notation));
            }
            else
            {
                AddIcn(decls, uri, true);
            }
        }
    }

    /// <summary>Resolve a pattern catalog entry over all infoEntityIdent values
    /// in the document (mirrors resolve_pattern_icn).</summary>
    private static void ResolvePatternIcn(XmlDocument doc, DtdSubset decls, string pattern,
        string uri, string? notation, Verbosity verbosity, TextWriter stderr)
    {
        XmlNodeList? attrs = doc.SelectNodes("//@infoEntityIdent");
        if (attrs == null)
        {
            return;
        }
        foreach (XmlNode attr in attrs)
        {
            string icn = attr.Value ?? string.Empty;
            ResolveIcnRegex(decls, pattern, icn, uri, notation, verbosity, stderr);
        }
    }

    /// <summary>Resolve one ICN via a regular expression pattern (mirrors
    /// resolve_icn_regex + regex_replace).</summary>
    private static void ResolveIcnRegex(DtdSubset decls, string pattern, string icn,
        string uri, string? notation, Verbosity verbosity, TextWriter stderr)
    {
        Regex re;
        try
        {
            re = new Regex(pattern);
        }
        catch (ArgumentException)
        {
            if (verbosity > Verbosity.Quiet)
            {
                stderr.WriteLine($"s1kd-icncatalog: ERROR: Invalid regular expression: {pattern}");
            }
            return;
        }

        Match m = re.Match(icn);
        if (!m.Success)
        {
            return;
        }

        string s = RegexReplace(m, uri, verbosity, stderr);

        EntityDecl? e = decls.GetEntity(icn);
        if (e != null)
        {
            ReplaceEntity(decls, e, icn, s, notation);
        }
        else if (notation != null)
        {
            AddNotationRef(decls, notation);
            decls.SetEntity(new EntityDecl(icn, s, notation));
        }
        else
        {
            AddIcn(decls, s, true);
        }
    }

    /// <summary>Fill backreferences (\1..\9) in the URI template from regex
    /// match groups (mirrors regex_replace).</summary>
    private static string RegexReplace(Match m, string uri, Verbosity verbosity, TextWriter stderr)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < uri.Length; i++)
        {
            if (uri[i] == '\\' && i + 1 < uri.Length)
            {
                int refNum = uri[++i] - '0';
                if (refNum >= 0 && refNum < m.Groups.Count && m.Groups[refNum].Success)
                {
                    sb.Append(m.Groups[refNum].Value);
                }
                else if (verbosity > Verbosity.Quiet)
                {
                    stderr.WriteLine($"s1kd-{nameof(IcnCatalogTool)}: ERROR: Undefined reference in URI template: \\{(char)(refNum + '0')}");
                }
            }
            else
            {
                sb.Append(uri[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>Replace an entity's SYSTEM URI, adding a notation if required
    /// (mirrors replace_entity).</summary>
    private static void ReplaceEntity(DtdSubset decls, EntityDecl e, string ident, string uri, string? notation)
    {
        // When no new notation is given, keep the entity's existing NDATA.
        string? ndata = notation ?? e.Notation;
        decls.SetEntity(new EntityDecl(ident, uri, ndata));
        if (notation != null)
        {
            AddNotationRef(decls, notation);
        }
    }

    /// <summary>Whether the ICN identifier is referenced by any attribute in the
    /// document (mirrors icn_is_used: //@*[.=$id]).</summary>
    private static bool IcnIsUsed(XmlDocument doc, string ident)
    {
        XmlNode? node = doc.SelectSingleNode($"//@*[.='{XPathLiteral(ident)}']");
        return node != null;
    }

    /// <summary>Copy a notation declaration from the catalog into the document
    /// by its name (mirrors add_notation_ref + add_notation).</summary>
    private static void AddNotationRef(DtdSubset decls, string notation)
    {
        // Look it up in the loaded catalog so we honour publicId/systemId.
        NotationDecl decl = decls.LookupCatalogNotation(notation)
            ?? new NotationDecl(notation, null, null);
        if (!decls.HasNotation(decl.Name))
        {
            decls.Notations.Add(decl);
        }
    }

    /// <summary>Reconstruct an entity (and notation) directly from a filename
    /// when the ICN had no existing entity (mirrors add_icn).</summary>
    private static void AddIcn(DtdSubset decls, string path, bool fullpath)
    {
        string baseName = GetBaseName(path);
        int dot = baseName.IndexOf('.');
        string infoEntityIdent;
        string? notation;
        if (dot < 0)
        {
            infoEntityIdent = baseName;
            notation = null;
        }
        else
        {
            infoEntityIdent = baseName[..dot];
            string rest = baseName[(dot + 1)..];
            notation = rest.Length == 0 ? null : rest;
        }

        if (notation != null && !decls.HasNotation(notation))
        {
            decls.Notations.Add(new NotationDecl(notation, null, notation));
        }
        decls.SetEntity(new EntityDecl(infoEntityIdent, fullpath ? path : baseName, notation));
    }

    private static string GetBaseName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }
        string trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return "/";
        }
        int slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private static string? GetAttr(XmlElement el, string name)
    {
        return el.HasAttribute(name) ? el.GetAttribute(name) : null;
    }

    /// <summary>Return a value for embedding inside an XPath single-quoted
    /// literal. ICN identifiers and media names do not contain quotes, so any
    /// single quote is simply stripped to keep the surrounding XPath well-formed.</summary>
    private static string XPathLiteral(string value)
    {
        return value.IndexOf('\'') < 0 ? value : value.Replace("'", string.Empty);
    }

    // ---- DTD subset model ---------------------------------------------------

    private sealed class CatalogIcn
    {
        public string InfoEntityIdent { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Uri { get; set; }
        public string? Notation { get; set; }
    }

    private sealed record NotationDecl(string Name, string? PublicId, string? SystemId);

    private sealed record EntityDecl(string Name, string SystemId, string? Notation);

    /// <summary>
    /// Parsed representation of a document's internal DTD subset, holding the
    /// NOTATION and ENTITY declarations so they can be edited and re-serialized.
    /// Anything that is not a notation or unparsed entity is preserved verbatim
    /// in <see cref="Preamble"/>.
    /// </summary>
    private sealed class DtdSubset
    {
        public List<NotationDecl> Notations { get; } = new();
        public List<EntityDecl> Entities { get; } = new();
        public string Preamble { get; private set; } = string.Empty;
        public string DtdName { get; private set; } = "doc";

        // Notations declared in the catalog, by name, used by AddNotationRef.
        private readonly Dictionary<string, NotationDecl> _catalogNotations = new(StringComparer.Ordinal);

        public bool HasNotation(string name) => Notations.Exists(n => n.Name == name);

        public EntityDecl? GetEntity(string name) => Entities.Find(e => e.Name == name);

        public void SetEntity(EntityDecl e)
        {
            Entities.RemoveAll(x => x.Name == e.Name);
            Entities.Add(e);
        }

        public NotationDecl? LookupCatalogNotation(string name) =>
            _catalogNotations.TryGetValue(name, out NotationDecl? d) ? d : null;

        public static DtdSubset Parse(XmlDocument doc)
        {
            var subset = new DtdSubset();
            XmlDocumentType? dtd = doc.DocumentType;
            subset.DtdName = dtd?.Name ?? doc.DocumentElement?.Name ?? "doc";
            if (dtd?.InternalSubset is { Length: > 0 } internalSubset)
            {
                subset.ParseDeclarations(internalSubset);
            }
            return subset;
        }

        /// <summary>Index notations declared in the loaded catalog so resolution
        /// can copy their publicId/systemId by name.</summary>
        public void IndexCatalogNotations(XmlDocument icns)
        {
            XmlNodeList? nodes = icns.SelectNodes("/icnCatalog/notation");
            if (nodes == null)
            {
                return;
            }
            foreach (XmlNode node in nodes)
            {
                if (node is not XmlElement el)
                {
                    continue;
                }
                string name = el.GetAttribute("name");
                if (name.Length == 0)
                {
                    continue;
                }
                string? pub = el.HasAttribute("publicId") ? el.GetAttribute("publicId") : null;
                string? sys = el.HasAttribute("systemId") ? el.GetAttribute("systemId") : null;
                _catalogNotations[name] = new NotationDecl(name, pub, sys);
            }
        }

        private void ParseDeclarations(string subset)
        {
            var preamble = new StringBuilder();
            int i = 0;
            while (i < subset.Length)
            {
                int open = subset.IndexOf("<!", i, StringComparison.Ordinal);
                if (open < 0)
                {
                    break;
                }
                preamble.Append(subset, i, open - i);

                int close = subset.IndexOf('>', open);
                if (close < 0)
                {
                    preamble.Append(subset, open, subset.Length - open);
                    i = subset.Length;
                    break;
                }

                string decl = subset.Substring(open, close - open + 1);
                string body = decl.Substring(2, decl.Length - 3).Trim();

                if (body.StartsWith("NOTATION", StringComparison.Ordinal))
                {
                    NotationDecl? n = ParseNotation(body);
                    if (n != null) { Notations.Add(n); } else { preamble.Append(decl); }
                }
                else if (body.StartsWith("ENTITY", StringComparison.Ordinal))
                {
                    EntityDecl? e = ParseEntity(body);
                    if (e != null) { Entities.Add(e); } else { preamble.Append(decl); }
                }
                else
                {
                    preamble.Append(decl);
                }

                i = close + 1;
            }

            if (i < subset.Length)
            {
                preamble.Append(subset, i, subset.Length - i);
            }

            Preamble = preamble.ToString();
        }

        private static NotationDecl? ParseNotation(string body)
        {
            var toks = Tokenize(body);
            if (toks.Count < 2) { return null; }
            string name = toks[1];
            if (toks.Count >= 4 && toks[2] == "SYSTEM")
            {
                return new NotationDecl(name, null, Unquote(toks[3]));
            }
            if (toks.Count >= 4 && toks[2] == "PUBLIC")
            {
                string pub = Unquote(toks[3]);
                string? sys = toks.Count >= 5 ? Unquote(toks[4]) : null;
                return new NotationDecl(name, pub, sys);
            }
            return new NotationDecl(name, null, null);
        }

        private static EntityDecl? ParseEntity(string body)
        {
            var toks = Tokenize(body);
            if (toks.Count < 4) { return null; }
            if (toks[1] == "%") { return null; }
            string name = toks[1];
            if (toks[2] != "SYSTEM") { return null; }
            string sysId = Unquote(toks[3]);
            string? notation = null;
            for (int k = 4; k < toks.Count - 1; k++)
            {
                if (toks[k] == "NDATA")
                {
                    notation = toks[k + 1];
                    break;
                }
            }
            return new EntityDecl(name, sysId, notation);
        }

        private static List<string> Tokenize(string s)
        {
            var toks = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '"' || c == '\'')
                {
                    int end = s.IndexOf(c, i + 1);
                    if (end < 0) { toks.Add(s[i..]); break; }
                    toks.Add(s.Substring(i, end - i + 1));
                    i = end + 1;
                }
                else
                {
                    int start = i;
                    while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '"' && s[i] != '\'') { i++; }
                    toks.Add(s[start..i]);
                }
            }
            return toks;
        }

        private static string Unquote(string s)
        {
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0])
            {
                return s[1..^1];
            }
            return s;
        }

        public void Apply(XmlDocument doc)
        {
            // If nothing requires a DTD, leave the document untouched.
            if (Notations.Count == 0 && Entities.Count == 0 && string.IsNullOrWhiteSpace(Preamble))
            {
                if (doc.DocumentType == null)
                {
                    return;
                }
            }

            string internalSubset = Build();
            XmlDocumentType? existing = doc.DocumentType;
            string? publicId = existing?.PublicId;
            string? systemId = existing?.SystemId;

            XmlDocumentType newDtd = doc.CreateDocumentType(DtdName, publicId, systemId, internalSubset);

            if (existing != null)
            {
                doc.ReplaceChild(newDtd, existing);
            }
            else
            {
                XmlNode? root = doc.DocumentElement;
                if (root != null)
                {
                    doc.InsertBefore(newDtd, root);
                }
                else
                {
                    doc.AppendChild(newDtd);
                }
            }
        }

        private string Build()
        {
            var sb = new StringBuilder();
            sb.Append('\n');
            if (!string.IsNullOrWhiteSpace(Preamble))
            {
                sb.Append(Preamble.Trim('\n'));
                sb.Append('\n');
            }
            foreach (NotationDecl n in Notations)
            {
                sb.Append("<!NOTATION ").Append(n.Name);
                if (n.PublicId != null)
                {
                    sb.Append(" PUBLIC \"").Append(n.PublicId).Append('"');
                    if (n.SystemId != null) { sb.Append(" \"").Append(n.SystemId).Append('"'); }
                }
                else if (n.SystemId != null)
                {
                    sb.Append(" SYSTEM \"").Append(n.SystemId).Append('"');
                }
                sb.Append(">\n");
            }
            foreach (EntityDecl e in Entities)
            {
                sb.Append("<!ENTITY ").Append(e.Name)
                  .Append(" SYSTEM \"").Append(e.SystemId).Append('"');
                if (e.Notation != null)
                {
                    sb.Append(" NDATA ").Append(e.Notation);
                }
                sb.Append(">\n");
            }
            return sb.ToString();
        }

        public static string SerializeWithDtd(XmlDocument doc)
        {
            XmlDocumentType? dtd = doc.DocumentType;
            if (dtd == null)
            {
                return XmlUtils.ToXmlString(doc);
            }

            var sb = new StringBuilder();

            XmlDeclaration? decl = doc.FirstChild as XmlDeclaration;
            string version = decl?.Version ?? "1.0";
            string encoding = string.IsNullOrEmpty(decl?.Encoding) ? "utf-8" : decl!.Encoding;
            sb.Append("<?xml version=\"").Append(version).Append("\" encoding=\"").Append(encoding).Append("\"?>\n");

            sb.Append("<!DOCTYPE ").Append(dtd.Name);
            if (!string.IsNullOrEmpty(dtd.PublicId))
            {
                sb.Append(" PUBLIC \"").Append(dtd.PublicId).Append("\" \"").Append(dtd.SystemId ?? string.Empty).Append('"');
            }
            else if (!string.IsNullOrEmpty(dtd.SystemId))
            {
                sb.Append(" SYSTEM \"").Append(dtd.SystemId).Append('"');
            }
            if (!string.IsNullOrEmpty(dtd.InternalSubset))
            {
                sb.Append(" [").Append(dtd.InternalSubset).Append(']');
            }
            sb.Append(">\n");

            if (doc.DocumentElement != null)
            {
                sb.Append(doc.DocumentElement.OuterXml);
            }

            return sb.ToString();
        }
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -a, --add <icn>         Add an ICN to the catalog.");
        stdout.WriteLine("  -C, --create            Create a new ICN catalog.");
        stdout.WriteLine("  -c, --catalog <catalog> Use <catalog> as the ICN catalog.");
        stdout.WriteLine("  -d, --del <icn>         Delete an ICN from the catalog.");
        stdout.WriteLine("  -f, --overwrite         Overwrite input objects.");
        stdout.WriteLine("  -h, -?, --help          Show help/usage message.");
        stdout.WriteLine("  -l, --list              Treat input as list of objects.");
        stdout.WriteLine("  -m, --media <media>     Specify intended output media.");
        stdout.WriteLine("  -n, --ndata <notation>  Set the notation of the new ICN.");
        stdout.WriteLine("  -q, --quiet             Quiet mode.");
        stdout.WriteLine("  -t, --type <type>       Set the type of the new catalog entry.");
        stdout.WriteLine("  -u, --uri <uri>         Set the URI of the new ICN.");
        stdout.WriteLine("  -v, --verbose           Verbose output.");
        stdout.WriteLine("  --version               Show version information.");
    }

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }
}
