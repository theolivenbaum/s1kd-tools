using System.Xml;

namespace S1kdTools.Editing;

/// <summary>
/// What a newly inserted element is made of, and what may be inserted where.
///
/// S1000D's content models are in the schemas, and a schema-aware editor would
/// read them. This does not: it carries a small table of the elements a technical
/// author reaches for and the minimum well-formed shape of each, because that is
/// what an editor has to offer *before* the document is valid — a
/// <c>&lt;warning/&gt;</c> with no <c>warningAndCautionPara</c> is invalid the
/// moment it is created, so the empty paragraph has to come with it.
///
/// The table is the editor's opinion, not the standard's. Validation stays where
/// it belongs: <c>s1kd validate</c> against the schema and <c>s1kd brexcheck</c>
/// against the project's business rules, run on the document after the edit, and
/// reported rather than prevented. An editor that refuses an edit because the
/// result is momentarily invalid is an editor authors work around.
/// </summary>
public static class EditTemplates
{
    /// <summary>An element the author can insert, as the editor's menu shows it.</summary>
    /// <param name="Element">The element name.</param>
    /// <param name="Label">What the menu calls it.</param>
    /// <param name="Kind">The block kind it will project as, for the menu's icon.</param>
    public readonly record struct InsertOption(string Element, string Label, string Kind);

    private static readonly InsertOption Para = new("para", "Paragraph", "para");
    private static readonly InsertOption Step = new("proceduralStep", "Step", "step");
    private static readonly InsertOption Warning = new("warning", "Warning", "warning");
    private static readonly InsertOption Caution = new("caution", "Caution", "caution");
    private static readonly InsertOption Note = new("note", "Note", "note");
    private static readonly InsertOption Title = new("title", "Title", "title");
    private static readonly InsertOption Figure = new("figure", "Figure", "figure");
    private static readonly InsertOption Table = new("table", "Table", "table");
    private static readonly InsertOption RandomList = new("randomList", "Bulleted list", "list");
    private static readonly InsertOption SequentialList = new("sequentialList", "Numbered list", "list");
    private static readonly InsertOption ListItem = new("listItem", "List item", "listItem");
    private static readonly InsertOption LevelledPara = new("levelledPara", "Section", "section");
    private static readonly InsertOption Condition = new("reqCondNoRef", "Condition", "requirement");
    private static readonly InsertOption SupportEquip = new("supportEquipDescr", "Support equipment", "requirement");
    private static readonly InsertOption Supply = new("supplyDescr", "Consumable", "requirement");
    private static readonly InsertOption Spare = new("spareDescr", "Spare", "requirement");
    private static readonly InsertOption Row = new("row", "Table row", "row");

    /// <summary>
    /// What may be inserted as a sibling of an element inside
    /// <paramref name="parentElement"/>.
    ///
    /// Keyed on the parent rather than on the element itself, because "what can go
    /// here" is a question about the place, not about what happens to be in it —
    /// the answer beside a paragraph in a step is the answer beside a warning in
    /// that step.
    /// </summary>
    public static IReadOnlyList<InsertOption> SiblingOptions(string parentElement) =>
        parentElement switch
        {
            "proceduralStep" or "isolationStep" or "crewDrillStep" =>
                [Para, Step, Warning, Caution, Note, Figure, Table, RandomList, SequentialList],
            "mainProcedure" or "correctiveProcedure" or "isolationMainProcedure" =>
                [Step],
            "commonInfo" or "description" or "levelledPara" =>
                [Para, LevelledPara, Warning, Caution, Note, Figure, Table, RandomList, SequentialList],
            "safetyRqmts" =>
                [Warning, Caution, Note],
            "reqCondGroup" or "closeRqmts" =>
                [Condition],
            "supportEquipDescrGroup" => [SupportEquip],
            "supplyDescrGroup" => [Supply],
            "spareDescrGroup" => [Spare],
            "randomList" or "sequentialList" => [ListItem],
            "listItem" => [Para, RandomList, SequentialList, Note, Figure],
            "tbody" or "thead" or "tfoot" => [Row],
            "entry" => [Para],
            "figure" => [Title],
            _ => [Para],
        };

    /// <summary>
    /// What may be inserted as the first content of <paramref name="element"/>,
    /// for a container the author has just created or emptied.
    /// </summary>
    public static IReadOnlyList<InsertOption> ChildOptions(string element) =>
        element switch
        {
            "proceduralStep" or "isolationStep" or "crewDrillStep" =>
                [Para, Step, Warning, Caution, Note, Figure, Table, Title],
            "mainProcedure" or "correctiveProcedure" => [Step],
            "randomList" or "sequentialList" => [ListItem],
            "listItem" => [Para],
            "description" => [LevelledPara, Para],
            "levelledPara" => [Para, LevelledPara],
            "reqCondGroup" or "closeRqmts" => [Condition],
            "safetyRqmts" => [Warning, Caution],
            _ => [Para],
        };

