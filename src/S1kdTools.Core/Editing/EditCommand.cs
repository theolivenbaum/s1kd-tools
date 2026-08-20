namespace S1kdTools.Editing;

/// <summary>The kinds of edit the engine understands.</summary>
public static class EditOps
{
    /// <summary>Replace a block's inline content with <see cref="EditCommand.Runs"/>.</summary>
    public const string SetText = "setText";

    /// <summary>Set (or, with an empty value, remove) an attribute of a block's element.</summary>
    public const string SetAttr = "setAttr";

    /// <summary>Insert a new element relative to a block.</summary>
    public const string Insert = "insert";

    /// <summary>Remove a block's element.</summary>
    public const string Delete = "delete";

    /// <summary>Move a block's element among its siblings.</summary>
    public const string Move = "move";
}

/// <summary>Where <see cref="EditOps.Insert"/> puts the new element.</summary>
public static class EditPositions
{
    /// <summary>As the preceding sibling of the referenced element.</summary>
    public const string Before = "before";

    /// <summary>As the following sibling of the referenced element.</summary>
    public const string After = "after";

    /// <summary>As the referenced element's first child.</summary>
    public const string FirstChild = "firstChild";

    /// <summary>As the referenced element's last child.</summary>
    public const string LastChild = "lastChild";
}

/// <summary>
/// One edit, addressed at a block's <see cref="Path"/>.
///
/// A single flat shape rather than a class per operation: it crosses a network
/// boundary as JSON, is written by a front-end compiled to JavaScript, and has to
/// be readable from both sides without either owning a polymorphic serializer.
/// <see cref="Op"/> says which of the fields below matter.
/// </summary>
public sealed class EditCommand
{
    /// <summary>One of the constants on <see cref="EditOps"/>.</summary>
    public string Op { get; set; } = "";

    /// <summary>
    /// The XPath of the element the edit addresses, as
    /// <see cref="EditBlock.Path"/> gave it. Only valid against the revision of
    /// the document the block was projected from.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>For <see cref="EditOps.SetText"/>: the block's new inline content.</summary>
    public IReadOnlyList<EditRun> Runs { get; set; } = [];

    /// <summary>For <see cref="EditOps.SetAttr"/>: the attribute name.</summary>
    public string Name { get; set; } = "";

    /// <summary>For <see cref="EditOps.SetAttr"/>: the new value; empty removes the attribute.</summary>
    public string Value { get; set; } = "";

    /// <summary>For <see cref="EditOps.Insert"/>: the element to create.</summary>
    public string Element { get; set; } = "";

    /// <summary>For <see cref="EditOps.Insert"/>: one of the constants on <see cref="EditPositions"/>.</summary>
    public string Position { get; set; } = EditPositions.After;

    /// <summary>For <see cref="EditOps.Insert"/>: initial text for the new element.</summary>
    public string Text { get; set; } = "";

    /// <summary>For <see cref="EditOps.Move"/>: <c>up</c> or <c>down</c>.</summary>
    public string Direction { get; set; } = "";

    /// <summary>A short description of the edit, for the undo stack and the revision log.</summary>
    public string Describe() => Op switch
    {
        EditOps.SetText => "Edit text",
        EditOps.SetAttr => Value.Length == 0 ? $"Remove {Name}" : $"Set {Name}",
        EditOps.Insert => $"Insert {Element}",
        EditOps.Delete => "Delete block",
        EditOps.Move => Direction == "up" ? "Move up" : "Move down",
        _ => Op,
    };
}

/// <summary>Raised when a command cannot be applied to the document it addresses.</summary>
public sealed class EditCommandException(string message) : Exception(message);
