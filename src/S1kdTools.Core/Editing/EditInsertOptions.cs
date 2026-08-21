namespace S1kdTools.Editing;

/// <summary>
/// Fills in each block's insert menus after the projection has run.
///
/// A block's path already says where it sits — the step before the last one names
/// the element it is a child of — so the menu for "insert beside this" can be
/// worked out from the model alone, without the stylesheet knowing the editor's
/// vocabulary or the front-end knowing S1000D's. The vocabulary itself comes from
/// the profile's <see cref="EditTemplateCatalogue"/>.
/// </summary>
public static class EditInsertOptions
{
    /// <summary>Attach the insert menus to every block in <paramref name="document"/>.</summary>
    public static EditDocument Decorate(EditDocument document, EditProfile? profile = null)
    {
        EditTemplateCatalogue templates = (profile ?? EditProfile.Default).Templates;

        foreach (EditBlock block in document.AllBlocks())
        {
            // A metadata field is a fixed part of the address; there is nothing to
            // add beside it, and offering to would suggest otherwise.
            if (block.Kind == "metaField")
            {
                continue;
            }

            string parent = ParentElement(block.Path);

            block.InsertSiblings = parent.Length == 0
                ? []
                : templates.SiblingOptions(parent);

            block.InsertChildren = block.Editable == EditMode.None
                ? templates.ChildOptions(block.Element)
                : [];
        }

        return document;
    }

    /// <summary>
    /// The element name of the next-to-last step of a block path — the element the
    /// block's own element is a child of. Empty for a path with only one step,
    /// which is the root and has no parent to insert into.
    /// </summary>
    public static string ParentElement(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        string[] steps = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (steps.Length < 2)
        {
            return "";
        }

        return StripPredicate(steps[^2]);
    }

    /// <summary>The element name of the last step of a block path.</summary>
    public static string LeafElement(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        string[] steps = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return steps.Length == 0 ? "" : StripPredicate(steps[^1]);
    }

    private static string StripPredicate(string step)
    {
        int bracket = step.IndexOf('[');
        return bracket < 0 ? step : step[..bracket];
    }
}
