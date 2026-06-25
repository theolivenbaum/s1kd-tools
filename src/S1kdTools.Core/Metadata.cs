using System.Xml;

namespace S1kdTools;

/// <summary>
/// Describes a single metadata item: its key, the XPath used to locate it, and
/// a human-readable description. Mirrors the <c>struct metadata</c> table in
/// <c>tools/s1kd-metadata/s1kd-metadata.c</c>.
/// </summary>
public sealed record MetadataKey(string Name, string XPath, string Description, bool Editable);

/// <summary>
/// Retrieve and set S1000D metadata on CSDB objects. Ports the metadata table
/// and the <c>s1kdDocGetMetadata</c> / <c>s1kdDocSetMetadata</c> API from
/// <c>libs1kd</c>.
///
/// The generic engine covers the modern (Issue 4.x+) element/attribute forms.
/// A handful of composite keys (issueInfo, issueNumber, inWork, language, type,
/// url) have dedicated handling. Legacy SGML (issno/avee) forms and node
/// creation for missing targets are partially supported — see todo.md.
/// </summary>
public static class Metadata
{
    /// <summary>The full metadata key table (ordered as in the C source).</summary>
    public static readonly IReadOnlyList<MetadataKey> Keys = new MetadataKey[]
    {
        new("act", "//applicCrossRefTableRef/dmRef/dmRefIdent/dmCode", "ACT data module code", true),
        new("applic", "//applic/displayText/simplePara|//applic/displaytext/p", "Whole data module applicability", true),
        new("assyCode", "//@assyCode|//avee/subject", "Assembly code", true),
        new("authorization", "//authorization|//authrtn", "Authorization for a DDN", true),
        new("brex", "//brexDmRef/dmRef/dmRefIdent/dmCode|//brexref/refdm/avee", "BREX data module code", true),
        new("code", "//dmCode|//avee|//pmCode|//pmc|//commentCode|//ccode|//ddnCode|//ddnc|//dmlCode|//dmlc", "CSDB object code", false),
        new("commentCode", "//commentCode|//ccode", "Comment code", false),
        new("commentPriority", "//commentPriority/@commentPriorityCode|//priority/@cprio", "Priority code of a comment", true),
        new("commentResponse", "//commentResponse/@responseType|//response/@rsptype", "Response type of a comment", true),
        new("commentTitle", "//commentTitle|//ctitle", "Title of a comment", true),
        new("commentType", "//@commentType|//ctype/@type", "Type of a comment", true),
        new("countryIsoCode", "//language/@countryIsoCode|//language/@country", "Country ISO code (CA, US, GB...)", true),
        new("ddnCode", "//ddnCode|//ddnc", "Data dispatch note code", false),
        new("disassyCode", "//@disassyCode|//discode", "Disassembly code", true),
        new("disassyCodeVariant", "//@disassyCodeVariant|//discodev", "Disassembly code variant", true),
        new("dmCode", "//dmCode|//avee", "Data module code", false),
        new("dmlCode", "//dmlCode|//dmlc", "Data management list code", false),
        new("firstVerificationType", "//firstVerification/@verificationType|//firstver/@type", "First verification type", true),
        new("icnTitle", "//imfAddressItems/icnTitle", "Title of an IMF", true),
        new("infoCode", "//@infoCode|//incode", "Information code", true),
        new("infoCodeVariant", "//@infoCodeVariant|//incodev", "Information code variant", true),
        new("infoName", "//infoName|//infoname", "Information name of a data module", true),
        new("infoNameVariant", "//infoNameVariant", "Information name variant of a data module", true),
        new("inWork", "//issueInfo/@inWork|//issno", "Inwork issue number (NN)", true),
        new("issueDate", "//issueDate|//issdate", "Issue date of the CSDB object", true),
        new("issueInfo", "//issueInfo|//issno", "Issue info (NNN-NN)", false),
        new("issueNumber", "//issueInfo/@issueNumber|//issno/@issno", "Issue number (NNN)", true),
        new("issueType", "//dmStatus/@issueType|//pmStatus/@issueType|//issno/@type", "Issue type (new, changed, deleted...)", true),
        new("itemLocationCode", "//@itemLocationCode|//itemloc", "Item location code", true),
        new("language", "//language", "Language and country ISO codes (en-CA, ...)", false),
        new("languageIsoCode", "//language/@languageIsoCode|//language/@language", "Language ISO code (en, fr, es...)", true),
        new("learnCode", "//@learnCode", "Learn code", true),
        new("learnEventCode", "//@learnEventCode", "Learn event code", true),
        new("modelIdentCode", "//@modelIdentCode|//modelic", "Model identification code", true),
        new("originator", "//originator/enterpriseName|//orig/@origname", "Name of the originator", true),
        new("originatorCode", "//originator/@enterpriseCode", "NCAGE code of the originator", true),
        new("pmCode", "//pmCode|//pmc", "Publication module code", false),
        new("pmIssuer", "//@pmIssuer|//pmissuer", "Issuing authority of the PM", true),
        new("pmNumber", "//@pmNumber|//pmnumber", "PM number", true),
        new("pmTitle", "//pmTitle|//pmtitle", "Title of a publication module", true),
        new("pmVolume", "//@pmVolume|//pmvolume", "Volume of the PM", true),
        new("reasonForUpdate", "//reasonForUpdate|//rfu", "Reason for update", true),
        new("receiverIdent", "//@receiverIdent|//recvid", "Receiving authority", true),
        new("remarks", "//dmStatus/remarks|//status/remarks|//pmStatus/remarks|//pmstatus/remarks|//commentStatus/remarks|//dmlStatus/remarks|//ddnStatus/remarks|//scormContentPackageStatus/remarks|//imfStatus/remarks", "General remarks", true),
        new("responsiblePartnerCompany", "//responsiblePartnerCompany/enterpriseName|//rpc/@rpcname", "Name of the RPC", true),
        new("responsiblePartnerCompanyCode", "//responsiblePartnerCompany/@enterpriseCode", "NCAGE code of the RPC", true),
        new("schema", "/*", "S1000D schema name", false),
        new("schemaUrl", "/*", "XML schema URL", true),
        new("secondVerificationType", "//secondVerification/@verificationType|//secver/@type", "Second verification type", true),
        new("securityClassification", "//security/@securityClassification|//security/@class", "Security classification (01, 02...)", true),
        new("senderIdent", "//@senderIdent|//sendid", "Issuing authority", true),
        new("seqNumber", "//@seqNumber|//seqnum", "Sequence number", true),
        new("shortPmTitle", "//shortPmTitle", "Short title of a publication module", true),
        new("skillLevelCode", "//dmStatus/skillLevel/@skillLevelCode|//status/skill/@skill", "Skill level code of the data module", true),
        new("subSubSystemCode", "//@subSubSystemCode|//subsect", "Subsubsystem code", true),
        new("subSystemCode", "//@subSystemCode|//section", "Subsystem code", true),
        new("systemCode", "//@systemCode|//chapnum", "System code", true),
        new("systemDiffCode", "//@systemDiffCode|//sdc", "System difference code", true),
        new("techName", "//techName|//techname", "Technical name of a data module", true),
        new("title", "//dmTitle|//dmtitle|//pmTitle|//pmtitle|//commentTitle|//ctitle|//icnTitle", "Title of a CSDB object", false),
        new("type", "/*", "Name of the root element of the document", false),
        new("yearOfDataIssue", "//@yearOfDataIssue|//diyear", "Year of data issue", true),
    };

