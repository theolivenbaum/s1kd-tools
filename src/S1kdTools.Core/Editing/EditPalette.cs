using System.Xml;

namespace S1kdTools.Editing;

/// <summary>
/// The catalogue of things an author can add to a data module, each one projected
/// so a front-end can show what it will look like rather than only what it is
/// called.
///
/// <b>The preview is a real projection, not a picture of one.</b> Every entry is
/// built by <see cref="EditTemplateCatalogue.Create"/> — the same call an insert
/// command makes, with the same empty content — dropped into the container the
/// editor would put it in, and run through the editing stylesheet. So a palette
/// card is drawn by the same code as the surface, from the same blocks, and shows
/// exactly what dropping it produces: change either half of the profile and both
/// change together.
///
/// It also means the palette is not a second list to maintain. What may be
/// inserted is <see cref="EditTemplateCatalogue.SiblingOptions"/>, which is what
/// the gutter menu already offers; this walks the catalogue's
/// <see cref="EditTemplateCatalogue.PaletteContexts"/> and collects the distinct
/// elements, so an element a house catalogue adds appears here without being
/// mentioned twice.
/// </summary>
public static class EditPalette
{
    /// <summary>
    /// Build the catalogue. Not cached: it costs a transform per entry and is asked
    /// for once when an editor opens, which is a price worth paying to keep it
    /// honest about a stylesheet that may have been edited since the server started.
    /// </summary>
    /// <param name="profile">
    /// Which dialect to build the palette for. <see cref="EditProfile.Default"/>
    /// when null.
    /// </param>
    public static IReadOnlyList<PaletteEntry> Build(EditProfile? profile = null)
    {
        profile ??= EditProfile.Default;
        EditTemplateCatalogue templates = profile.Templates;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<PaletteEntry>();

        foreach ((string parent, string scaffold) in templates.PaletteContexts)
        {
            foreach (EditTemplateCatalogue.InsertOption option in templates.SiblingOptions(parent))
            {
                if (!seen.Add(option.Element))
                {
                    continue;
                }

                EditBlock? preview = Project(option.Element, scaffold, profile);
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
                    templates.Summary(option.Element),
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
    private static EditBlock? Project(string element, string scaffold, EditProfile profile)
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
        XmlElement created = profile.Templates.Create(doc, element, "");

        slot.ParentNode!.ReplaceChild(created, slot);

        EditDocument model = EditInsertOptions.Decorate(EditProjection.Project(doc, profile), profile);
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
