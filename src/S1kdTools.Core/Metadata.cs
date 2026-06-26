using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace S1kdTools;

/// <summary>
/// Describes a single metadata item: its key, the XPath used to locate it, and
/// a human-readable description. Mirrors the <c>struct metadata</c> table in
/// <c>tools/s1kd-metadata/s1kd-metadata.c</c>.
/// </summary>
public sealed record MetadataKey(string Name, string XPath, string Description, bool Editable);

/// <summary>
/// Retrieve and set S1000D metadata on CSDB objects. Ports the <c>metadata[]</c>
/// table and the <c>show_*</c> / <c>edit_*</c> / <c>create_*</c> accessors from
/// <c>tools/s1kd-metadata/s1kd-metadata.c</c>, plus the <c>icn_metadata[]</c>
/// table for ICN files.
///
/// The engine mirrors the C exactly: a key's XPath locates a node; if that node
/// is an attribute, its owner element is used instead; the resulting element is
/// then handed to a <c>show</c> (get) or <c>edit</c> (set) handler that
/// dispatches between the modern (Issue 4.x+) and legacy SGML element/attribute
/// forms by inspecting the element name. When <c>set</c> targets a missing node
/// and the C defines a <c>create</c> function, the node is created in the right
/// place.
/// </summary>
public static class Metadata
{
    private const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Internal handler set for a metadata key, mirroring the function pointers
    /// in the C <c>struct metadata</c>.
    /// </summary>
    private sealed record Entry(
        string Name,
        string XPath,
        string Description,
        Func<XmlElement, string?>? Show,
        Func<XmlElement, string, bool>? Edit,
        Func<XmlDocument, string, bool>? Create)
    {
        public bool Editable => Edit != null || Create != null;
    }

    // The full ordered table, mirroring metadata[] in the C source. "format",
    // "modified" and "path" are file-system metadata the C derives outside the
    // XML (path "false()"); they are intentionally omitted here as they are not
    // document metadata. Everything else is reproduced.
    private static readonly Entry[] Table = BuildTable();

    /// <summary>The full metadata key table (ordered as in the C source).</summary>
    public static readonly IReadOnlyList<MetadataKey> Keys =
        Table.Select(e => new MetadataKey(e.Name, e.XPath, e.Description, e.Editable)).ToArray();

    private static readonly Dictionary<string, Entry> ByName =
        Table.ToDictionary(e => e.Name, StringComparer.Ordinal);

    /// <summary>Whether a metadata key is known.</summary>
    public static bool IsKnown(string name) => ByName.ContainsKey(name);

    /// <summary>
    /// Retrieve a metadata value from a document, or null if not present.
    /// Mirrors the tool's <c>show_metadata</c>: locate the XPath node (using the
    /// owner element for attribute matches), then run the key's show handler.
    /// </summary>
    public static string? Get(XmlDocument doc, string key)
    {
        if (!ByName.TryGetValue(key, out var entry))
        {
            throw new ArgumentException($"Unknown metadata key: {key}", nameof(key));
        }

        XmlElement? el = LocateElement(doc, entry.XPath);
        if (el == null || entry.Show == null)
        {
            return null;
        }
        return entry.Show(el);
    }

    /// <summary>
    /// Set a metadata value on a document. Returns true if applied. Mirrors the
    /// tool's <c>edit_metadata</c>: if the node exists, run the edit handler; if
    /// it is missing and a create handler exists, create it; otherwise fail.
    /// </summary>
    public static bool Set(XmlDocument doc, string key, string value)
    {
        if (!ByName.TryGetValue(key, out var entry))
        {
            throw new ArgumentException($"Unknown metadata key: {key}", nameof(key));
        }

        XmlElement? el = LocateElement(doc, entry.XPath);
        if (el == null)
        {
            return entry.Create != null && entry.Create(doc, value);
        }
        return entry.Edit != null && entry.Edit(el, value);
    }

    // ----- engine helpers -----

    /// <summary>
    /// Locate the first node matching <paramref name="xpath"/>; if it is an
    /// attribute, return its owner element (mirroring the C
    /// <c>if (node-&gt;type == XML_ATTRIBUTE_NODE) node = node-&gt;parent</c>).
    /// </summary>
    private static XmlElement? LocateElement(XmlDocument doc, string xpath)
    {
        XmlNode? node = doc.SelectSingleNode(xpath);
        return node switch
        {
            XmlAttribute attr => attr.OwnerElement,
            XmlElement el => el,
            null => null,
            // text/content nodes: walk up to the containing element
            { } other => other.ParentNode as XmlElement,
        };
    }

    private static XmlElement? FirstElement(XmlDocument doc, string xpath)
    {
        XmlNode? node = doc.SelectSingleNode(xpath);
        return node as XmlElement ?? (node as XmlAttribute)?.OwnerElement;
    }

    private static XmlNode? Local(XmlElement node, string xpath) => node.SelectSingleNode(xpath);

    private static string? LocalString(XmlElement node, string xpath) => Local(node, xpath)?.InnerText;

    private static bool Has(XmlElement node, string attr) => node.HasAttribute(attr);

    private static string ShowAttr(XmlElement node, string attr) => node.GetAttribute(attr);

    private static bool EditAttr(XmlElement node, string attr, string val)
    {
        node.SetAttribute(attr, val);
        return true;
    }

    private static string ShowNode(XmlElement node) => node.InnerText;

    private static bool EditNode(XmlNode? node, string val)
    {
        if (node == null)
        {
            return false;
        }
        node.InnerText = val;
        return true;
    }

    private static bool Is(XmlNode node, string name) => node.LocalName == name;

    private static XmlElement NewElement(XmlDocument doc, string name) => doc.CreateElement(name);

    // ----- show / edit handlers (faithful ports of the C functions) -----

    private static string ShowSimpleNode(XmlElement n) => n.InnerText;
    private static bool EditSimpleNode(XmlElement n, string v) => EditNode(n, v);

    // edit_info_name: empty value removes the node.
    private static bool EditInfoName(XmlElement n, string v)
    {
        if (v.Length == 0)
        {
            n.ParentNode?.RemoveChild(n);
            return true;
        }
        return EditNode(n, v);
    }

    // show_type
    private static string ShowType(XmlElement n) => n.Name;

    // show_rpc_name / edit_rpc_name
    private static string ShowRpcName(XmlElement n) => Is(n, "rpc") ? ShowAttr(n, "rpcname") : ShowNode(n);
    private static bool EditRpcName(XmlElement n, string v) =>
        Is(n, "rpc") ? EditAttr(n, "rpcname", v) : EditNode(n, v);

    // show_orig_name / edit_orig_name
    private static string ShowOrigName(XmlElement n) => Is(n, "orig") ? ShowAttr(n, "origname") : ShowNode(n);
    private static bool EditOrigName(XmlElement n, string v) =>
        Is(n, "orig") ? EditAttr(n, "origname", v) : EditNode(n, v);