    private static MetadataKey? Find(string name) =>
        Keys.FirstOrDefault(k => k.Name == name);

    /// <summary>Whether a metadata key is known.</summary>
    public static bool IsKnown(string name) => Find(name) != null;

    /// <summary>
    /// Retrieve a metadata value from a document, or null if not present.
    /// Mirrors <c>s1kdDocGetMetadata</c> / the tool's <c>show_metadata</c>.
    /// </summary>
    public static string? Get(XmlDocument doc, string key)
    {
        var entry = Find(key);
        if (entry == null)
        {
            throw new ArgumentException($"Unknown metadata key: {key}", nameof(key));
        }

        switch (key)
        {
            case "type":
                return doc.DocumentElement?.Name;
            case "issueInfo":
                return GetIssueInfo(doc);
            case "issueNumber":
                return GetIssueNumber(doc);
            case "inWork":
                return GetInWork(doc);
            case "language":
                return GetLanguage(doc);
            case "title":
                return GetTitle(doc);
            case "schema":
                return GetSchema(doc);
        }

        XmlNode? node = doc.SelectSingleNode(entry.XPath);
        return NodeValue(node);
    }

    /// <summary>
    /// Set a metadata value on a document. Returns true if applied. Mirrors
    /// <c>s1kdDocSetMetadata</c> / the tool's <c>edit_metadata</c>.
    /// </summary>
    public static bool Set(XmlDocument doc, string key, string value)
    {
        var entry = Find(key);
        if (entry == null)
        {
            throw new ArgumentException($"Unknown metadata key: {key}", nameof(key));
        }
        if (!entry.Editable)
        {
            return false;
        }

        switch (key)
        {
            case "issueNumber":
                return SetIssueAttr(doc, "issueNumber", "issno", value);
            case "inWork":
                return SetIssueAttr(doc, "inWork", "inwork", value);
        }

        XmlNode? node = doc.SelectSingleNode(entry.XPath);
        if (node == null)
        {
            return false;
        }

        if (node is XmlAttribute attr)
        {
            attr.Value = value;
        }
        else
        {
            node.InnerText = value;
        }
        return true;
    }

