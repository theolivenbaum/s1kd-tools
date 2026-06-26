using System.Text;
using System.Xml;

namespace S1kdTools;

/// <summary>
/// ICN (Information Control Number) entity and notation helpers.
/// </summary>
/// <remarks>
/// Faithful port of the shared <c>add_icn</c> and <c>add_notation</c> helpers in
/// <c>reference/tools/common/s1kd_tools.c</c>, used by <c>s1kd-addicn</c>.
///
/// In the C implementation these manipulate libxml2's DTD model directly
/// (<c>xmlCreateIntSubset</c>, <c>xmlAddNotationDecl</c>, <c>xmlAddDocEntity</c>).
/// .NET's <see cref="XmlDocument"/> cannot add NOTATION/ENTITY declarations
/// through its DOM, so the internal DTD subset is parsed from, and serialized
/// back to, text. That text round-trip is encapsulated entirely here.
/// </remarks>
public static class Icn
{
    /// <summary>A parsed NOTATION declaration from a DTD internal subset.</summary>
    public sealed record NotationDecl(string Name, string? PublicId, string? SystemId);

    /// <summary>A parsed external unparsed general ENTITY declaration.</summary>
    public sealed record EntityDecl(string Name, string SystemId, string? Notation);

    /// <summary>
    /// The NOTATION and ENTITY declarations of a document's internal DTD subset,
    /// plus any other content preserved verbatim as a preamble.
    /// </summary>
    public sealed class DtdDecls
    {
        public List<NotationDecl> Notations { get; } = new();
        public List<EntityDecl> Entities { get; } = new();
        public string Preamble { get; set; } = string.Empty;

        public bool HasNotation(string name) => Notations.Exists(n => n.Name == name);
    }

    /// <summary>
    /// Port of <c>add_notation</c>: ensure a NOTATION with the given name exists
    /// in the document's internal DTD subset. Like the C, if a notation with the
    /// same name already exists nothing is added (first declaration wins).
    /// </summary>
    public static void AddNotation(XmlDocument doc, string? name, string? pubId, string? sysId)
    {
        // libxml2's xmlAddNotationDecl with a NULL name is a no-op (no notation
        // is created), matching add_icn called on a path with no extension.
        if (name == null)
        {
            return;
        }

        DtdDecls decls = ParseInternalSubset(doc, out string dtdName);
        if (!decls.HasNotation(name))
        {
            decls.Notations.Add(new NotationDecl(name, pubId, sysId));
            ApplyInternalSubset(doc, dtdName, decls);
        }
    }

    /// <summary>
    /// Port of <c>add_icn</c>: declare the NOTATION and external unparsed general
    /// ENTITY for the given ICN file path.
    /// </summary>
    /// <remarks>
    /// Mirrors the C exactly: the basename is split on the first '.', giving the
    /// info entity identifier (before the dot) and the notation (everything after
    /// the first dot). A SYSTEM notation named after the notation is declared, and
    /// an external unparsed entity referencing the ICN (by basename, or full path
    /// when <paramref name="fullpath"/>) via that notation is added.
    /// </remarks>
    public static void AddIcn(XmlDocument doc, string path, bool fullpath)
    {
        string baseName = BaseName(path);

        // strtok(name, ".") -> identifier before the first '.';
        // strtok(NULL, "")  -> everything after that first '.'.
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
            // strtok would return NULL if nothing follows the delimiter.
            notation = rest.Length == 0 ? null : rest;
        }

        string systemId = fullpath ? path : baseName;

        DtdDecls decls = ParseInternalSubset(doc, out string dtdName);

        // add_notation(doc, notation, NULL, notation)
        if (notation != null && !decls.HasNotation(notation))
        {
            decls.Notations.Add(new NotationDecl(notation, null, notation));
        }

        // xmlAddDocEntity replaces an existing entity with the same name.
        decls.Entities.RemoveAll(e => e.Name == infoEntityIdent);
        decls.Entities.Add(new EntityDecl(infoEntityIdent, systemId, notation));

        ApplyInternalSubset(doc, dtdName, decls);
    }

    /// <summary>
    /// Serialize a document including its internal DTD subset. <see
    /// cref="XmlWriter"/> conformance checks can choke on a DOCTYPE added after
    /// load, so the prolog, DOCTYPE and document element are emitted directly.
    /// </summary>
    public static string SerializeWithDtd(XmlDocument doc)
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

    /// <summary>Mirror POSIX <c>basename</c> for the path semantics used here.</summary>
    public static string BaseName(string path)
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

    /// <summary>
    /// Pull apart the existing internal DTD subset so declarations can be appended
    /// and re-serialized. The DTD name defaults to the root element name (mirroring
    /// <c>xmlCreateIntSubset</c> when no DOCTYPE exists).
    /// </summary>
    public static DtdDecls ParseInternalSubset(XmlDocument doc, out string dtdName)
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
    /// preserving anything else verbatim as a preamble so existing content is not
    /// lost.
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
    public static void ApplyInternalSubset(XmlDocument doc, string dtdName, DtdDecls decls)
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
}