    // show_ent_code / edit_ent_code
    private static string ShowEntCode(XmlElement n) =>
        Is(n, "orig") || Is(n, "rpc") ? ShowNode(n) : ShowAttr(n, "enterpriseCode");
    private static bool EditEntCode(XmlElement n, string v) =>
        Is(n, "orig") || Is(n, "rpc") ? EditNode(n, v) : EditAttr(n, "enterpriseCode", v);

    // show_sec_class / edit_sec_class
    private static string ShowSecClass(XmlElement n) =>
        Has(n, "securityClassification") ? ShowAttr(n, "securityClassification") : ShowAttr(n, "class");
    private static bool EditSecClass(XmlElement n, string v) =>
        Has(n, "securityClassification") ? EditAttr(n, "securityClassification", v) : EditAttr(n, "class", v);

    // show_issue_type / edit_issue_type
    private static string ShowIssueType(XmlElement n) =>
        Is(n, "issno") ? ShowAttr(n, "type") : ShowAttr(n, "issueType");
    private static bool EditIssueType(XmlElement n, string v) =>
        Is(n, "issno") ? EditAttr(n, "type", v) : EditAttr(n, "issueType", v);

    // show_language_iso_code / edit_language_iso_code
    private static string ShowLanguageIso(XmlElement n) =>
        Has(n, "languageIsoCode") ? ShowAttr(n, "languageIsoCode") : ShowAttr(n, "language");
    private static bool EditLanguageIso(XmlElement n, string v) =>
        Has(n, "languageIsoCode") ? EditAttr(n, "languageIsoCode", v) : EditAttr(n, "language", v);

    // show_country_iso_code / edit_country_iso_code
    private static string ShowCountryIso(XmlElement n) =>
        Has(n, "countryIsoCode") ? ShowAttr(n, "countryIsoCode") : ShowAttr(n, "country");
    private static bool EditCountryIso(XmlElement n, string v) =>
        Has(n, "countryIsoCode") ? EditAttr(n, "countryIsoCode", v) : EditAttr(n, "country", v);

    // show_issue_number / edit_issue_number
    private static string ShowIssueNumber(XmlElement n) =>
        Has(n, "issueNumber") ? ShowAttr(n, "issueNumber") : ShowAttr(n, "issno");
    private static bool EditIssueNumber(XmlElement n, string v) => EditAttr(n, "issueNumber", v);

    // show_in_work / edit_in_work
    private static string ShowInWork(XmlElement n) =>
        Has(n, "inWork") ? ShowAttr(n, "inWork") : Has(n, "inwork") ? ShowAttr(n, "inwork") : "00";
    private static bool EditInWork(XmlElement n, string v) => EditAttr(n, "inWork", v);

    // get_issue_info (issueNumber-inWork, defaulting inWork to 00)
    private static string GetIssueInfo(XmlElement n)
    {
        string i = LocalString(n, "@issueNumber|@issno") ?? string.Empty;
        string w = LocalString(n, "@inWork|@inwork") ?? "00";
        return $"{i}-{w}";
    }

    // get_language (languageIso-countryIso)
    private static string GetLanguage(XmlElement n)
    {
        string l = LocalString(n, "@languageIsoCode|@language") ?? string.Empty;
        string c = LocalString(n, "@countryIsoCode|@country") ?? string.Empty;
        return $"{l}-{c}";
    }