    // ----- composite getters -----

    private static string? NodeValue(XmlNode? node) => node switch
    {
        null => null,
        XmlAttribute a => a.Value,
        _ => node.InnerText,
    };

    private static string? GetIssueInfo(XmlDocument doc)
    {
        XmlNode? node = doc.SelectSingleNode("//issueInfo|//issno");
        if (node is not XmlElement el)
        {
            return null;
        }
        string? num = el.GetAttribute("issueNumber") is { Length: > 0 } n ? n :
                      el.GetAttribute("issno") is { Length: > 0 } n2 ? n2 : null;
        string work = el.GetAttribute("inWork") is { Length: > 0 } w ? w :
                      el.GetAttribute("inwork") is { Length: > 0 } w2 ? w2 : "00";
        return num == null ? null : $"{num}-{work}";
    }

    private static string? GetIssueNumber(XmlDocument doc)
    {
        if (doc.SelectSingleNode("//issueInfo|//issno") is not XmlElement el)
        {
            return null;
        }
        if (el.HasAttribute("issueNumber"))
        {
            return el.GetAttribute("issueNumber");
        }
        return el.HasAttribute("issno") ? el.GetAttribute("issno") : null;
    }

    private static string? GetInWork(XmlDocument doc)
    {
        if (doc.SelectSingleNode("//issueInfo|//issno") is not XmlElement el)
        {
            return null;
        }
        if (el.HasAttribute("inWork"))
        {
            return el.GetAttribute("inWork");
        }
        if (el.HasAttribute("inwork"))
        {
            return el.GetAttribute("inwork");
        }
        return "00";
    }

    private static string? GetLanguage(XmlDocument doc)
    {
        if (doc.SelectSingleNode("//language") is not XmlElement el)
        {
            return null;
        }
        string lang = el.GetAttribute("languageIsoCode") is { Length: > 0 } l ? l : el.GetAttribute("language");
        string country = el.GetAttribute("countryIsoCode") is { Length: > 0 } c ? c : el.GetAttribute("country");
        if (string.IsNullOrEmpty(lang) && string.IsNullOrEmpty(country))
        {
            return null;
        }
        return $"{lang}-{country}";
    }

    private static string? GetTitle(XmlDocument doc)
    {
        XmlNode? node = doc.SelectSingleNode(
            "//dmTitle|//dmtitle|//pmTitle|//pmtitle|//commentTitle|//ctitle|//icnTitle");
        return node?.InnerText;
    }

    private static string? GetSchema(XmlDocument doc)
    {
        XmlElement? root = doc.DocumentElement;
        // xsi:noNamespaceSchemaLocation, if present.
        string? loc = root?.GetAttribute("noNamespaceSchemaLocation",
            "http://www.w3.org/2001/XMLSchema-instance");
        return string.IsNullOrEmpty(loc) ? null : loc;
    }

    private static bool SetIssueAttr(XmlDocument doc, string modern, string legacy, string value)
    {
        if (doc.SelectSingleNode("//issueInfo|//issno") is not XmlElement el)
        {
            return false;
        }
        if (el.HasAttribute(legacy) && !el.HasAttribute(modern))
        {
            el.SetAttribute(legacy, value);
        }
        else
        {
            el.SetAttribute(modern, value);
        }
        return true;
    }
}
