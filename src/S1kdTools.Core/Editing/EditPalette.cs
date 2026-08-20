using System.Xml;

namespace S1kdTools.Editing;

/// <summary>
/// The catalogue of things an author can add to a data module, each one projected
/// so a front-end can show what it will look like rather than only what it is
/// called.
///
/// <b>The preview is a real projection, not a picture of one.</b> Every entry is
/// built by <see cref="EditTemplates.Create"/> — the same call an insert command
/// makes, with the same empty content — dropped into the container the editor would
/// put it in, and run through the editing stylesheet. So a palette card is drawn by
/// the same code as the surface, from the same blocks, and shows exactly what
/// dropping it produces: change the stylesheet and both change together.
///
/// It also means the catalogue is not a second list to maintain. What may be
/// inserted is <see cref="EditTemplates.SiblingOptions"/>, which is what the
/// gutter menu already offers; this walks the containers it knows and collects the
/// distinct elements, so an element added there appears here without being
/// mentioned twice.
/// </summary>
public static class EditPalette
{
    /// <summary>
    /// The containers the catalogue is derived from, and the scaffold each one
    /// needs to be projected in.
    ///
    /// A block's presentation depends on where it is — a step's number comes from
    /// its depth, a warning inside safety requirements is headed differently from
    /// one in a step — so a preview built in the wrong place would be a picture of
    /// a block that does not exist. Each scaffold is therefore the real ancestry,
    /// with <c>{0}</c> where the new element goes.
    ///
    /// Order matters: an element offered by several containers is previewed in the
    /// first one here, so the list runs from the place an author most often adds
    /// something outwards.
    /// </summary>
    private static readonly (string Parent, string Scaffold)[] Contexts =
    [
        ("proceduralStep",
            "<content><procedure><mainProcedure><proceduralStep><para/>{0}</proceduralStep></mainProcedure></procedure></content>"),
        ("mainProcedure",
            "<content><procedure><mainProcedure>{0}</mainProcedure></procedure></content>"),
        ("description",
            "<content><description>{0}</description></content>"),
        ("safetyRqmts",
            "<content><procedure><preliminaryRqmts><reqSafety><safetyRqmts>{0}</safetyRqmts></reqSafety></preliminaryRqmts></procedure></content>"),
        ("reqCondGroup",
            "<content><procedure><preliminaryRqmts><reqCondGroup>{0}</reqCondGroup></preliminaryRqmts></procedure></content>"),
        ("supportEquipDescrGroup",
            "<content><procedure><preliminaryRqmts><reqSupportEquips><supportEquipDescrGroup>{0}</supportEquipDescrGroup></reqSupportEquips></preliminaryRqmts></procedure></content>"),
        ("supplyDescrGroup",
            "<content><procedure><preliminaryRqmts><reqSupplies><supplyDescrGroup>{0}</supplyDescrGroup></reqSupplies></preliminaryRqmts></procedure></content>"),
        ("spareDescrGroup",
            "<content><procedure><preliminaryRqmts><reqSpares><spareDescrGroup>{0}</spareDescrGroup></reqSpares></preliminaryRqmts></procedure></content>"),
        ("randomList",
            "<content><description><randomList>{0}</randomList></description></content>"),
        ("figure",
            "<content><description><figure>{0}<graphic/></figure></description></content>"),
        ("tbody",
            "<content><description><table><tgroup cols=\"2\"><tbody>{0}</tbody></tgroup></table></description></content>"),
    ];

    /// <summary>What each element is for, in one line, for the card under its name.</summary>
    private static readonly Dictionary<string, string> Summary = new(StringComparer.Ordinal)
    {
        ["para"] = "A paragraph of prose.",
        ["proceduralStep"] = "A numbered step. Steps nest, and the numbering follows.",
        ["warning"] = "A hazard to a person. Boxed, and printed before the step it applies to.",
        ["caution"] = "A hazard to the equipment. Boxed, like a warning.",
        ["note"] = "Information that is neither an instruction nor a hazard.",
        ["attention"] = "A notice the reader must attend to.",
        ["title"] = "The heading of the step or section it is added to.",
        ["levelledPara"] = "A numbered section with a title, which can hold further sections.",
        ["figure"] = "An illustration with a title and an ICN.",
        ["table"] = "A table with a title, a head row and a body row.",
        ["randomList"] = "A bulleted list.",
        ["sequentialList"] = "A numbered list.",
        ["listItem"] = "One more item in the list.",
        ["reqCondNoRef"] = "A condition that must hold before the task starts.",
        ["supportEquipDescr"] = "A tool or a piece of ground equipment the task needs.",
        ["supplyDescr"] = "A consumable, material or expendable the task needs.",
        ["spareDescr"] = "A spare part the task needs.",
        ["row"] = "One more row of the table.",
    };

