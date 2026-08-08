namespace S1kdTools.Presentation;

/// <summary>
/// The CSDB object types this library can present: every S1000D data module
/// schema plus the non-data-module objects (publication module, data management
/// list, data dispatch note, comment, ICN metadata file, SCORM content package
/// and data update file).
/// </summary>
/// <remarks>
/// The names follow the S1000D schema names used by <c>s1kd-newdm -@</c> and the
/// <c>dmtypes</c> catalogue, so a value maps one-to-one onto a schema
/// (<c>proced.xsd</c>, <c>ipd.xsd</c>, …) and onto a presentation stylesheet.
/// </remarks>
public enum CsdbObjectType
{
    /// <summary>Descriptive data module (<c>descript.xsd</c>).</summary>
    Description,

    /// <summary>Procedural data module (<c>proced.xsd</c>).</summary>
    Procedure,

    /// <summary>Fault isolation data module (<c>fault.xsd</c>).</summary>
    FaultIsolation,

    /// <summary>Illustrated parts data module (<c>ipd.xsd</c>).</summary>
    IllustratedPartsCatalog,

    /// <summary>Crew/operator information data module (<c>crew.xsd</c>).</summary>
    Crew,

    /// <summary>Maintenance planning data module (<c>schedul.xsd</c>).</summary>
    MaintenancePlanning,

    /// <summary>Checklist data module (<c>checklist.xsd</c>).</summary>
    Checklist,

    /// <summary>Business rules exchange data module (<c>brex.xsd</c>).</summary>
    Brex,

    /// <summary>Business rules data module (<c>brdoc.xsd</c>).</summary>
    BusinessRulesDocument,

    /// <summary>Front matter data module (<c>frontmatter.xsd</c>).</summary>
    FrontMatter,

    /// <summary>Service bulletin data module (<c>sb.xsd</c>).</summary>
    ServiceBulletin,

    /// <summary>Technical repository data module (<c>techrep.xsd</c>).</summary>
    TechnicalRepository,

    /// <summary>Wiring data data module (<c>wrngdata.xsd</c>).</summary>
    WiringData,

    /// <summary>Wiring fields data module (<c>wrngflds.xsd</c>).</summary>
    WiringFields,

    /// <summary>Process data module (<c>process.xsd</c>).</summary>
    Process,

    /// <summary>Container data module (<c>container.xsd</c>).</summary>
    Container,

    /// <summary>Learning data module (<c>learning.xsd</c>).</summary>
    Learning,

    /// <summary>SCO content data module (<c>scocontent.xsd</c>).</summary>
    ScoContent,

    /// <summary>Applicability cross-reference table (<c>appliccrossreftable.xsd</c>).</summary>
    ApplicabilityCrossRefTable,

    /// <summary>Product cross-reference table (<c>prdcrossreftable.xsd</c>).</summary>
    ProductCrossRefTable,

    /// <summary>Conditions cross-reference table (<c>condcrossreftable.xsd</c>).</summary>
    ConditionCrossRefTable,

    /// <summary>Common information repository data module (<c>comrep.xsd</c>).</summary>
    CommonRepository,

    /// <summary>Publication module (<c>pm.xsd</c>).</summary>
    PublicationModule,

    /// <summary>Data management list (<c>dml.xsd</c>).</summary>
    DataManagementList,

    /// <summary>Data dispatch note (<c>ddn.xsd</c>).</summary>
    DataDispatchNote,

    /// <summary>Comment (<c>comment.xsd</c>).</summary>
    Comment,

    /// <summary>ICN metadata file (<c>icnmetadata.xsd</c>).</summary>
    IcnMetadata,

    /// <summary>SCORM content package (<c>scormcontentpackage.xsd</c>).</summary>
    ScormContentPackage,

    /// <summary>Data update file (<c>update.xsd</c>).</summary>
    DataUpdateFile,
}

