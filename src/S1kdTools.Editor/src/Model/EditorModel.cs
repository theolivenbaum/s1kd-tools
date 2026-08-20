using Transpose;

namespace S1kdTools.Editor
{
    /// <summary>
    /// The editor's wire model, declared to the compiler rather than deserialized.
    ///
    /// Everything the server sends is JSON produced from the C# model in
    /// <c>S1kdTools.Core</c> (<c>S1kdTools.Editing.EditDocument</c> and friends).
    /// These <c>[External]</c> interfaces name that shape, so <c>JSON.parse</c>'s
    /// result is used directly and typed: no reflection metadata to keep, no
    /// serializer in the bundle, and a member the server does not send becomes a
    /// build error at the call site rather than an undefined at run time.
    ///
    /// <c>[Convention(Notation.None)]</c> is what keeps the C# names identical to
    /// the JSON ones. Without it the compiler would camel-case them a second time
    /// and <c>state.model</c> would be emitted as a property the payload does not
    /// have. It is also why these members are lower-case: they are the JSON's
    /// names, not names of this library's choosing.
    ///
    /// <b>These are views of a parsed payload, not values.</b> They are replaced
    /// wholesale every time the server answers - see the note on paths in
    /// <see cref="IEditBlock.path"/> - so nothing should hold one across a call.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditorState
    {
        /// <summary>The document's identifier: its CSDB file name without the extension.</summary>
        string id { get; }

        /// <summary>The file it was read from.</summary>
        string fileName { get; }

        /// <summary>The data module or publication module code.</summary>
        string code { get; }

        /// <summary>The object's title.</summary>
        string title { get; }

        /// <summary>A human-readable object type, e.g. <c>Procedure</c>.</summary>
        string objectType { get; }

        /// <summary>The schema it declares, e.g. <c>proced</c>.</summary>
        string schema { get; }

        /// <summary>The source, as the code editor shows it.</summary>
        string xml { get; }

        /// <summary>The projection the editing surface draws.</summary>
        IEditDocument model { get; }

        /// <summary>What undoing would reverse.</summary>
        IHistoryState undo { get; }

        /// <summary>What redoing would reapply.</summary>
        IHistoryState redo { get; }

        /// <summary>Whether the session holds edits that are not on disk.</summary>
        bool dirty { get; }

        /// <summary>When the document was last saved, or null.</summary>
        string savedAt { get; }
    }

    /// <summary>One end of the undo history, for a toolbar button's label and enablement.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IHistoryState
    {
        /// <summary>How many steps are on the stack.</summary>
        int depth { get; }

        /// <summary>What the next step would do, or null when the stack is empty.</summary>
        string label { get; }
    }

    /// <summary>A CSDB object as a tree of editable blocks.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditDocument
    {
        /// <summary>The name of the root element, e.g. <c>dmodule</c>.</summary>
        string root { get; }

        /// <summary>The schema the object declares.</summary>
        string schema { get; }

        /// <summary>A human-readable object type.</summary>
        string objectType { get; }

        /// <summary>The object's code.</summary>
        string code { get; }

        /// <summary>The object's title.</summary>
        string title { get; }

        /// <summary>The identification section, then the content section.</summary>
        IEditSection[] sections { get; }
    }

    /// <summary>One top-level division of the projection.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditSection
    {
        /// <summary><c>ident</c> or <c>content</c>.</summary>
        string key { get; }

        /// <summary>The heading shown above the section.</summary>
        string label { get; }

        /// <summary>The blocks in the section.</summary>
        IEditBlock[] blocks { get; }
    }