    // get_issue_date / edit_issue_date
    private static string? GetIssueDate(XmlElement n)
    {
        if (!int.TryParse(n.GetAttribute("year"), out int y) ||
            !int.TryParse(n.GetAttribute("month"), out int m) ||
            !int.TryParse(n.GetAttribute("day"), out int d))
        {
            return null;
        }
        try
        {
            // Default time format is %Y-%m-%d.
            return new DateTime(y, m, d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool EditIssueDate(XmlElement n, string v)
    {
        var match = Regex.Match(v, @"^(\d{1,4})-(\d{1,2})-(\d{1,2})");
        if (!match.Success)
        {
            return false;
        }
        EditAttr(n, "year", match.Groups[1].Value);
        EditAttr(n, "month", match.Groups[2].Value);
        EditAttr(n, "day", match.Groups[3].Value);
        return true;
    }

    // ----- code assembly (get_dmcode / get_pmcode / get_ddncode / get_dmlcode / get_comment_code) -----

    private static string? GetDmCode(XmlElement n)
    {
        string? mic, sdc, sc, ssc, sssc, ac, dc, dcv, ic, icv, ilc;
        string? lc = null, lec = null;

        if (Is(n, "dmCode"))
        {
            mic = AttrOrNull(n, "modelIdentCode");
            sdc = AttrOrNull(n, "systemDiffCode");
            sc = AttrOrNull(n, "systemCode");
            ssc = AttrOrNull(n, "subSystemCode");
            sssc = AttrOrNull(n, "subSubSystemCode");
            ac = AttrOrNull(n, "assyCode");
            dc = AttrOrNull(n, "disassyCode");
            dcv = AttrOrNull(n, "disassyCodeVariant");
            ic = AttrOrNull(n, "infoCode");
            icv = AttrOrNull(n, "infoCodeVariant");
            ilc = AttrOrNull(n, "itemLocationCode");
            lc = AttrOrNull(n, "learnCode");
            lec = AttrOrNull(n, "learnEventCode");
        }
        else
        {
            mic = LocalString(n, "modelic");
            sdc = LocalString(n, "sdc");
            sc = LocalString(n, "chapnum");
            ssc = LocalString(n, "section");
            sssc = LocalString(n, "subsect");
            ac = LocalString(n, "subject");
            dc = LocalString(n, "discode");
            dcv = LocalString(n, "discodev");
            ic = LocalString(n, "incode");
            icv = LocalString(n, "incodev");
            ilc = LocalString(n, "itemloc");
        }

        if (mic == null || sdc == null || sc == null || ssc == null || sssc == null ||
            ac == null || dc == null || dcv == null || ic == null || icv == null || ilc == null)
        {
            return null;
        }

        string learn = lc != null && lec != null ? $"-{lc}{lec}" : string.Empty;
        return $"{mic}-{sdc}-{sc}-{ssc}{sssc}-{ac}-{dc}{dcv}-{ic}{icv}-{ilc}{learn}";
    }

    // edit_dmcode
    private static bool EditDmCode(XmlElement n, string v)
    {
        string s = v.StartsWith("DMC-", StringComparison.Ordinal) ? v[4..] : v;
        // mic-sdc-sc-(ssc)(sssc)-ac-(dc)(dcv)-(ic)(icv)-ilc[-(lc)(lec)]
        var m = Regex.Match(s,
            @"^([^-]{1,14})-([^-]{1,4})-([^-]{1,3})-(.)(.)-([^-]{1,4})-(..)([^-]{1,3})-(...)(.)-(.)(?:-(...)(.))?$");
        if (!m.Success)
        {
            return false;
        }

        bool has13 = m.Groups[12].Success && m.Groups[13].Success;

        if (Is(n, "dmCode"))
        {
            EditAttr(n, "modelIdentCode", m.Groups[1].Value);
            EditAttr(n, "systemDiffCode", m.Groups[2].Value);
            EditAttr(n, "systemCode", m.Groups[3].Value);
            EditAttr(n, "subSystemCode", m.Groups[4].Value);
            EditAttr(n, "subSubSystemCode", m.Groups[5].Value);
            EditAttr(n, "assyCode", m.Groups[6].Value);
            EditAttr(n, "disassyCode", m.Groups[7].Value);
            EditAttr(n, "disassyCodeVariant", m.Groups[8].Value);
            EditAttr(n, "infoCode", m.Groups[9].Value);
            EditAttr(n, "infoCodeVariant", m.Groups[10].Value);
            EditAttr(n, "itemLocationCode", m.Groups[11].Value);
            if (has13)
            {
                EditAttr(n, "learnCode", m.Groups[12].Value);
                EditAttr(n, "learnEventCode", m.Groups[13].Value);
            }
        }
        else
        {
            EditNode(Local(n, "modelic"), m.Groups[1].Value);
            EditNode(Local(n, "sdc"), m.Groups[2].Value);
            EditNode(Local(n, "chapnum"), m.Groups[3].Value);
            EditNode(Local(n, "section"), m.Groups[4].Value);
            EditNode(Local(n, "subsect"), m.Groups[5].Value);
            EditNode(Local(n, "subject"), m.Groups[6].Value);
            EditNode(Local(n, "discode"), m.Groups[7].Value);
            EditNode(Local(n, "discodev"), m.Groups[8].Value);
            EditNode(Local(n, "incode"), m.Groups[9].Value);
            EditNode(Local(n, "incodev"), m.Groups[10].Value);
            EditNode(Local(n, "itemloc"), m.Groups[11].Value);
        }
        return true;
    }

    private static string? GetPmCode(XmlElement n)
    {
        string? mic = LocalString(n, "@modelIdentCode|modelic");
        string? issuer = LocalString(n, "@pmIssuer|pmissuer");
        string? number = LocalString(n, "@pmNumber|pmnumber");
        string? volume = LocalString(n, "@pmVolume|pmvolume");
        if (mic == null || issuer == null || number == null || volume == null)
        {
            return null;
        }
        return $"{mic}-{issuer}-{number}-{volume}";
    }

    private static bool EditPmCode(XmlElement n, string v)
    {
        string s = v.StartsWith("PMC-", StringComparison.Ordinal) ? v[4..] : v;
        var m = Regex.Match(s, @"^([^-]{1,14})-([^-]{1,5})-([^-]{1,5})-(.{1,2})$");
        if (!m.Success)
        {
            return false;
        }
        if (Is(n, "pmCode"))
        {
            EditAttr(n, "modelIdentCode", m.Groups[1].Value);
            EditAttr(n, "pmIssuer", m.Groups[2].Value);
            EditAttr(n, "pmNumber", m.Groups[3].Value);
            EditAttr(n, "pmVolume", m.Groups[4].Value);
        }
        else
        {
            EditNode(Local(n, "modelic"), m.Groups[1].Value);
            EditNode(Local(n, "pmissuer"), m.Groups[2].Value);
            EditNode(Local(n, "pmnumber"), m.Groups[3].Value);
            EditNode(Local(n, "pmvolume"), m.Groups[4].Value);
        }
        return true;
    }

    private static string? GetDdnCode(XmlElement n)
    {
        string? mic = LocalString(n, "@modelIdentCode|modelic");
        string? send = LocalString(n, "@senderIdent|sendid");
        string? recv = LocalString(n, "@receiverIdent|recvid");
        string? year = LocalString(n, "@yearOfDataIssue|diyear");
        string? seq = LocalString(n, "@seqNumber|seqnum");
        if (mic == null || send == null || recv == null || year == null || seq == null)
        {
            return null;
        }
        return $"{mic}-{send}-{recv}-{year}-{seq}";
    }

    private static string? GetDmlCode(XmlElement n)
    {
        string? mic = LocalString(n, "@modelIdentCode|modelic");
        string? send = LocalString(n, "@senderIdent|sendid");
        string? type = LocalString(n, "@dmlType|dmltype/@type");
        string? year = LocalString(n, "@yearOfDataIssue|diyear");
        string? seq = LocalString(n, "@seqNumber|seqnum");
        if (mic == null || send == null || type == null || year == null || seq == null)
        {
            return null;
        }
        return $"{mic}-{send}-{type}-{year}-{seq}";
    }

    private static string? GetCommentCode(XmlElement n)
    {
        string? mic, send, year, seq, type;
        if (Is(n, "commentCode"))
        {
            mic = AttrOrNull(n, "modelIdentCode");
            send = AttrOrNull(n, "senderIdent");
            year = AttrOrNull(n, "yearOfDataIssue");
            seq = AttrOrNull(n, "seqNumber");
            type = AttrOrNull(n, "commentType");
        }
        else
        {
            mic = LocalString(n, "modelic");
            send = LocalString(n, "sendid");
            year = LocalString(n, "diyear");
            seq = LocalString(n, "seqnum");
            type = LocalString(n, "ctype/@type");
        }
        if (mic == null || send == null || year == null || seq == null || type == null)
        {
            return null;
        }
        return $"{mic}-{send}-{year}-{seq}-{type}";
    }

    // get_code / edit_code
    private static string? GetCode(XmlElement n)
    {
        if (Is(n, "dmCode") || Is(n, "avee")) return GetDmCode(n);
        if (Is(n, "pmCode") || Is(n, "pmc")) return GetPmCode(n);
        if (Is(n, "commentCode") || Is(n, "ccode")) return GetCommentCode(n);
        if (Is(n, "ddnCode") || Is(n, "ddnc")) return GetDdnCode(n);
        if (Is(n, "dmlCode") || Is(n, "dmlc")) return GetDmlCode(n);
        return null;
    }

    private static bool EditCode(XmlElement n, string v)
    {
        if (Is(n, "dmCode") || Is(n, "avee")) return EditDmCode(n, v);
        if (Is(n, "pmCode") || Is(n, "pmc")) return EditPmCode(n, v);
        return false; // EXIT_NO_EDIT
    }

    // ----- comment priority / response / type -----

    private static string ShowCommentPriority(XmlElement n) =>
        Is(n, "priority") ? ShowAttr(n, "cprio") : ShowAttr(n, "commentPriorityCode");
    private static bool EditCommentPriority(XmlElement n, string v) =>
        Is(n, "priority") ? EditAttr(n, "cprio", v) : EditAttr(n, "commentPriorityCode", v);

    private static string ShowCommentResponse(XmlElement n) =>
        Is(n, "response") ? ShowAttr(n, "rsptype") : ShowAttr(n, "responseType");
    private static bool EditCommentResponse(XmlElement n, string v) =>
        Is(n, "response") ? EditAttr(n, "rsptype", v) : EditAttr(n, "responseType", v);

    private static string ShowCommentType(XmlElement n) =>
        Is(n, "ctype") ? ShowAttr(n, "type") : ShowAttr(n, "commentType");
    private static bool EditCommentType(XmlElement n, string v) =>
        Is(n, "ctype") ? EditAttr(n, "type", v) : EditAttr(n, "commentType", v);

    // ----- single code components (modern attr vs legacy element) -----

    private static string ShowMic(XmlElement n) =>
        Is(n, "modelic") ? ShowNode(n) : ShowAttr(n, "modelIdentCode");
    private static bool EditMic(XmlElement n, string v) =>
        Is(n, "modelic") ? EditNode(n, v) : EditAttr(n, "modelIdentCode", v);

    private static string ShowSdc(XmlElement n) =>
        Is(n, "sdc") ? ShowNode(n) : ShowAttr(n, "systemDiffCode");
    private static bool EditSdc(XmlElement n, string v) =>
        Is(n, "sdc") ? EditNode(n, v) : EditAttr(n, "systemDiffCode", v);

    private static string ShowSc(XmlElement n) =>
        Is(n, "chapnum") ? ShowNode(n) : ShowAttr(n, "systemCode");
    private static bool EditSc(XmlElement n, string v) =>
        Is(n, "chapnum") ? EditNode(n, v) : EditAttr(n, "systemCode", v);

    private static string ShowSsc(XmlElement n) =>
        Is(n, "section") ? ShowNode(n) : ShowAttr(n, "subSystemCode");
    private static bool EditSsc(XmlElement n, string v) =>
        Is(n, "section") ? EditNode(n, v) : EditAttr(n, "subSystemCode", v);

    private static string ShowSssc(XmlElement n) =>
        Is(n, "subsect") ? ShowNode(n) : ShowAttr(n, "subSubSystemCode");
    private static bool EditSssc(XmlElement n, string v) =>
        Is(n, "subsect") ? EditNode(n, v) : EditAttr(n, "subSubSystemCode", v);

    private static string ShowAc(XmlElement n) =>
        Is(n, "subject") ? ShowNode(n) : ShowAttr(n, "assyCode");
    private static bool EditAc(XmlElement n, string v) =>
        Is(n, "subject") ? EditNode(n, v) : EditAttr(n, "assyCode", v);

    private static string ShowDc(XmlElement n) =>
        Is(n, "discode") ? ShowNode(n) : ShowAttr(n, "disassyCode");
    private static bool EditDc(XmlElement n, string v) =>
        Is(n, "discode") ? EditNode(n, v) : EditAttr(n, "disassyCode", v);

    private static string ShowDcv(XmlElement n) =>
        Is(n, "discodev") ? ShowNode(n) : ShowAttr(n, "disassyCodeVariant");
    private static bool EditDcv(XmlElement n, string v) =>
        Is(n, "discodev") ? EditNode(n, v) : EditAttr(n, "disassyCodeVariant", v);

    private static string ShowIc(XmlElement n) =>
        Is(n, "incode") ? ShowNode(n) : ShowAttr(n, "infoCode");
    private static bool EditIc(XmlElement n, string v) =>
        Is(n, "incode") ? EditNode(n, v) : EditAttr(n, "infoCode", v);

    private static string ShowIcv(XmlElement n) =>
        Is(n, "incodev") ? ShowNode(n) : ShowAttr(n, "infoCodeVariant");
    private static bool EditIcv(XmlElement n, string v) =>
        Is(n, "incodev") ? EditNode(n, v) : EditAttr(n, "infoCodeVariant", v);

    private static string ShowIlc(XmlElement n) =>
        Is(n, "itemloc") ? ShowNode(n) : ShowAttr(n, "itemLocationCode");
    private static bool EditIlc(XmlElement n, string v) =>
        Is(n, "itemloc") ? EditNode(n, v) : EditAttr(n, "itemLocationCode", v);

    private static string ShowLearnCode(XmlElement n) => ShowAttr(n, "learnCode");
    private static bool EditLearnCode(XmlElement n, string v) => EditAttr(n, "learnCode", v);

    private static string ShowLearnEventCode(XmlElement n) => ShowAttr(n, "learnEventCode");
    private static bool EditLearnEventCode(XmlElement n, string v) => EditAttr(n, "learnEventCode", v);

    private static string ShowSeqNumber(XmlElement n) =>
        Is(n, "seqnum") ? ShowNode(n) : ShowAttr(n, "seqNumber");
    private static bool EditSeqNumber(XmlElement n, string v) =>
        Is(n, "seqnum") ? EditNode(n, v) : EditAttr(n, "seqNumber", v);

    private static string ShowYear(XmlElement n) =>
        Is(n, "diyear") ? ShowNode(n) : ShowAttr(n, "yearOfDataIssue");
    private static bool EditYear(XmlElement n, string v) =>
        Is(n, "diyear") ? EditNode(n, v) : EditAttr(n, "yearOfDataIssue", v);

    private static string ShowSenderIdent(XmlElement n) =>
        Is(n, "sendid") ? ShowNode(n) : ShowAttr(n, "senderIdent");
    private static bool EditSenderIdent(XmlElement n, string v) =>
        Is(n, "sendid") ? EditNode(n, v) : EditAttr(n, "senderIdent", v);

    private static string ShowReceiverIdent(XmlElement n) =>
        Is(n, "recvid") ? ShowNode(n) : ShowAttr(n, "receiverIdent");
    private static bool EditReceiverIdent(XmlElement n, string v) =>
        Is(n, "recvid") ? EditNode(n, v) : EditAttr(n, "receiverIdent", v);

    private static string ShowPmIssuer(XmlElement n) =>
        Is(n, "pmissuer") ? ShowNode(n) : ShowAttr(n, "pmIssuer");
    private static bool EditPmIssuer(XmlElement n, string v) =>
        Is(n, "pmissuer") ? EditNode(n, v) : EditAttr(n, "pmIssuer", v);

    private static string ShowPmNumber(XmlElement n) =>
        Is(n, "pmnumber") ? ShowNode(n) : ShowAttr(n, "pmNumber");
    private static bool EditPmNumber(XmlElement n, string v) =>
        Is(n, "pmnumber") ? EditNode(n, v) : EditAttr(n, "pmNumber", v);

    private static string ShowPmVolume(XmlElement n) =>
        Is(n, "pmvolume") ? ShowNode(n) : ShowAttr(n, "pmVolume");
    private static bool EditPmVolume(XmlElement n, string v) =>
        Is(n, "pmvolume") ? EditNode(n, v) : EditAttr(n, "pmVolume", v);

    // ----- skill level -----

    private static string ShowSkillLevel(XmlElement n) =>
        Is(n, "skill") ? ShowAttr(n, "skill") : ShowAttr(n, "skillLevelCode");
    private static bool EditSkillLevel(XmlElement n, string v) =>
        Is(n, "skill") ? EditAttr(n, "skill", v) : EditAttr(n, "skillLevelCode", v);

    // ----- verification (quality assurance) -----

    private static string GetQa(XmlElement n)
    {
        if (Local(n, "secondVerification|secver") != null) return "secondVerification";
        if (Local(n, "firstVerification|firstver") != null) return "firstVerification";
        return "unverified";
    }

    private static string ShowVerificationType(XmlElement n) =>
        Is(n, "firstVerification") || Is(n, "secondVerification")
            ? ShowAttr(n, "verificationType")
            : ShowAttr(n, "type");

    private static bool EditFirstVerificationType(XmlElement n, string v)
    {
        if (v == "unverified")
        {
            XmlNode? qa = n.ParentNode;
            if (qa == null)
            {
                return false;
            }
            while (qa.FirstChild != null)
            {
                qa.RemoveChild(qa.FirstChild);
            }
            var doc = qa.OwnerDocument!;
            qa.AppendChild(doc.CreateElement(Is(n, "firstVerification") ? "unverified" : "unverif"));
            return true;
        }
        return Is(n, "firstVerification") ? EditAttr(n, "verificationType", v) : EditAttr(n, "type", v);
    }

    private static bool EditSecondVerificationType(XmlElement n, string v)
    {
        if (v == "unverified")
        {
            n.ParentNode?.RemoveChild(n);
            return true;
        }
        return Is(n, "secondVerification") ? EditAttr(n, "verificationType", v) : EditAttr(n, "type", v);
    }

    // ----- remarks / reasonForUpdate -----

    private static string? GetRemarksOrRfu(XmlElement n) => LocalString(n, "simplePara|p");

    private static bool EditRemarksOrRfu(XmlElement n, string v)
    {
        XmlNode? p = Local(n, "simplePara|p");
        return EditNode(p, v);
    }

    // ----- title -----

    private static string ShowTitle(XmlElement n)
    {
        if (Is(n, "dmTitle") || Is(n, "dmtitle"))
        {
            XmlNode? tech = Local(n, "techName|techname");
            XmlNode? info = Local(n, "infoName|infoname");
            XmlNode? vari = Local(n, "infoNameVariant");
            string result = tech?.InnerText ?? string.Empty;
            if (info != null)
            {
                result += $" - {info.InnerText}";
                if (vari != null)
                {
                    result += $", {vari.InnerText}";
                }
            }
            return result;
        }
        return ShowNode(n);
    }

    // ----- source (full source DM/PM identification) -----

    private static string? ShowSource(XmlElement n)
    {
        XmlElement? dmc = Local(n, "dmCode|pmCode|dmc/avee") as XmlElement;
        XmlElement? issno = Local(n, "issueInfo|issno") as XmlElement;
        XmlElement? lang = Local(n, "language") as XmlElement;
        if (dmc == null || issno == null || lang == null)
        {
            return null;
        }

        string code = Is(dmc, "pmCode") ? "PMC-" + (GetPmCode(dmc) ?? "") : "DMC-" + (GetDmCode(dmc) ?? "");
        string issue = $"{ShowIssueNumber(issno)}-{ShowInWork(issno)}";
        string l = $"{ShowLanguageIso(lang)}-{ShowCountryIso(lang)}";
        return $"{code}_{issue}_{l}";
    }

    // ----- schema (name from URL) / schemaUrl -----

    private static string? GetSchema(XmlElement n)
    {
        string url = n.GetAttribute("noNamespaceSchemaLocation", Xsi);
        if (string.IsNullOrEmpty(url))
        {
            // Some documents use the local attribute name.
            url = n.GetAttribute("noNamespaceSchemaLocation");
        }
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }
        int slash = url.LastIndexOf('/');
        string s = slash >= 0 ? url[(slash + 1)..] : url;
        int dot = s.LastIndexOf('.');
        if (dot >= 0)
        {
            s = s[..dot];
        }
        return s;
    }

    private static string ShowSchemaUrl(XmlElement n)
    {
        string url = n.GetAttribute("noNamespaceSchemaLocation", Xsi);
        return string.IsNullOrEmpty(url) ? n.GetAttribute("noNamespaceSchemaLocation") : url;
    }

    private static bool EditSchemaUrl(XmlElement n, string v)
    {
        n.SetAttribute("noNamespaceSchemaLocation", Xsi, v);
        return true;
    }

    // ----- issue (of S1000D), derived from / set on the schema URL -----

    private static readonly Regex IssueRe = new(@"S1000D_([0-9]+)-([0-9]+)", RegexOptions.Compiled);

    private static string? GetIssue(XmlElement n)
    {
        string url = n.GetAttribute("noNamespaceSchemaLocation", Xsi);
        if (string.IsNullOrEmpty(url))
        {
            url = n.GetAttribute("noNamespaceSchemaLocation");
        }
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }
        var m = IssueRe.Match(url);
        return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : null;
    }

    private static readonly Dictionary<string, string> IssueMap = new(StringComparer.Ordinal)
    {
        ["2.0"] = "2-0", ["2.1"] = "2-1", ["2.2"] = "2-2", ["2.3"] = "2-3",
        ["3.0"] = "3-0", ["4.0"] = "4-0", ["4.1"] = "4-1", ["4.2"] = "4-2",
        ["5.0"] = "5-0", ["6"] = "6",
    };

    private static bool EditIssue(XmlElement n, string v)
    {
        string url = n.GetAttribute("noNamespaceSchemaLocation", Xsi);
        bool ns = !string.IsNullOrEmpty(url);
        if (!ns)
        {
            url = n.GetAttribute("noNamespaceSchemaLocation");
        }
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }
        var m = Regex.Match(url, @"[^/]+\.xsd$");
        if (!m.Success)
        {
            return false; // EXIT_MISSING_METADATA
        }
        if (!IssueMap.TryGetValue(v, out string? mapped))
        {
            return false; // EXIT_INVALID_VALUE
        }
        string schema = m.Value;
        string xsi = $"http://www.s1000d.org/S1000D_{mapped}/xml_schema_flat/{schema}";
        if (ns)
        {
            n.SetAttribute("noNamespaceSchemaLocation", Xsi, xsi);
        }
        else
        {
            n.SetAttribute("noNamespaceSchemaLocation", xsi);
        }
        return true;
    }

