using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-addicn</c>: add the DTD entity and notation declarations
/// required to reference an ICN (Information Control Number) file from an
/// S1000D module.
/// </summary>
/// <remarks>
/// Mirrors <c>reference/tools/s1kd-addicn/s1kd-addicn.c</c> and the shared
/// <c>add_icn</c>/<c>add_notation</c> helpers in
/// <c>reference/tools/common/s1kd_tools.c</c>. For each ICN file path the tool
/// derives:
/// <list type="bullet">
///   <item>the info entity identifier (basename up to the first '.'),</item>
///   <item>the notation name (the remainder of the basename after the first '.'),</item>
/// </list>
/// then declares a SYSTEM NOTATION for the notation name and an external
/// unparsed general ENTITY referencing the ICN via that notation.
/// </remarks>
public sealed class AddIcnTool : ITool
{
    public string Name => "addicn";
    public string Description => "Add entity/notation declarations for an ICN.";
    public string Version => "1.5.1";

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string src = "-";
        string outPath = "-";
        bool overwrite = false;
        bool fullpath = false;
        var icns = new List<string>();

        // The C tool uses getopt_long; argument processing stops collecting
        // options once a non-option token is seen only because getopt permutes
        // by default. To stay faithful to the documented usage we accept options
        // anywhere and treat the rest as ICNs.
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
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-F" or "--full-path":
                    fullpath = true;
                    break;
                case "-s" or "--source":
                    if (++i >= args.Count) { stderr.WriteLine($"s1kd-{Name}: ERROR: {a} requires an argument."); return 2; }
                    src = args[i];
                    break;
                case "-o" or "--out":
                    if (++i >= args.Count) { stderr.WriteLine($"s1kd-{Name}: ERROR: {a} requires an argument."); return 2; }
                    outPath = args[i];
                    break;
                default:
                    if (a.Length > 1 && a[0] == '-' && a != "-")
                    {
                        // Support combined short options such as "-fF" or "-fs".
                        if (TryParseCombinedShort(a, args, ref i, ref overwrite, ref fullpath, ref src, ref outPath, out bool handled, out int rc, stdout, stderr))
                        {
                            if (handled)
                            {
                                if (rc != int.MinValue)
                                {
                                    return rc;
                                }
                                break;
                            }
                        }
                        stderr.WriteLine($"s1kd-{Name}: ERROR: Unknown option: {a}");
                        return 2;
                    }
                    icns.Add(a);
                    break;
            }
        }

        XmlDocument doc;
        try
        {
            doc = src == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(src);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            // The C tool silently does nothing when read_xml_doc returns NULL,
            // returning 0. Preserve that exit code while reporting the cause.
            stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read {src}: {ex.Message}");
            return 0;
        }

        foreach (string icn in icns)
        {
            AddIcn(doc, icn, fullpath);
        }

        if (overwrite && src != "-")
        {
            XmlUtils.SaveDoc(doc, src);
        }
        else if (outPath == "-")
        {
            stdout.Write(SerializeWithDtd(doc));
            stdout.Write('\n');
        }
        else
        {
            File.WriteAllText(outPath, SerializeWithDtd(doc), new UTF8Encoding(false));
        }

        return 0;
    }

    /// <summary>
    /// Handle combined short option clusters (e.g. <c>-fF</c>, <c>-fs FILE</c>).
    /// Returns true when the token was recognised as a short-option cluster.
    /// </summary>
    private bool TryParseCombinedShort(
        string token, IReadOnlyList<string> args, ref int i,
        ref bool overwrite, ref bool fullpath, ref string src, ref string outPath,
        out bool handled, out int rc, TextWriter stdout, TextWriter stderr)
    {
        handled = false;
        rc = int.MinValue;

        if (token.Length < 2 || token[0] != '-' || token[1] == '-')
        {
            return false;
        }

        for (int p = 1; p < token.Length; p++)
        {
            char c = token[p];
            switch (c)
            {
                case 'f':
                    overwrite = true;
                    break;
                case 'F':
                    fullpath = true;
                    break;
                case 'h':
                case '?':
                    ShowHelp(stdout);
                    handled = true;
                    rc = 0;
                    return true;
                case 's':
                case 'o':
                    {
                        // Remainder of token is the argument, else next token.
                        string val;
                        if (p + 1 < token.Length)
                        {
                            val = token[(p + 1)..];
                        }
                        else if (i + 1 < args.Count)
                        {
                            val = args[++i];
                        }
                        else
                        {
                            stderr.WriteLine($"s1kd-{Name}: ERROR: -{c} requires an argument.");
                            handled = true;
                            rc = 2;
                            return true;
                        }
                        if (c == 's') src = val; else outPath = val;
                        handled = true;
                        return true;
                    }
                default:
                    return false; // unknown character; let caller report error
            }
        }

        handled = true;
        return true;
    }

    /// <summary>
    /// Port of <c>add_icn</c>: declare the NOTATION and external unparsed ENTITY
    /// for the given ICN file path.
    /// </summary>
    private static void AddIcn(XmlDocument doc, string path, bool fullpath)
    {
        string baseName = GetBaseName(path);

        // strtok(name, ".") -> identifier before first '.'; strtok(NULL, "")
        // -> everything after that first '.'.
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

        string systemId = fullpath ? path : baseName;

        var decls = ParseInternalSubset(doc, out string dtdName);

        if (notation != null && !decls.HasNotation(notation))
        {
            decls.Notations.Add(new NotationDecl(notation, null, notation));
        }

        // xmlAddDocEntity replaces an existing entity with the same name.
        decls.Entities.RemoveAll(e => e.Name == infoEntityIdent);
        decls.Entities.Add(new EntityDecl(infoEntityIdent, systemId, notation));

        ApplyInternalSubset(doc, dtdName, decls);
    }

    /// <summary>Mirror POSIX <c>basename</c> for the path semantics used here.</summary>
    private static string GetBaseName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }
        // Handle both separators; libxml2/POSIX use '/', but be forgiving.
        string trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return "/";
        }
        int slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private sealed record NotationDecl(string Name, string? PublicId, string? SystemId);

    private sealed record EntityDecl(string Name, string SystemId, string? Notation);

    private sealed class DtdDecls
    {
        public List<NotationDecl> Notations { get; } = new();
        public List<EntityDecl> Entities { get; } = new();
        public string Preamble { get; set; } = string.Empty;

        public bool HasNotation(string name) => Notations.Exists(n => n.Name == name);
    }

    /// <summary>
    /// Pull apart the existing internal DTD subset so we can append our
    /// declarations and re-serialize. The DTD name defaults to the root element
    /// name (mirroring <c>xmlCreateIntSubset</c> when no DOCTYPE exists).
    /// </summary>
    private static DtdDecls ParseInternalSubset(XmlDocument doc, out string dtdName)
    {
        var decls = new DtdDecls();
        XmlDocumentType? dtd = doc.DocumentType;
        dtdName = dtd?.Name ?? doc.DocumentElement?.Name ?? "doc";

        if (dtd?.InternalSubset is { Length: > 0 } subset)
        {
            ParseDeclarations(subset, decls);
        }

        return decls;
    }

    /// <summary>
    /// Extract NOTATION and ENTITY declarations from an internal subset string,
    /// preserving anything else verbatim as a preamble so existing content is
    /// not lost.
    /// </summary>
    private static void ParseDeclarations(string subset, DtdDecls decls)
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
                if (n != null) decls.Notations.Add(n);
                else preamble.Append(decl);
            }
            else if (body.StartsWith("ENTITY", StringComparison.Ordinal))
            {
                EntityDecl? e = ParseEntity(body);
                if (e != null) decls.Entities.Add(e);
                else preamble.Append(decl);
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

        decls.Preamble = preamble.ToString();
    }

    private static NotationDecl? ParseNotation(string body)
    {
        // NOTATION name SYSTEM "sysId"  |  NOTATION name PUBLIC "pubId" ["sysId"]
        var toks = Tokenize(body);
        if (toks.Count < 2) return null;
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
        // ENTITY name SYSTEM "sysId" NDATA notation
        var toks = Tokenize(body);
        if (toks.Count < 4) return null;
        if (toks[1] == "%") return null; // parameter entity: leave as preamble
        string name = toks[1];
        if (toks[2] != "SYSTEM") return null; // only external unparsed handled
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
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '"' && s[i] != '\'') i++;
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

    /// <summary>
    /// Replace the document's internal DTD subset with one rebuilt from the
    /// collected declarations.
    /// </summary>
    private static void ApplyInternalSubset(XmlDocument doc, string dtdName, DtdDecls decls)
    {
        string internalSubset = BuildInternalSubset(decls);

        XmlDocumentType? existing = doc.DocumentType;
        string? publicId = existing?.PublicId;
        string? systemId = existing?.SystemId;

        XmlDocumentType newDtd = doc.CreateDocumentType(dtdName, publicId, systemId, internalSubset);

        if (existing != null)
        {
            doc.ReplaceChild(newDtd, existing);
        }
        else
        {
            // Insert before the root element (after the XML declaration).
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

    private static string BuildInternalSubset(DtdDecls decls)
    {
        var sb = new StringBuilder();
        sb.Append('\n');
        if (!string.IsNullOrWhiteSpace(decls.Preamble))
        {
            sb.Append(decls.Preamble.Trim('\n'));
            sb.Append('\n');
        }
        foreach (var n in decls.Notations)
        {
            sb.Append("<!NOTATION ").Append(n.Name);
            if (n.PublicId != null)
            {
                sb.Append(" PUBLIC \"").Append(n.PublicId).Append('"');
                if (n.SystemId != null) sb.Append(" \"").Append(n.SystemId).Append('"');
            }
            else
            {
                sb.Append(" SYSTEM \"").Append(n.SystemId).Append('"');
            }
            sb.Append(">\n");
        }
        foreach (var e in decls.Entities)
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

    /// <summary>
    /// Serialize the document including its internal DTD subset. <see
    /// cref="XmlWriter"/> with conformance checks can choke on a DOCTYPE added
    /// after load, so we emit the prolog, DOCTYPE and document element ourselves.
    /// </summary>
    private static string SerializeWithDtd(XmlDocument doc)
    {
        XmlDocumentType? dtd = doc.DocumentType;
        if (dtd == null)
        {
            return XmlUtils.ToXmlString(doc);
        }

        var sb = new StringBuilder();

        // XML declaration (match the default emitted by XmlUtils/libxml2).
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

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-o <file>] [-s <src>] [-fh?] <ICN>...");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -F, --full-path     Include full ICN file path.");
        stdout.WriteLine("  -f, --overwrite     Overwrite source file.");
        stdout.WriteLine("  -h, -?, --help      Show help/usage message.");
        stdout.WriteLine("  -o, --out <file>    Output filename.");
        stdout.WriteLine("  -s, --source <src>  Source filename.");
        stdout.WriteLine("  --version           Show version information.");
        stdout.WriteLine("  <ICN>...            ICNs to add.");
    }

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }
}