    /// <summary>
    /// One editable thing on the page - a paragraph, a step, a warning, a table
    /// cell, a labelled metadata field.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditBlock
    {
        /// <summary>
        /// The XPath of the element this block came from, and the address every
        /// command is expressed against.
        ///
        /// <b>Only valid against the revision it was projected from.</b> Inserting
        /// a paragraph renumbers the positional predicates of its later siblings,
        /// so a path collected before an edit must not be used after one. Every
        /// endpoint answers with a whole fresh state for exactly this reason.
        /// </summary>
        string path { get; }

        /// <summary>The name of the source element, e.g. <c>para</c>.</summary>
        string element { get; }

        /// <summary>
        /// What the block is, which is what the surface chooses a shape from:
        /// <c>para</c>, <c>title</c>, <c>step</c>, <c>warning</c>, <c>caution</c>,
        /// <c>note</c>, <c>attention</c>, <c>figure</c>, <c>graphic</c>,
        /// <c>table</c>, <c>row</c>, <c>cell</c>, <c>list</c>, <c>listItem</c>,
        /// <c>requirement</c>, <c>reference</c>, <c>applic</c>, <c>partRow</c>,
        /// <c>field</c>, <c>metaField</c>, <c>group</c>, <c>section</c> or
        /// <c>unknown</c>. See <see cref="BlockKinds"/>.
        /// </summary>
        string kind { get; }

        /// <summary>The number or bullet printed in the margin, e.g. <c>1.</c>.</summary>
        string label { get; }

        /// <summary>The heading printed above the block, e.g. <c>WARNING</c>.</summary>
        string heading { get; }

        /// <summary>Nesting depth, for indentation.</summary>
        int level { get; }

        /// <summary><c>none</c>, <c>text</c> or <c>attr</c>. See <see cref="EditModes"/>.</summary>
        string editable { get; }

        /// <summary>Placeholder text for an empty block.</summary>
        string placeholder { get; }

        /// <summary>For <c>attr</c> blocks: the attribute edited.</summary>
        string attrName { get; }

        /// <summary>For <c>attr</c> blocks: its value. Also a reference's display value.</summary>
        string value { get; }

        /// <summary>When present, the value is one of these.</summary>
        string[] options { get; }

        /// <summary>Whether the element may be removed from its parent.</summary>
        bool canDelete { get; }

        /// <summary>Whether the element may be reordered among its siblings.</summary>
        bool canMove { get; }

        /// <summary>The inline content, for a <c>text</c> block.</summary>
        IEditRun[] runs { get; }

        /// <summary>The attributes offered for editing.</summary>
        IEditAttribute[] attributes { get; }

        /// <summary>Nested blocks.</summary>
        IEditBlock[] blocks { get; }

        /// <summary>What may be inserted beside this block.</summary>
        IInsertOption[] insertSiblings { get; }

        /// <summary>What may be inserted inside it.</summary>
        IInsertOption[] insertChildren { get; }
    }

    /// <summary>
    /// A piece of a block's inline content: plain text, styled text, or an atomic
    /// element the author sees as a chip and cannot retype.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditRun
    {
        /// <summary>The text shown for this run.</summary>
        string text { get; }

        /// <summary>
        /// <c>bold</c>, <c>italic</c>, <c>underline</c>, <c>subscript</c>,
        /// <c>superscript</c>, <c>code</c>, or empty. See <see cref="RunStyles"/>.
        /// </summary>
        string style { get; }

        /// <summary>Whether the run is a chip the author cannot type into.</summary>
        bool atomic { get; }

        /// <summary>The source element's name, when the run came from one.</summary>
        string element { get; }

        /// <summary>What an atomic run is, for the chip's icon: <c>dmRef</c>, ….</summary>
        string refKind { get; }

        /// <summary>What an atomic run points at.</summary>
        string target { get; }

        /// <summary>
        /// The 1-based position of the source element among its parent's child
        /// elements, or 0 for a run that came from a text node. Carried back
        /// unchanged on every run the author did not remove, which is what lets the
        /// server put the original element back instead of rebuilding it.
        /// </summary>
        int src { get; }
    }

    /// <summary>An attribute of a block's element, offered for editing.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditAttribute
    {
        /// <summary>The attribute name.</summary>
        string name { get; }

        /// <summary>Its current value.</summary>
        string value { get; }

        /// <summary>The label shown beside the field.</summary>
        string label { get; }

        /// <summary><c>text</c>, <c>choice</c> or <c>applic</c>.</summary>
        string type { get; }

        /// <summary>The permitted values, when <c>type</c> is <c>choice</c>.</summary>
        string[] options { get; }
    }

    /// <summary>An element the author may insert, as the menu shows it.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IInsertOption
    {
        /// <summary>The element name to send in the command.</summary>
        string Element { get; }

        /// <summary>What the menu calls it.</summary>
        string Label { get; }

        /// <summary>The block kind it will project as, for the menu's icon.</summary>
        string Kind { get; }
    }

    /// <summary>One document in the CSDB, as the picker lists it.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IDocumentSummary
    {
        /// <summary>The identifier the API addresses it by.</summary>
        string id { get; }

        /// <summary>The file it was read from.</summary>
        string fileName { get; }

        /// <summary>The data module or publication module code.</summary>
        string code { get; }

        /// <summary>The object's title.</summary>
        string title { get; }

        /// <summary>A human-readable object type.</summary>
        string objectType { get; }

        /// <summary>The schema it declares.</summary>
        string schema { get; }

        /// <summary>Whether the server's session holds unsaved edits.</summary>
        bool dirty { get; }
    }