    /// <summary>
    /// Build the catalogue. Not cached: it costs a transform per entry and is asked
    /// for once when an editor opens, which is a price worth paying to keep it
    /// honest about a stylesheet that may have been edited since the server started.
    /// </summary>
    /// <param name="stylesheet">
    /// The editing stylesheet to project with, relative to <c>Resources/</c>.
    /// Defaults to <see cref="EditProjection.DefaultStylesheet"/>.
    /// </param>
    public static IReadOnlyList<PaletteEntry> Build(string? stylesheet = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<PaletteEntry>();

        foreach ((string parent, string scaffold) in Contexts)
        {
            foreach (EditTemplates.InsertOption option in EditTemplates.SiblingOptions(parent))
            {
                if (!seen.Add(option.Element))
                {
                    continue;
                }

                EditBlock? preview = Project(option.Element, scaffold, stylesheet);
                if (preview == null)
                {
                    // The stylesheet declined to project it — which is a real answer:
                    // an element the editor cannot show is one it should not offer.
                    continue;
                }

                entries.Add(new PaletteEntry(
                    option.Element,
                    option.Label,
                    option.Kind,
                    Summary.TryGetValue(option.Element, out string? summary) ? summary : "",
                    parent,
                    preview));
            }
        }

        return entries;
    }

    /// <summary>
    /// Create one element and project it in its scaffold, returning the block it
    /// came out as.
    /// </summary>
    private static EditBlock? Project(string element, string scaffold, string? stylesheet)
    {
        XmlDocument doc = XmlUtils.ReadMem(
            "<dmodule><identAndStatusSection><dmAddress><dmAddressItems><dmTitle>" +
            "<techName>Component</techName><infoName>Preview</infoName>" +
            "</dmTitle></dmAddressItems></dmAddress></identAndStatusSection>" +
            string.Format(scaffold, "<s1kdPaletteSlot/>") +
            "</dmodule>");

        XmlNode slot = doc.SelectSingleNode("//s1kdPaletteSlot")
            ?? throw new InvalidOperationException($"The scaffold for '{element}' has no slot.");

        // Created empty, which is how an insert command creates it.
        //
        // Filling the preview with a plausible sentence would make a better-looking
        // card and a dishonest one: the author would drop a warning expecting the
        // words on the card and get an empty box — or, worse, get the words and
        // publish them. What a preview has to show is the *shape*, and the shape
        // survives being empty: the box and its WARNING heading, a step's number, a
        // parts row's four labelled columns. The placeholders the projection carries
        // fill in the rest.
        XmlElement created = EditTemplates.Create(doc, element, "");

        slot.ParentNode!.ReplaceChild(created, slot);

        EditDocument model = EditInsertOptions.Decorate(EditProjection.Project(doc, stylesheet));
        string path = XmlUtils.XPathOf(created);

        return model.Find(path);
    }
}

/// <summary>
/// One thing the palette offers: what it is called, what it is for, and the block
/// it projects as.
/// </summary>
/// <param name="Element">The element name to send in an insert command.</param>
/// <param name="Label">What the palette calls it.</param>
/// <param name="Kind">The block kind it projects as, for the card's icon.</param>
/// <param name="Summary">One line on what it is for.</param>
/// <param name="PreviewedIn">
/// The container the preview was built in. Not decoration: it is why a step's
/// preview is numbered the way it is, and a front-end showing the preview out of
/// that context would be showing something that does not happen.
/// </param>
/// <param name="Preview">The block, exactly as the surface would draw it.</param>
public sealed record PaletteEntry(
    string Element,
    string Label,
    string Kind,
    string Summary,
    string PreviewedIn,
    EditBlock Preview);