    /// <summary>
    /// Build a new element, complete enough to be projected and edited: every
    /// container comes with the child its schema requires, so an inserted warning
    /// has a paragraph to type into rather than being an empty box the author has
    /// to fill through a second menu.
    /// </summary>
    public static XmlElement Create(XmlDocument doc, string element, string text = "")
    {
        if (string.IsNullOrWhiteSpace(element))
        {
            throw new EditCommandException("The insert command names no element.");
        }

        return element switch
        {
            "para" or "simplePara" or "notePara" or "warningAndCautionPara" or "title" or
            "reqCond" or "name" or "shortName" or "reqQuantity" or "descrForPart" =>
                Text(doc, element, text),

            "warning" or "caution" =>
                Wrap(doc, element, Text(doc, "warningAndCautionPara", text)),

            "note" =>
                Wrap(doc, "note", Text(doc, "notePara", text)),

            "attention" =>
                Wrap(doc, "attention", Text(doc, "attentionListItemPara", text)),

            "proceduralStep" or "isolationStep" or "crewDrillStep" =>
                Wrap(doc, element, Text(doc, "para", text)),

            "levelledPara" =>
                Wrap(doc, "levelledPara", Text(doc, "title", text), Text(doc, "para", "")),

            "randomList" or "sequentialList" =>
                Wrap(doc, element, Create(doc, "listItem", text)),

            "listItem" =>
                Wrap(doc, "listItem", Text(doc, "para", text)),

            "definitionList" =>
                Wrap(doc, "definitionList", Create(doc, "definitionListItem", text)),

            "definitionListItem" =>
                Wrap(doc, "definitionListItem",
                    Text(doc, "listItemTerm", text), Text(doc, "listItemDefinition", "")),

            "figure" =>
                Wrap(doc, "figure", Text(doc, "title", text), doc.CreateElement("graphic")),

            "table" => CreateTable(doc, text),
            "row" => CreateRow(doc, 2),

            "reqCondNoRef" =>
                Wrap(doc, "reqCondNoRef", Text(doc, "reqCond", text)),

            "supportEquipDescr" or "supplyDescr" or "spareDescr" =>
                CreateRequirement(doc, element, text),

            _ => Text(doc, element, text),
        };
    }

    private static XmlElement Text(XmlDocument doc, string name, string text)
    {
        XmlElement element = doc.CreateElement(name);
        if (text.Length > 0)
        {
            element.AppendChild(doc.CreateTextNode(text));
        }
        return element;
    }

    private static XmlElement Wrap(XmlDocument doc, string name, params XmlNode[] children)
    {
        XmlElement element = doc.CreateElement(name);
        foreach (XmlNode child in children)
        {
            element.AppendChild(child);
        }
        return element;
    }

    private static XmlElement CreateTable(XmlDocument doc, string text)
    {
        XmlElement table = Wrap(doc, "table", Text(doc, "title", text));

        XmlElement tgroup = doc.CreateElement("tgroup");
        tgroup.SetAttribute("cols", "2");

        for (int i = 1; i <= 2; i++)
        {
            XmlElement colspec = doc.CreateElement("colspec");
            colspec.SetAttribute("colname", "col" + i);
            tgroup.AppendChild(colspec);
        }

        XmlElement thead = doc.CreateElement("thead");
        thead.AppendChild(CreateRow(doc, 2));
        tgroup.AppendChild(thead);

        XmlElement tbody = doc.CreateElement("tbody");
        tbody.AppendChild(CreateRow(doc, 2));
        tgroup.AppendChild(tbody);

        table.AppendChild(tgroup);
        return table;
    }

    private static XmlElement CreateRow(XmlDocument doc, int columns)
    {
        XmlElement row = doc.CreateElement("row");
        for (int i = 0; i < columns; i++)
        {
            row.AppendChild(doc.CreateElement("entry"));
        }
        return row;
    }

    private static XmlElement CreateRequirement(XmlDocument doc, string element, string text)
    {
        XmlElement partNumber = doc.CreateElement("partNumber");
        XmlElement partAndSerial = Wrap(doc, "partAndSerialNumber", partNumber);
        XmlElement identNumber = Wrap(doc, "identNumber",
            doc.CreateElement("manufacturerCode"), partAndSerial);

        return Wrap(doc, element,
            Text(doc, "name", text),
            identNumber,
            Text(doc, "reqQuantity", "1"));
    }
}