/// <summary>
/// What the library knows about a <see cref="CsdbObjectType"/>: the S1000D schema
/// it comes from, the element that identifies it, the stylesheet that presents it
/// and the publication title printed in the page header.
/// </summary>
/// <param name="Type">The object type described.</param>
/// <param name="Schema">The S1000D schema name, without the <c>.xsd</c> suffix.</param>
/// <param name="RootElement">The document element of the object (e.g. <c>dmodule</c>).</param>
/// <param name="ContentElement">
/// The element that distinguishes this type from its siblings — the single child of
/// <c>content</c> for a data module (e.g. <c>procedure</c>), or the document element
/// itself for the objects that have no such child.
/// </param>
/// <param name="StylesheetName">File name of the embedded presentation stylesheet.</param>
/// <param name="PublicationTitle">Default publication title for the page header.</param>
public readonly record struct CsdbObjectTypeInfo(
    CsdbObjectType Type,
    string Schema,
    string RootElement,
    string ContentElement,
    string StylesheetName,
    string PublicationTitle);

/// <summary>
/// The <see cref="CsdbObjectTypeInfo"/> catalogue, and lookups over it by type,
/// schema name or content element.
/// </summary>
public static class CsdbObjectTypes
{
    private static readonly CsdbObjectTypeInfo[] All =
    [
        new(CsdbObjectType.Description, "descript", "dmodule", "description",
            "descript.xsl", "AIRCRAFT MAINTENANCE MANUAL"),
        new(CsdbObjectType.Procedure, "proced", "dmodule", "procedure",
            "proced.xsl", "AIRCRAFT MAINTENANCE MANUAL"),
        new(CsdbObjectType.FaultIsolation, "fault", "dmodule", "faultIsolation",
            "fault.xsl", "TROUBLE SHOOTING MANUAL"),
        new(CsdbObjectType.IllustratedPartsCatalog, "ipd", "dmodule", "illustratedPartsCatalog",
            "ipd.xsl", "ILLUSTRATED PARTS CATALOGUE"),
        new(CsdbObjectType.Crew, "crew", "dmodule", "crew",
            "crew.xsl", "FLIGHT CREW OPERATING MANUAL"),
        new(CsdbObjectType.MaintenancePlanning, "schedul", "dmodule", "maintPlanning",
            "schedul.xsl", "MAINTENANCE PLANNING DOCUMENT"),
        new(CsdbObjectType.Checklist, "checklist", "dmodule", "checkList",
            "checklist.xsl", "MAINTENANCE CHECK LIST"),
        new(CsdbObjectType.Brex, "brex", "dmodule", "brex",
            "brex.xsl", "BUSINESS RULES EXCHANGE"),
        new(CsdbObjectType.BusinessRulesDocument, "brdoc", "dmodule", "brDoc",
            "brdoc.xsl", "BUSINESS RULES DOCUMENT"),
        new(CsdbObjectType.FrontMatter, "frontmatter", "dmodule", "frontMatter",
            "frontmatter.xsl", "AIRCRAFT MAINTENANCE MANUAL"),
        new(CsdbObjectType.ServiceBulletin, "sb", "dmodule", "sb",
            "sb.xsl", "SERVICE BULLETIN"),
        new(CsdbObjectType.TechnicalRepository, "techrep", "dmodule", "techRepository",
            "techrep.xsl", "TECHNICAL REPOSITORY"),
        new(CsdbObjectType.WiringData, "wrngdata", "dmodule", "wiringData",
            "wrngdata.xsl", "AIRCRAFT WIRING MANUAL"),
        new(CsdbObjectType.WiringFields, "wrngflds", "dmodule", "wiringFields",
            "wrngflds.xsl", "AIRCRAFT WIRING MANUAL"),
        new(CsdbObjectType.Process, "process", "dmodule", "process",
            "process.xsl", "INTERACTIVE MAINTENANCE PROCESS"),
        new(CsdbObjectType.Container, "container", "dmodule", "container",
            "container.xsl", "AIRCRAFT MAINTENANCE MANUAL"),
        new(CsdbObjectType.Learning, "learning", "dmodule", "learning",
            "learning.xsl", "TECHNICAL TRAINING MANUAL"),
        new(CsdbObjectType.ScoContent, "scocontent", "dmodule", "scoContent",
            "scocontent.xsl", "TECHNICAL TRAINING MANUAL"),
        new(CsdbObjectType.ApplicabilityCrossRefTable, "appliccrossreftable", "dmodule", "applicCrossRefTable",
            "appliccrossreftable.xsl", "APPLICABILITY CROSS-REFERENCE TABLE"),
        new(CsdbObjectType.ProductCrossRefTable, "prdcrossreftable", "dmodule", "productCrossRefTable",
            "prdcrossreftable.xsl", "PRODUCT CROSS-REFERENCE TABLE"),
        new(CsdbObjectType.ConditionCrossRefTable, "condcrossreftable", "dmodule", "condCrossRefTable",
            "condcrossreftable.xsl", "CONDITIONS CROSS-REFERENCE TABLE"),
        new(CsdbObjectType.CommonRepository, "comrep", "dmodule", "commonRepository",
            "comrep.xsl", "COMMON INFORMATION REPOSITORY"),
        new(CsdbObjectType.PublicationModule, "pm", "pm", "pm",
            "pm.xsl", "PUBLICATION MODULE"),
        new(CsdbObjectType.DataManagementList, "dml", "dml", "dml",
            "dml.xsl", "DATA MANAGEMENT LIST"),
        new(CsdbObjectType.DataDispatchNote, "ddn", "ddn", "ddn",
            "ddn.xsl", "DATA DISPATCH NOTE"),
        new(CsdbObjectType.Comment, "comment", "comment", "comment",
            "comment.xsl", "COMMENT"),
        new(CsdbObjectType.IcnMetadata, "icnmetadata", "icnMetadataFile", "icnMetadataFile",
            "icnmetadata.xsl", "ICN METADATA FILE"),
        new(CsdbObjectType.ScormContentPackage, "scormcontentpackage", "scormContentPackage", "scormContentPackage",
            "scormcontentpackage.xsl", "SCORM CONTENT PACKAGE"),
        new(CsdbObjectType.DataUpdateFile, "update", "dataUpdateFile", "dataUpdateFile",
            "update.xsl", "DATA UPDATE FILE"),
    ];

