using System.Xml;

namespace S1kdTools.Editing;

/// <summary>
/// Applies an <see cref="EditCommand"/> to the document it addresses.
///
/// This is the half of the editor that must not be clever. The projection can
/// afford to simplify — it is a view, and a view that shows a little less than
/// the object holds costs nothing. Writing cannot: an edit that silently drops an
/// attribute, or rebuilds a reference the author never touched, loses data that
/// nothing downstream can recover.
///
/// So the write path is built around one rule: <b>an element the author did not
/// retype is put back, not recreated.</b> <see cref="EditRun.Src"/> carries each
/// run's position among the block's original child elements, and
/// <see cref="SetText"/> moves those very nodes back into place. A <c>dmRef</c>
/// survives an edit to the sentence around it with its address items, its
/// applicability and any attribute this port has never heard of intact, because
/// it is the same node — it was never serialized and re-parsed.
/// </summary>
public static class EditCommands
{
    /// <summary>Apply one command, mutating <paramref name="doc"/> in place.</summary>
    /// <exception cref="EditCommandException">
    /// The path does not resolve, or the command is not one the engine knows.
    /// </exception>
    public static void Apply(XmlDocument doc, EditCommand command)
    {
        switch (command.Op)
        {
            case EditOps.SetText:
                SetText(doc, command);
                break;
            case EditOps.SetAttr:
                SetAttr(doc, command);
                break;
            case EditOps.Insert:
                Insert(doc, command);
                break;
            case EditOps.Delete:
                Delete(doc, command);
                break;
            case EditOps.Move:
                Move(doc, command);
                break;
            default:
                throw new EditCommandException($"Unknown edit operation '{command.Op}'.");
        }
    }

    /// <summary>Apply a batch, in order. Either all of them land or none do:
    /// the caller works on a copy and swaps it in, because a half-applied batch
    /// is a document nobody asked for.</summary>
    public static void ApplyAll(XmlDocument doc, IEnumerable<EditCommand> commands)
    {
        foreach (EditCommand command in commands)
        {
            Apply(doc, command);
        }
    }

    /// <summary>Resolve a block path to the element it names.</summary>
    /// <exception cref="EditCommandException">Nothing is at that path.</exception>
    public static XmlElement Resolve(XmlDocument doc, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new EditCommandException("The command carries no path.");
        }

        XmlNode? node;
        try
        {
            node = doc.SelectSingleNode(path);
        }
        catch (System.Xml.XPath.XPathException)
        {
            // A path from anywhere but the projection - a hand-written command, a
            // stale client - reads as a bad request rather than a server fault.
            throw new EditCommandException($"'{path}' is not a usable path.");
        }

