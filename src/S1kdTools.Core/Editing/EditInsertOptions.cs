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
        HashSet<string> repeated = Repeated(document);

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
                : Siblings(templates, parent, block, repeated);

            block.InsertChildren = block.Editable == EditMode.None
                ? templates.ChildOptions(block.Element)
                : [];
        }

        return document;
    }

    /// <summary>
    /// What may go beside a block: what the catalogue says about its container,
    /// and — when the catalogue has never heard of that container — another of
    /// whatever is already there, but only where the object itself shows that more
    /// than one of them is allowed.
    ///
    /// The catalogue is a vocabulary and cannot know every container in S1000D, let
    /// alone in a project's own schema. It used to guess a paragraph for the ones it
    /// did not know, which was wrong far more often than it was right. A document
    /// holding two <c>entry</c>s in a <c>row</c> has said something a guess cannot:
    /// that a third is allowed. One holding a single <c>techName</c> in a
    /// <c>dmTitle</c> has said nothing, and nothing is what is offered.
    /// </summary>
    private static IReadOnlyList<EditTemplateCatalogue.InsertOption> Siblings(
        EditTemplateCatalogue templates, string parent, EditBlock block, HashSet<string> repeated)
    {
        IReadOnlyList<EditTemplateCatalogue.InsertOption> known = templates.SiblingOptions(parent);
        if (known.Count > 0)
        {
            return known;
        }

        return repeated.Contains(Sibship(block.Path))
            ? [templates.Another(block.Element, block.Kind)]
            : [];
    }

    /// <summary>
    /// The sibships — a parent path plus a child element name — that hold more than
    /// one block in this projection.
    /// </summary>
    private static HashSet<string> Repeated(EditDocument document)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var twice = new HashSet<string>(StringComparer.Ordinal);

        foreach (EditBlock block in document.AllBlocks())
        {
            string sibship = Sibship(block.Path);
            if (sibship.Length > 0 && !seen.Add(sibship))
            {
                twice.Add(sibship);
            }
        }

        return twice;
    }

    /// <summary>
    /// A path with the position dropped off its last step: everything with the same
    /// one is a sibling of everything else with it.
    /// </summary>
    private static string Sibship(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        int bracket = path.LastIndexOf('[');
        return bracket < 0 ? path : path[..bracket];
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
