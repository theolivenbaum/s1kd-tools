using System.Xml;

namespace S1kdTools.Editing;

/// <summary>
/// One CSDB object, open for editing: the document, the model of it the editor is
/// showing, and the history that makes an edit reversible.
///
/// The XML is the document of record and the model is derived from it — never the
/// other way round. Every command mutates the XML and the model is re-projected
/// from the result, so what the editor draws is always a reading of what would be
/// saved, and there is no second representation to keep in step. It costs a
/// transform per edit, which for a data module is a few milliseconds and buys the
/// one property an authoring tool cannot do without: <b>the editor cannot show
/// something the file does not say.</b>
///
/// The same rule is why <see cref="SetXml"/> exists and is not a special case. An
/// author editing the source by hand and an author typing into a paragraph are
/// doing the same thing from opposite ends; both replace the document and both
/// re-project. That is what lets a front-end put a source editor and a WYSIWYG
/// surface on one document and let the author switch between them mid-sentence.
///
/// Not thread-safe: a session is one author's open document. A server holding
/// several serializes access per session.
/// </summary>
public sealed class EditSession
{
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];

    private XmlDocument _doc;
    private EditDocument? _model;

    /// <summary>Open a session on a document. The session takes ownership of it.</summary>
    /// <param name="doc">The CSDB object.</param>
    /// <param name="profile">
    /// Which dialect to edit in. <see cref="EditProfile.Default"/> when null.
    /// </param>
    public EditSession(XmlDocument doc, EditProfile? profile = null)
    {
        _doc = doc;
        Profile = profile ?? EditProfile.Default;
    }

    /// <summary>Open a session on the object in <paramref name="path"/>.</summary>
    public static EditSession Open(string path, EditProfile? profile = null) =>
        new(XmlUtils.ReadDoc(path), profile);

    /// <summary>Open a session on an object held in a string.</summary>
    public static EditSession Parse(string xml, EditProfile? profile = null) =>
        new(XmlUtils.ReadMem(xml), profile);

    /// <summary>The stylesheet and vocabulary this session edits in.</summary>
    public EditProfile Profile { get; }

    /// <summary>The document as it stands. Do not mutate it behind the session's back.</summary>
    public XmlDocument Document => _doc;

    /// <summary>How many edits can be undone.</summary>
    public int UndoDepth => _undo.Count;

    /// <summary>How many undone edits can be redone.</summary>
    public int RedoDepth => _redo.Count;

    /// <summary>What undoing would reverse, or null when there is nothing to undo.</summary>
    public string? UndoLabel => _undo.Count == 0 ? null : _undo[^1].Label;

    /// <summary>What redoing would reapply, or null when there is nothing to redo.</summary>
    public string? RedoLabel => _redo.Count == 0 ? null : _redo[^1].Label;

    /// <summary>The model of the document as it stands, projected on first ask and
    /// cached until the document changes.</summary>
    public EditDocument Model => _model ??=
        EditInsertOptions.Decorate(EditProjection.Project(_doc, Profile), Profile);

    /// <summary>The document, serialized the way the tools serialize one.</summary>
    public string Xml
    {
        get
        {
            using var ms = new MemoryStream();
            XmlUtils.SaveDoc(_doc, ms);
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    /// <summary>
    /// Apply a batch of commands as one undoable edit.
    ///
    /// The batch is applied to a copy and swapped in only if every command lands.
    /// A partly-applied batch would be a document the author never asked for and
    /// could not undo their way out of, since the undo stack would hold one entry
    /// for a change that half happened.
    /// </summary>
    /// <returns>The model of the document after the edit.</returns>
    /// <exception cref="EditCommandException">A command could not be applied. The session is unchanged.</exception>
    public EditDocument Apply(IReadOnlyList<EditCommand> commands)
    {
        if (commands.Count == 0)
        {
            return Model;
        }

        var candidate = (XmlDocument)_doc.CloneNode(true);
        EditCommands.ApplyAll(candidate, commands, Profile);

        Commit(candidate, Label(commands));
        return Model;
    }

    /// <summary>Apply a single command as one undoable edit.</summary>
    public EditDocument Apply(EditCommand command) => Apply([command]);

    /// <summary>
    /// Replace the whole document — the source editor's write path.
    /// </summary>
    /// <exception cref="EditCommandException">The text is not well-formed XML.</exception>
    public EditDocument SetXml(string xml, string label = "Edit source")
    {
        XmlDocument parsed;
        try
        {
            parsed = XmlUtils.ReadMem(xml);
        }
        catch (XmlException e)
        {
            // The line and column are the whole value of the message to an author
            // looking at their own text, so they are kept rather than flattened
            // into "invalid XML".
            throw new EditCommandException(e.Message);
        }

        Commit(parsed, label);
        return Model;
    }

    /// <summary>Reverse the last edit. Returns false when there is nothing to undo.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        Snapshot previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(new Snapshot(_doc, previous.Label));

        _doc = previous.Document;
        _model = null;
        return true;
    }

    /// <summary>Reapply the last undone edit. Returns false when there is nothing to redo.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        Snapshot next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(new Snapshot(_doc, next.Label));

        _doc = next.Document;
        _model = null;
        return true;
    }

    /// <summary>
    /// Take the document over, pushing the current one onto the undo stack.
    ///
    /// A whole-document snapshot per edit rather than an inverse command: a data
    /// module is tens of kilobytes, and an inverse for "insert" that is correct
    /// under every subsequent edit is a far harder thing to get right than a copy.
    /// The stack is bounded because an editing session left open all afternoon
    /// should not be measured in hundreds of copies.
    /// </summary>
    private void Commit(XmlDocument candidate, string label)
    {
        _undo.Add(new Snapshot(_doc, label));
        if (_undo.Count > MaxUndo)
        {
            _undo.RemoveAt(0);
        }

        // A new edit is a new branch: what was undone can no longer be redone.
        _redo.Clear();

        _doc = candidate;
        _model = null;
    }

    private const int MaxUndo = 100;

    private static string Label(IReadOnlyList<EditCommand> commands) =>
        commands.Count == 1 ? commands[0].Describe() : $"{commands.Count} edits";

    private readonly record struct Snapshot(XmlDocument Document, string Label);
}
