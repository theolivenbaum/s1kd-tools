namespace S1kdTools.Editing;

/// <summary>
/// A CSDB object as something to edit: the projection an editing front-end
/// renders, and the coordinates an edit is expressed against.
///
/// The model is produced by <see cref="EditProjection"/> from
/// <c>Resources/editing/edit.xsl</c> and is deliberately shallow in what it
/// claims: it does not model S1000D, it models *what can be typed into*. A block
/// is a thing on the page with a path back to the element it came from; a run is
/// a piece of a block's text with an optional style, or an atomic reference the
/// author cannot retype. Everything else about the object — its schema, the order
/// its elements are allowed in, what a BREX makes of it — stays in the XML, which
/// remains the document of record. The model is a view of it, regenerated after
/// every command.
///
/// The consequence worth knowing: <b>a block's path is only valid against the
/// revision of the document it was projected from.</b> Inserting a paragraph
/// renumbers the positional predicates of its later siblings, so a front-end must
/// re-read the model after each command rather than keep applying edits against
/// paths it collected earlier. <see cref="EditSession"/> returns a fresh model
/// from every call for that reason.
/// </summary>
public sealed class EditDocument
{
    /// <summary>The name of the root element, e.g. <c>dmodule</c>.</summary>
    public string Root { get; init; } = "";

    /// <summary>The schema the object declares, e.g. <c>proced</c>, or the root element name.</summary>
    public string Schema { get; init; } = "";

    /// <summary>A human-readable object type, e.g. <c>Procedure</c>.</summary>
    public string ObjectType { get; init; } = "";

    /// <summary>The data module or publication module code.</summary>
    public string Code { get; init; } = "";

    /// <summary>The object's title, as the tech name and info name joined.</summary>
    public string Title { get; init; } = "";

    /// <summary>The sections of the projection: identification, then content.</summary>
    public IReadOnlyList<EditSection> Sections { get; init; } = [];