    private static string? AttrOrNull(XmlElement n, string attr) =>
        n.HasAttribute(attr) ? n.GetAttribute(attr) : null;

    // ----- create_* handlers -----

    private static bool CreateInfoName(XmlDocument doc, string val)
    {
        XmlElement? techName = FirstElement(doc, "//techName|//techname");
        if (techName == null)
        {
            return false;
        }
        string name = Is(techName, "techName") ? "infoName" : "infoname";
        XmlElement infoName = NewElement(doc, name);
        techName.ParentNode!.InsertAfter(infoName, techName);
        infoName.InnerText = val;
        return true;
    }

    private static bool CreateInfoNameVariant(XmlDocument doc, string val)
    {
        XmlElement? infoName = FirstElement(doc, "//infoName");
        if (infoName == null)
        {
            return false;
        }
        XmlElement variant = NewElement(doc, "infoNameVariant");
        infoName.ParentNode!.InsertAfter(variant, infoName);
        variant.InnerText = val;
        return true;
    }

    private static bool CreateEntName(XmlElement node, string val)
    {
        XmlElement name = node.OwnerDocument!.CreateElement("enterpriseName");
        name.InnerText = val;
        node.AppendChild(name);
        return true;
    }

    private static bool CreateRpcName(XmlDocument doc, string val)
    {
        XmlElement? node = FirstElement(doc, "//responsiblePartnerCompany|//rpc");
        if (node == null)
        {
            return false;
        }
        return Is(node, "rpc") ? EditAttr(node, "rpcname", val) : CreateEntName(node, val);
    }