        return node as XmlElement
            ?? throw new EditCommandException(
                $"Nothing is at '{path}'. The document has changed since it was projected.");
    }

    // ------------------------------------------------------------------------
    // setText
    // ------------------------------------------------------------------------

    private static void SetText(XmlDocument doc, EditCommand command)
    {
        XmlElement element = Resolve(doc, command.Path);

        // The block's child elements, by the 1-based position the projection
        // numbered them with. Taken before anything is removed, and each one used
        // at most once: a run claiming an index a previous run already took gets a
        // copy, so a front-end bug duplicates a reference rather than losing it.
        List<XmlElement> originals = [.. element.ChildNodes.OfType<XmlElement>()];
        var claimed = new HashSet<int>();

        element.RemoveAll();

        foreach (EditRun run in command.Runs)
        {
            XmlNode? node = BuildRun(doc, run, originals, claimed);
            if (node != null)
            {
                element.AppendChild(node);
            }
        }
    }

    private static XmlNode? BuildRun(XmlDocument doc, EditRun run,
        List<XmlElement> originals, HashSet<int> claimed)
    {
        XmlElement? original = run.Src >= 1 && run.Src <= originals.Count
            ? originals[run.Src - 1]
            : null;

        if (run.Atomic)
        {
            // A chip. The author cannot have changed it, so nothing about it is
            // rebuilt — the original node goes back exactly as it came out.
            if (original == null)
            {
                return null;
            }
            return claimed.Add(run.Src) ? original : original.CloneNode(true);
        }

        string wanted = ElementForStyle(run.Style);

        if (wanted.Length == 0)
        {
            return run.Text.Length == 0 ? null : doc.CreateTextNode(run.Text);
        }

        // Styled text. Reusing the original element keeps whatever it carried that
        // this model does not describe; it is only abandoned when the author
        // changed the style to something a different element expresses.
        if (original != null && original.Name == wanted && claimed.Add(run.Src))
        {
            original.RemoveAll();
            if (run.Text.Length > 0)
            {
                original.AppendChild(doc.CreateTextNode(run.Text));
            }
            ApplyEmphasisType(original, run.Style);
            return original;
        }

        XmlElement created = doc.CreateElement(wanted);
        if (run.Text.Length > 0)
        {
            created.AppendChild(doc.CreateTextNode(run.Text));
        }
        ApplyEmphasisType(created, run.Style);
        return created;
    }

    /// <summary>The S1000D element a run's style is carried by, or empty for plain text.</summary>
    private static string ElementForStyle(string style) => style switch
    {
        "bold" or "italic" or "underline" => "emphasis",
        "subscript" => "subScript",
        "superscript" => "superScript",
        "code" => "verbatimText",
        _ => "",
    };

    /// <summary>
    /// S1000D spells the three emphases as values of <c>emphasisType</c> on one
    /// element: <c>em01</c> bold, <c>em02</c> italic, <c>em03</c> underline. Bold
    /// is the schema default, so it is written by removing the attribute rather
    /// than by asserting it — which is also what keeps a document that never used
    /// the attribute from acquiring it on its first edit.
    /// </summary>
    private static void ApplyEmphasisType(XmlElement element, string style)
    {
        if (element.Name != "emphasis")
        {
            return;
        }

        switch (style)
        {
            case "italic":
                element.SetAttribute("emphasisType", "em02");
                break;
            case "underline":
                element.SetAttribute("emphasisType", "em03");
                break;
            default:
                element.RemoveAttribute("emphasisType");
                break;
        }
    }

    // ------------------------------------------------------------------------
    // setAttr
    // ------------------------------------------------------------------------

    private static void SetAttr(XmlDocument doc, EditCommand command)
    {
        XmlElement element = Resolve(doc, command.Path);

        if (string.IsNullOrEmpty(command.Name))
        {
            throw new EditCommandException("An attribute edit carries no attribute name.");
        }

        if (command.Value.Length == 0)
        {
            element.RemoveAttribute(command.Name);
        }
        else
        {
            element.SetAttribute(command.Name, command.Value);
        }
    }

    // ------------------------------------------------------------------------
    // insert / delete / move
    // ------------------------------------------------------------------------

    private static void Insert(XmlDocument doc, EditCommand command)
    {
        XmlElement anchor = Resolve(doc, command.Path);
        XmlElement created = EditTemplates.Create(doc, command.Element, command.Text);

        switch (command.Position)
        {
            case EditPositions.Before:
                RequireParent(anchor).InsertBefore(created, anchor);
                break;
            case EditPositions.FirstChild:
                anchor.PrependChild(created);
                break;
            case EditPositions.LastChild:
                anchor.AppendChild(created);
                break;
            default:
                RequireParent(anchor).InsertAfter(created, anchor);
                break;
        }
    }

    private static void Delete(XmlDocument doc, EditCommand command)
    {
        XmlElement element = Resolve(doc, command.Path);
        RequireParent(element).RemoveChild(element);
    }

    private static void Move(XmlDocument doc, EditCommand command)
    {
        XmlElement element = Resolve(doc, command.Path);
        XmlNode parent = RequireParent(element);

        // Among elements, not among nodes: the whitespace between two steps is
        // formatting, and stepping over it would make "move up" a no-op on a
        // pretty-printed file.
        XmlElement? target = command.Direction == "up"
            ? PreviousElement(element)
            : NextElement(element);

        if (target == null)
        {
            throw new EditCommandException(
                $"There is nothing to move {(command.Direction == "up" ? "above" : "below")}.");
        }

        parent.RemoveChild(element);

        if (command.Direction == "up")
        {
            parent.InsertBefore(element, target);
        }
        else
        {
            parent.InsertAfter(element, target);
        }
    }

    private static XmlElement? PreviousElement(XmlNode node)
    {
        for (XmlNode? n = node.PreviousSibling; n != null; n = n.PreviousSibling)
        {
            if (n is XmlElement e)
            {
                return e;
            }
        }
        return null;
    }

    private static XmlElement? NextElement(XmlNode node)
    {
        for (XmlNode? n = node.NextSibling; n != null; n = n.NextSibling)
        {
            if (n is XmlElement e)
            {
                return e;
            }
        }
        return null;
    }

    private static XmlNode RequireParent(XmlNode node) =>
        node.ParentNode
        ?? throw new EditCommandException("The root element cannot be inserted beside, moved or removed.");
}