    /// <summary>Every block in the document, depth-first, including nested ones.</summary>
    public IEnumerable<EditBlock> AllBlocks()
    {
        foreach (EditSection section in Sections)
        {
            foreach (EditBlock block in section.Blocks)
            {
                foreach (EditBlock descendant in Descend(block))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>The block at <paramref name="path"/>, or null when the model has none.</summary>
    public EditBlock? Find(string path) =>
        AllBlocks().FirstOrDefault(b => string.Equals(b.Path, path, StringComparison.Ordinal));

    private static IEnumerable<EditBlock> Descend(EditBlock block)
    {
        yield return block;
        foreach (EditBlock child in block.Blocks)
        {
            foreach (EditBlock descendant in Descend(child))
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>One top-level division of the projection.</summary>
public sealed class EditSection
{
    /// <summary>A stable key: <c>ident</c> or <c>content</c>.</summary>
    public string Key { get; init; } = "";

    /// <summary>The heading shown above the section.</summary>
    public string Label { get; init; } = "";

    /// <summary>The blocks in the section.</summary>
    public IReadOnlyList<EditBlock> Blocks { get; init; } = [];
}

/// <summary>How a block's substance is edited.</summary>
public enum EditMode
{
    /// <summary>Nothing on this block is typed into; its children carry the content.</summary>
    None,

    /// <summary>The block's inline content, as <see cref="EditBlock.Runs"/>.</summary>
    Text,

    /// <summary>A single attribute of the block's element, named by <see cref="EditBlock.AttrName"/>.</summary>
    Attr,
}

/// <summary>
/// One editable thing on the page — a paragraph, a step, a warning, a table cell,
/// a labelled metadata field.
/// </summary>
public sealed class EditBlock
{
    /// <summary>
    /// The XPath of the element this block came from, positionally predicated at
    /// every step, in the shape <see cref="XmlUtils.XPathOf"/> produces. This is
    /// the address every command is expressed against.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>The name of the source element, e.g. <c>para</c>.</summary>
    public string Element { get; init; } = "";

    /// <summary>
    /// What the block is, for the front-end to choose a shape for: <c>para</c>,
    /// <c>title</c>, <c>step</c>, <c>warning</c>, <c>caution</c>, <c>note</c>,
    /// <c>attention</c>, <c>figure</c>, <c>graphic</c>, <c>table</c>, <c>row</c>,
    /// <c>cell</c>, <c>list</c>, <c>listItem</c>, <c>requirement</c>,
    /// <c>reference</c>, <c>applic</c>, <c>partRow</c>, <c>field</c>,
    /// <c>metaField</c>, <c>group</c>, <c>section</c> or <c>unknown</c>.
    /// </summary>
    public string Kind { get; init; } = "";

    /// <summary>The number or bullet printed in the margin, e.g. <c>1.</c> or <c>A.</c>.</summary>
    public string Label { get; init; } = "";

    /// <summary>The heading printed above the block, e.g. <c>WARNING</c>.</summary>
    public string Heading { get; init; } = "";

    /// <summary>Nesting depth, for indentation.</summary>
    public int Level { get; init; }

    /// <summary>How the block is edited.</summary>
    public EditMode Editable { get; init; }

    /// <summary>Placeholder text for an empty block.</summary>
    public string Placeholder { get; init; } = "";

    /// <summary>For <see cref="EditMode.Attr"/>: the attribute edited.</summary>
    public string AttrName { get; init; } = "";

    /// <summary>For <see cref="EditMode.Attr"/>: its current value. Also the display value of a reference.</summary>
    public string Value { get; init; } = "";

    /// <summary>When set, the value is one of these (comma-separated in the projection).</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>Whether the block's element may be removed from its parent.</summary>
    public bool CanDelete { get; init; }

    /// <summary>Whether the block's element may be reordered among its siblings.</summary>
    public bool CanMove { get; init; }

    /// <summary>The inline content, when <see cref="Editable"/> is <see cref="EditMode.Text"/>.</summary>
    public IReadOnlyList<EditRun> Runs { get; init; } = [];

    /// <summary>The attributes offered for editing.</summary>
    public IReadOnlyList<EditAttribute> Attributes { get; init; } = [];

    /// <summary>Nested blocks.</summary>
    public IReadOnlyList<EditBlock> Blocks { get; init; } = [];

    /// <summary>
    /// What the editor may insert beside this block, and inside it. Filled in
    /// after projection by <see cref="EditInsertOptions.Decorate"/> rather than by
    /// the stylesheet: which elements are offered is a decision about the editor's
    /// vocabulary, and a stylesheet has no business holding it.
    /// </summary>
    public IReadOnlyList<EditTemplates.InsertOption> InsertSiblings { get; set; } = [];

    /// <inheritdoc cref="InsertSiblings"/>
    public IReadOnlyList<EditTemplates.InsertOption> InsertChildren { get; set; } = [];

    /// <summary>The block's text with all runs joined, for search, tests and diffing.</summary>
    public string Text => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>
/// A piece of a block's inline content.
///
/// A run is one of three things, and which one it is decides what happens when
/// the author's edit comes back:
///
/// * <b>plain text</b> — no <see cref="Src"/>, no <see cref="Style"/>: written back
///   as a text node;
/// * <b>styled text</b> — a <see cref="Style"/> and usually a <see cref="Src"/>:
///   the original <c>emphasis</c>/<c>subScript</c>/<c>superScript</c> element is
///   reused with its text replaced, so attributes this model does not carry
///   survive the round trip;
/// * <b>an atomic element</b> — <see cref="Atomic"/>: a reference, an acronym, a
///   quantity. The author sees a chip and cannot retype it; the original element
///   goes back untouched.
/// </summary>
public sealed class EditRun
{
    /// <summary>The text shown for this run.</summary>
    public string Text { get; init; } = "";

    /// <summary><c>bold</c>, <c>italic</c>, <c>underline</c>, <c>subscript</c>, <c>superscript</c>, <c>code</c>, or empty.</summary>
    public string Style { get; init; } = "";

    /// <summary>Whether the run is a chip the author cannot type into.</summary>
    public bool Atomic { get; init; }

    /// <summary>The source element's name, when the run came from one.</summary>
    public string Element { get; init; } = "";

    /// <summary>What an atomic run is, for the chip's icon: <c>dmRef</c>, <c>internalRef</c>, ….</summary>
    public string RefKind { get; init; } = "";

    /// <summary>What an atomic run points at — a data module code, an id, an acronym expansion.</summary>
    public string Target { get; init; } = "";

    /// <summary>
    /// The 1-based position of the source element among its parent's child
    /// elements, or 0 for a run that came from a text node. This is how a run
    /// that the author did not retype is written back as the very element it came
    /// from rather than as a reconstruction of it.
    /// </summary>
    public int Src { get; init; }

    /// <summary>A plain-text run.</summary>
    public static EditRun Plain(string text) => new() { Text = text };
}

/// <summary>An attribute of a block's element, offered for editing.</summary>
public sealed class EditAttribute
{
    /// <summary>The attribute name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Its current value.</summary>
    public string Value { get; init; } = "";

    /// <summary>The label shown beside the field.</summary>
    public string Label { get; init; } = "";

    /// <summary><c>text</c>, <c>choice</c> or <c>applic</c>.</summary>
    public string Type { get; init; } = "text";

    /// <summary>The permitted values, when <see cref="Type"/> is <c>choice</c>.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}