    private static bool CreateOrigName(XmlDocument doc, string val)
    {
        XmlElement? node = FirstElement(doc, "//originator|//orig");
        if (node == null)
        {
            return false;
        }
        return Is(node, "orig") ? EditAttr(node, "origname", val) : CreateEntName(node, val);
    }

    private static bool CreateRpcEntCode(XmlDocument doc, string val)
    {
        XmlElement? node = FirstElement(doc, "//rpc|//responsiblePartnerCompany");
        if (node == null)
        {
            return false;
        }
        return Is(node, "rpc") ? EditNode(node, val) : EditAttr(node, "enterpriseCode", val);
    }

    private static bool CreateOrigEntCode(XmlDocument doc, string val)
    {
        XmlElement? node = FirstElement(doc, "//orig|//originator");
        if (node == null)
        {
            return false;
        }
        return Is(node, "orig") ? EditNode(node, val) : EditAttr(node, "enterpriseCode", val);
    }

    private static bool CreateActRef(XmlDocument doc, string val)
    {
        XmlElement? orig = FirstElement(doc, "//dmStatus/originator");
        if (orig == null)
        {
            return false;
        }
        XmlElement actRef = NewElement(doc, "applicCrossRefTableRef");
        orig.ParentNode!.InsertAfter(actRef, orig);
        XmlElement dmRef = NewElement(doc, "dmRef");
        actRef.AppendChild(dmRef);
        XmlElement dmRefIdent = NewElement(doc, "dmRefIdent");
        dmRef.AppendChild(dmRefIdent);
        XmlElement dmCode = NewElement(doc, "dmCode");
        dmRefIdent.AppendChild(dmCode);
        return EditDmCode(dmCode, val);
    }