    /// <summary>Every known object type, in schema order.</summary>
    public static IReadOnlyList<CsdbObjectTypeInfo> Catalogue => All;

    /// <summary>Look up the catalogue entry for <paramref name="type"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The type is not in the catalogue.</exception>
    public static CsdbObjectTypeInfo Info(CsdbObjectType type)
    {
        foreach (CsdbObjectTypeInfo info in All)
        {
            if (info.Type == type)
            {
                return info;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown CSDB object type.");
    }

    /// <summary>
    /// Find the entry whose <see cref="CsdbObjectTypeInfo.Schema"/> matches
    /// <paramref name="schema"/> (case-insensitive, with or without a
    /// <c>.xsd</c> suffix or a leading path).
    /// </summary>
    public static bool TryFromSchema(string? schema, out CsdbObjectTypeInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(schema))
        {
            return false;
        }

        // "http://www.s1000d.org/S1000D_6/xml_schema_flat/proced.xsd" -> "proced"
        string name = schema.Trim();
        int slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }
        if (name.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        foreach (CsdbObjectTypeInfo candidate in All)
        {
            if (string.Equals(candidate.Schema, name, StringComparison.OrdinalIgnoreCase))
            {
                info = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Find the entry whose <see cref="CsdbObjectTypeInfo.ContentElement"/> matches
    /// <paramref name="elementName"/> exactly (S1000D element names are case-sensitive).
    /// </summary>
    public static bool TryFromContentElement(string? elementName, out CsdbObjectTypeInfo info)
    {
        info = default;
        if (string.IsNullOrEmpty(elementName))
        {
            return false;
        }

        foreach (CsdbObjectTypeInfo candidate in All)
        {
            if (string.Equals(candidate.ContentElement, elementName, StringComparison.Ordinal))
            {
                info = candidate;
                return true;
            }
        }
        return false;
    }
}