    /// <summary>The result of checking a document.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ICheckReport
    {
        /// <summary>Whether nothing of severity <c>error</c> was found.</summary>
        bool ok { get; }

        /// <summary>The BREX data module the content was checked against.</summary>
        string brex { get; }

        /// <summary>What was found, errors first.</summary>
        ICheckFinding[] findings { get; }
    }

    /// <summary>One thing wrong with the document.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ICheckFinding
    {
        /// <summary><c>error</c> or <c>warning</c>.</summary>
        string severity { get; }

        /// <summary>Which check found it: <c>xml</c>, <c>brex</c> or <c>render</c>.</summary>
        string source { get; }

        /// <summary>What is wrong.</summary>
        string message { get; }

        /// <summary>
        /// The XPath of the offending element, in the same shape a block carries -
        /// so a finding can be clicked and land the author on the block it is about.
        /// Null when the finding is about the document as a whole.
        /// </summary>
        string path { get; }

        /// <summary>The BREX rule's own explanation of itself, when there is one.</summary>
        string rule { get; }
    }

    /// <summary>The values <see cref="IEditBlock.editable"/> takes.</summary>
    public static class EditModes
    {
        /// <summary>Nothing on the block is typed into; its children carry the content.</summary>
        public const string None = "none";

        /// <summary>The block's inline content, as <see cref="IEditBlock.runs"/>.</summary>
        public const string Text = "text";

        /// <summary>One attribute, named by <see cref="IEditBlock.attrName"/>.</summary>
        public const string Attr = "attr";
    }

    /// <summary>The values <see cref="IEditRun.style"/> takes.</summary>
    public static class RunStyles
    {
        /// <summary><c>emphasis</c> with no type.</summary>
        public const string Bold = "bold";

        /// <summary><c>emphasis emphasisType="em02"</c>.</summary>
        public const string Italic = "italic";

        /// <summary><c>emphasis emphasisType="em03"</c>.</summary>
        public const string Underline = "underline";

        /// <summary><c>subScript</c>.</summary>
        public const string Subscript = "subscript";

        /// <summary><c>superScript</c>.</summary>
        public const string Superscript = "superscript";

        /// <summary><c>verbatimText</c>.</summary>
        public const string Code = "code";
    }

    /// <summary>The block kinds the surface draws differently.</summary>
    public static class BlockKinds
    {
        /// <summary>A paragraph or any other prose-carrying element.</summary>
        public const string Para = "para";

        /// <summary>A title.</summary>
        public const string Title = "title";

        /// <summary>A procedural, isolation or crew-drill step.</summary>
        public const string Step = "step";

        /// <summary>A warning box.</summary>
        public const string Warning = "warning";

        /// <summary>A caution box.</summary>
        public const string Caution = "caution";

        /// <summary>A note.</summary>
        public const string Note = "note";

        /// <summary>An attention notice.</summary>
        public const string Attention = "attention";

        /// <summary>A figure.</summary>
        public const string Figure = "figure";

        /// <summary>A graphic inside a figure.</summary>
        public const string Graphic = "graphic";

        /// <summary>A table.</summary>
        public const string Table = "table";

        /// <summary>A table row.</summary>
        public const string Row = "row";

        /// <summary>A table cell.</summary>
        public const string Cell = "cell";

        /// <summary>A list.</summary>
        public const string List = "list";

        /// <summary>A list item.</summary>
        public const string ListItem = "listItem";

        /// <summary>A job set-up requirement: equipment, a consumable, a spare, a condition.</summary>
        public const string Requirement = "requirement";

        /// <summary>A reference to another CSDB object.</summary>
        public const string Reference = "reference";

        /// <summary>An applicability definition.</summary>
        public const string Applic = "applic";

        /// <summary>A row of a parts catalogue.</summary>
        public const string PartRow = "partRow";

        /// <summary>A labelled single-value field inside a block.</summary>
        public const string Field = "field";

        /// <summary>A labelled field of the identification and status section.</summary>
        public const string MetaField = "metaField";

        /// <summary>A structural grouping with a heading.</summary>
        public const string Group = "group";

        /// <summary>A levelled paragraph.</summary>
        public const string Section = "section";

        /// <summary>An element the projection has no template for.</summary>
        public const string Unknown = "unknown";
    }
}