    private static bool CreateCommentTitle(XmlDocument doc, string val)
    {
        XmlElement? issueDate = FirstElement(doc, "//commentAddressItems/issueDate");
        if (issueDate == null)
        {
            return false;
        }
        XmlElement title = NewElement(doc, "commentTitle");
        issueDate.ParentNode!.InsertAfter(title, issueDate);
        title.InnerText = val;
        return true;
    }

    private static bool CreateSkillLevel(XmlDocument doc, string val)
    {
        XmlElement? node = FirstElement(doc,
            "(//qualityAssurance|//qa|//systemBreakdownCode|//sbc|" +
            "//functionalItemCode|//fic|//dmStatus/functionalItemRef|//status/ein)[last()]");
        if (node == null)
        {
            return false;
        }
        bool iss30 = node.ParentNode?.LocalName == "status";
        XmlElement skillLevel = NewElement(doc, iss30 ? "skill" : "skillLevel");
        node.ParentNode!.InsertAfter(skillLevel, node);
        skillLevel.SetAttribute(iss30 ? "skill" : "skillLevelCode", val);
        return true;
    }

    private static bool CreateFirstVerification(XmlDocument doc, string val)
    {
        if (!(val == "tabtop" || val == "onobject" || val == "ttandoo"))
        {
            return true; // C returns 0 (success) without changes
        }
        XmlElement? unverif = FirstElement(doc, "//unverified|//unverif");
        if (unverif == null)
        {
            return false;
        }
        XmlElement first;
        if (Is(unverif, "unverified"))
        {
            first = NewElement(doc, "firstVerification");
            first.SetAttribute("verificationType", val);
        }
        else
        {
            first = NewElement(doc, "firstver");
            first.SetAttribute("type", val);
        }
        unverif.ParentNode!.InsertAfter(first, unverif);
        unverif.ParentNode.RemoveChild(unverif);
        return true;
    }

    private static bool CreateSecondVerification(XmlDocument doc, string val)
    {
        if (!(val == "tabtop" || val == "onobject" || val == "ttandoo"))
        {
            return true;
        }
        XmlElement? first = FirstElement(doc, "//firstVerification|//firstver");
        if (first == null)
        {
            return false;
        }
        XmlElement sec;
        if (Is(first, "firstVerification"))
        {
            sec = NewElement(doc, "secondVerification");
            sec.SetAttribute("verificationType", val);
        }
        else
        {
            sec = NewElement(doc, "secver");
            sec.SetAttribute("type", val);
        }
        first.ParentNode!.InsertAfter(sec, first);
        return true;
    }

    private const string StatusChildrenPath =
        "(//dmStatus/*|//status/*|//pmStatus/*|//pmstatus/*|//commentStatus/*|" +
        "//ddnStatus/*|//dmlStatus/*|//scormContentPackageStatus/*)[last()]";

    private static bool CreateRemarks(XmlDocument doc, string val)
    {
        XmlElement? node = FirstElement(doc, StatusChildrenPath);
        if (node == null)
        {
            return false;
        }
        bool iss30 = node.ParentNode?.LocalName is "status" or "pmstatus";
        XmlElement remarks = NewElement(doc, "remarks");
        node.ParentNode!.InsertAfter(remarks, node);
        XmlElement p = NewElement(doc, iss30 ? "p" : "simplePara");
        p.InnerText = val;
        remarks.AppendChild(p);
        return true;
    }

    private static bool CreateRfu(XmlDocument doc, string val)
    {
        XmlElement? node = FirstElement(doc,
            "(//dmStatus/*|//status/*|//pmStatus/*|//pmstatus/*|//commentStatus/*|" +
            "//ddnStatus/*|//dmlStatus/*|//scormContentPackageStatus/*)" +
            "[not(self::productSafety or self::remarks)][last()]");
        if (node == null)
        {
            return false;
        }
        bool iss30 = node.ParentNode?.LocalName is "status" or "pmstatus";
        XmlElement rfu = NewElement(doc, iss30 ? "rfu" : "reasonForUpdate");
        node.ParentNode!.InsertAfter(rfu, node);
        XmlElement p = NewElement(doc, iss30 ? "p" : "simplePara");
        p.InnerText = val;
        rfu.AppendChild(p);
        return true;
    }

    // ----- table construction -----

    private static Entry[] BuildTable() => new[]
    {
        new Entry("act", "//applicCrossRefTableRef/dmRef/dmRefIdent/dmCode",
            "ACT data module code", GetDmCode, EditDmCode, CreateActRef),
        new Entry("applic", "//applic/displayText/simplePara|//applic/displaytext/p",
            "Whole data module applicability", ShowSimpleNode, EditSimpleNode, null),
        new Entry("assyCode", "//@assyCode|//avee/subject",
            "Assembly code", ShowAc, EditAc, null),
        new Entry("authorization", "//authorization|//authrtn",
            "Authorization for a DDN", ShowSimpleNode, EditSimpleNode, null),
        new Entry("brex", "//brexDmRef/dmRef/dmRefIdent/dmCode|//brexref/refdm/avee",
            "BREX data module code", GetDmCode, EditDmCode, null),
        new Entry("code", "//dmCode|//avee|//pmCode|//pmc|//commentCode|//ccode|//ddnCode|//ddnc|//dmlCode|//dmlc",
            "CSDB object code", GetCode, EditCode, null),
        new Entry("commentCode", "//commentCode|//ccode",
            "Comment code", GetCommentCode, null, null),
        new Entry("commentPriority", "//commentPriority/@commentPriorityCode|//priority/@cprio",
            "Priority code of a comment", ShowCommentPriority, EditCommentPriority, null),
        new Entry("commentResponse", "//commentResponse/@responseType|//response/@rsptype",
            "Response type of a comment", ShowCommentResponse, EditCommentResponse, null),
        new Entry("commentTitle", "//commentTitle|//ctitle",
            "Title of a comment", ShowSimpleNode, EditSimpleNode, CreateCommentTitle),
        new Entry("commentType", "//@commentType|//ctype/@type",
            "Type of a comment", ShowCommentType, EditCommentType, null),
        new Entry("countryIsoCode", "//language/@countryIsoCode|//language/@country",
            "Country ISO code (CA, US, GB...)", ShowCountryIso, EditCountryIso, null),
        new Entry("ddnCode", "//ddnCode|//ddnc",
            "Data dispatch note code", GetDdnCode, null, null),
        new Entry("disassyCode", "//@disassyCode|//discode",
            "Disassembly code", ShowDc, EditDc, null),
        new Entry("disassyCodeVariant", "//@disassyCodeVariant|//discodev",
            "Disassembly code variant", ShowDcv, EditDcv, null),
        new Entry("dmCode", "//dmCode|//avee",
            "Data module code", GetDmCode, EditDmCode, null),
        new Entry("dmlCode", "//dmlCode|//dmlc",
            "Data management list code", GetDmlCode, null, null),
        new Entry("firstVerificationType", "//firstVerification/@verificationType|//firstver/@type",
            "First verification type", ShowVerificationType, EditFirstVerificationType, CreateFirstVerification),
        new Entry("icnTitle", "//imfAddressItems/icnTitle",
            "Title of an IMF", ShowSimpleNode, EditSimpleNode, null),
        new Entry("infoCode", "//@infoCode|//incode",
            "Information code", ShowIc, EditIc, null),
        new Entry("infoCodeVariant", "//@infoCodeVariant|//incodev",
            "Information code variant", ShowIcv, EditIcv, null),
        new Entry("infoName", "//infoName|//infoname",
            "Information name of a data module", ShowSimpleNode, EditInfoName, CreateInfoName),
        new Entry("infoNameVariant", "//infoNameVariant",
            "Information name variant of a data module", ShowSimpleNode, EditInfoName, CreateInfoNameVariant),
        new Entry("inWork", "//issueInfo/@inWork|//issno",
            "Inwork issue number (NN)", ShowInWork, EditInWork, null),
        new Entry("issue", "//*",
            "Issue of S1000D", GetIssue, EditIssue, null),
        new Entry("issueDate", "//issueDate|//issdate",
            "Issue date of the CSDB object", GetIssueDate, EditIssueDate, null),
        new Entry("issueInfo", "//issueInfo|//issno",
            "Issue info (NNN-NN)", GetIssueInfo, null, null),
        new Entry("issueNumber", "//issueInfo/@issueNumber|//issno/@issno",
            "Issue number (NNN)", ShowIssueNumber, EditIssueNumber, null),
        new Entry("issueType", "//dmStatus/@issueType|//pmStatus/@issueType|//issno/@type",
            "Issue type (new, changed, deleted...)", ShowIssueType, EditIssueType, null),
        new Entry("itemLocationCode", "//@itemLocationCode|//itemloc",
            "Item location code", ShowIlc, EditIlc, null),
        new Entry("language", "//language",
            "Language and country ISO codes (en-CA, en-US, fr-FR, ...)", GetLanguage, null, null),
        new Entry("languageIsoCode", "//language/@languageIsoCode|//language/@language",
            "Language ISO code (en, fr, es...)", ShowLanguageIso, EditLanguageIso, null),
        new Entry("learnCode", "//@learnCode",
            "Learn code", ShowLearnCode, EditLearnCode, null),
        new Entry("learnEventCode", "//@learnEventCode",
            "Learn event code", ShowLearnEventCode, EditLearnEventCode, null),
        new Entry("modelIdentCode", "//@modelIdentCode|//modelic",
            "Model identification code", ShowMic, EditMic, null),
        new Entry("originator", "//originator/enterpriseName|//orig/@origname",
            "Name of the originator", ShowOrigName, EditOrigName, CreateOrigName),
        new Entry("originatorCode", "//originator/@enterpriseCode|//orig[. != '']",
            "NCAGE code of the originator", ShowEntCode, EditEntCode, CreateOrigEntCode),
        new Entry("pmCode", "//pmCode|//pmc",
            "Publication module code", GetPmCode, EditPmCode, null),
        new Entry("pmIssuer", "//@pmIssuer|//pmissuer",
            "Issuing authority of the PM", ShowPmIssuer, EditPmIssuer, null),
        new Entry("pmNumber", "//@pmNumber|//pmnumber",
            "PM number", ShowPmNumber, EditPmNumber, null),
        new Entry("pmTitle", "//pmTitle|//pmtitle",
            "Title of a publication module", ShowSimpleNode, EditSimpleNode, null),
        new Entry("pmVolume", "//@pmVolume|//pmvolume",
            "Volume of the PM", ShowPmVolume, EditPmVolume, null),
        new Entry("qualityAssurance", "//qualityAssurance|//qa",
            "Quality assurance status", GetQa, null, null),
        new Entry("reasonForUpdate", "//reasonForUpdate|//rfu",
            "Reason for update", GetRemarksOrRfu, EditRemarksOrRfu, CreateRfu),
        new Entry("receiverIdent", "//@receiverIdent|//recvid",
            "Receiving authority", ShowReceiverIdent, EditReceiverIdent, null),
        new Entry("remarks",
            "//dmStatus/remarks|//status/remarks|//pmStatus/remarks|//pmstatus/remarks|//commentStatus/remarks|//dmlStatus/remarks|//ddnStatus/remarks|//scormContentPackageStatus/remarks|//imfStatus/remarks",
            "General remarks", GetRemarksOrRfu, EditRemarksOrRfu, CreateRemarks),
        new Entry("responsiblePartnerCompany", "//responsiblePartnerCompany/enterpriseName|//rpc/@rpcname",
            "Name of the RPC", ShowRpcName, EditRpcName, CreateRpcName),
        new Entry("responsiblePartnerCompanyCode", "//responsiblePartnerCompany/@enterpriseCode|//rpc[. != '']",
            "NCAGE code of the RPC", ShowEntCode, EditEntCode, CreateRpcEntCode),
        new Entry("schema", "/*",
            "S1000D schema name", GetSchema, null, null),
        new Entry("schemaUrl", "/*",
            "XML schema URL", ShowSchemaUrl, EditSchemaUrl, null),
        new Entry("secondVerificationType", "//secondVerification/@verificationType|//secver/@type",
            "Second verification type", ShowVerificationType, EditSecondVerificationType, CreateSecondVerification),
        new Entry("securityClassification", "//security/@securityClassification|//security/@class",
            "Security classification (01, 02...)", ShowSecClass, EditSecClass, null),
        new Entry("senderIdent", "//@senderIdent|//sendid",
            "Issuing authority", ShowSenderIdent, EditSenderIdent, null),
        new Entry("seqNumber", "//@seqNumber|//seqnum",
            "Sequence number", ShowSeqNumber, EditSeqNumber, null),
        new Entry("shortPmTitle", "//shortPmTitle",
            "Short title of a publication module", ShowSimpleNode, EditSimpleNode, null),
        new Entry("skillLevelCode", "//dmStatus/skillLevel/@skillLevelCode|//status/skill/@skill",
            "Skill level code of the data module", ShowSkillLevel, EditSkillLevel, CreateSkillLevel),
        new Entry("source", "//sourceDmIdent|//sourcePmIdent|//srcdmaddres",
            "Full source DM or PM identification", ShowSource, null, null),
        new Entry("sourceDmCode", "//sourceDmIdent/dmCode|//srcdmaddres/dmc/avee",
            "Source DM code", GetDmCode, EditDmCode, null),
        new Entry("sourcePmCode", "//sourcePmIdent/pmCode",
            "Source PM code", GetPmCode, EditPmCode, null),
        new Entry("sourceIssueNumber",
            "//sourceDmIdent/issueInfo/@issueNumber|//sourcePmIdent/issueInfo/@issueNumber|//srcdmaddres/issno/@issno",
            "Source DM or PM issue number", ShowIssueNumber, EditIssueNumber, null),
        new Entry("sourceInWork",
            "//sourceDmIdent/issueInfo/@inWork|//sourcePmIdent/issueInfo/@inWork|//srcdmaddres/issno",
            "Source DM or PM inwork issue number", ShowInWork, EditInWork, null),
        new Entry("sourceLanguageIsoCode",
            "//sourceDmIdent/language/@languageIsoCode|//sourcePmIdent/language/@languageIsoCode|//srcdmaddres/language/@language",
            "Source DM or PM language ISO code", ShowLanguageIso, EditLanguageIso, null),
        new Entry("sourceCountryIsoCode",
            "//sourceDmIdent/language/@countryIsoCode|//sourcePmIdent/language/@countryIsoCode|//srcdmaddres/language/@country",
            "Source DM or PM country ISO code", ShowCountryIso, EditCountryIso, null),
        new Entry("subSubSystemCode", "//@subSubSystemCode|//subsect",
            "Subsubsystem code", ShowSssc, EditSssc, null),
        new Entry("subSystemCode", "//@subSystemCode|//section",
            "Subsystem code", ShowSsc, EditSsc, null),
        new Entry("systemCode", "//@systemCode|//chapnum",
            "System code", ShowSc, EditSc, null),
        new Entry("systemDiffCode", "//@systemDiffCode|//sdc",
            "System difference code", ShowSdc, EditSdc, null),
        new Entry("techName", "//techName|//techname",
            "Technical name of a data module", ShowSimpleNode, EditSimpleNode, null),
        new Entry("title", "//dmTitle|//dmtitle|//pmTitle|//pmtitle|//commentTitle|//ctitle|//icnTitle",
            "Title of a CSDB object", ShowTitle, null, null),
        new Entry("type", "/*",
            "Name of the root element of the document", ShowType, null, null),
        // url: the C show_url prints the document's file URL. The in-memory
        // library has no associated path, so it resolves to null (read-only),
        // matching the C when no URL is set. Present for table completeness.
        new Entry("url", "/", "URL of the document", null, null, null),
        new Entry("yearOfDataIssue", "//@yearOfDataIssue|//diyear",
            "Year of data issue", ShowYear, EditYear, null),
    };

    // ----- ICN file metadata (icn_metadata[] in the C) -----

    /// <summary>
    /// The metadata keys derivable from an ICN file's name (mirrors
    /// <c>icn_metadata[]</c>): <c>code</c>, <c>issueNumber</c>,
    /// <c>securityClassification</c>, and <c>type</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> IcnKeys = new[]
    {
        "code", "issueNumber", "securityClassification", "type",
    };

    /// <summary>
    /// Retrieve an ICN metadata value derived from the ICN file's base name,
    /// e.g. <c>ICN-S1000DBIKE-AAA-D000000-0-U8025-00555-A-04-1.PNG</c>. Returns
    /// null for an unknown key or a name that does not match the ICN pattern.
    /// Mirrors the <c>icn_metadata[]</c> getters in the C tool.
    /// </summary>
    public static string? GetIcn(string fileName, string key)
    {
        string bname = Path.GetFileName(fileName);

        switch (key)
        {
            case "type":
                return "icn";

            case "code":
            {
                // Everything up to the first '.'.
                int dot = bname.IndexOf('.');
                return dot < 0 ? bname : bname[..dot];
            }

            case "securityClassification":
            {
                // The field after the last '-', up to the next '.'.
                int dash = bname.LastIndexOf('-');
                if (dash < 0)
                {
                    return null;
                }
                int start = dash + 1;
                int dot = bname.IndexOf('.', start);
                if (dot < 0)
                {
                    dot = bname.Length;
                }
                return bname[start..dot];
            }

            case "issueNumber":
            {
                // Three characters ending just before the last '-', up to the
                // following '-'. Mirrors: s = rchr('-') - 3; e = chr(s,'-').
                int dash = bname.LastIndexOf('-');
                if (dash < 3)
                {
                    return null;
                }
                int start = dash - 3;
                int end = bname.IndexOf('-', start);
                if (end < 0)
                {
                    return null;
                }
                return bname[start..end];
            }

            default:
                return null;
        }
    }
}
