using System.Collections.Concurrent;
using S1kdTools.Editing;

namespace S1kdTools.Editor.Server;

/// <summary>
/// The CSDB the sample edits, and the sessions open on it.
///
/// One session per document, shared by every browser that opens it — this is a
/// demonstration server, and two windows on one data module showing each other's
/// edits is more useful here than per-connection isolation would be. It is also
/// the honest shape: a real authoring system checks a module out to one author,
/// and pretending otherwise by handing every tab its own copy would hide the
/// question rather than answer it.
///
/// <see cref="EditSession"/> is not thread-safe, so every path through it holds
/// the session's lock. The work inside is a transform over a few tens of
/// kilobytes; the lock is never held across a PDF render, which is why
/// <see cref="Read"/> exists to take a consistent copy and let go.
/// </summary>
public sealed class CsdbLibrary
{
    private readonly ConcurrentDictionary<string, DocumentEntry> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Open the CSDB in <paramref name="directory"/>.</summary>
    /// <param name="directory">The folder holding the CSDB objects and their ICNs.</param>
    /// <param name="workingDirectory">
    /// Where <c>save</c> writes. Kept separate from the CSDB so a demonstration can
    /// be run, saved and re-run without its checked-in objects drifting; a server
    /// that owns its CSDB passes the same directory twice.
    /// </param>
    /// <param name="profile">
    /// Which dialect every session in this library edits in.
    /// <see cref="EditProfile.Default"/> when null.
    /// </param>
    public CsdbLibrary(string directory, string workingDirectory, EditProfile? profile = null)
    {
        Profile = profile ?? EditProfile.Default;

        Directory = Path.GetFullPath(directory);
        WorkingDirectory = Path.GetFullPath(workingDirectory);

        if (!System.IO.Directory.Exists(Directory))
        {
            throw new DirectoryNotFoundException($"No CSDB at {Directory}.");
        }

        System.IO.Directory.CreateDirectory(WorkingDirectory);

        foreach (string file in System.IO.Directory
                     .EnumerateFiles(Directory)
                     .Where(IsCsdbObject)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            _documents[id] = new DocumentEntry(id, file, Profile);
        }
    }

    /// <summary>The CSDB the sample reads.</summary>
    public string Directory { get; }

    /// <summary>Where saved copies are written.</summary>
    public string WorkingDirectory { get; }

    /// <summary>The stylesheet and vocabulary every session here edits in.</summary>
    public EditProfile Profile { get; }

    /// <summary>Every document, with the identifier the API addresses it by.</summary>
    public IReadOnlyList<DocumentSummary> List() =>
    [
        .. _documents.Values
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => d.Summarize())
    ];

    /// <summary>
    /// Take a consistent copy of a document's state and let the session go.
    ///
    /// Everything the API answers with is built from this, including the PDF: the
    /// render reads the XML string it took here rather than the live session, so a
    /// layout that takes a second does not hold up the author's next keystroke.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No document has that identifier.</exception>
    public EditorState Read(string id) => Entry(id).Read();

    /// <summary>Apply a batch of edits as one undoable step.</summary>
    public EditorState Apply(string id, IReadOnlyList<EditCommand> commands) =>
        Entry(id).Mutate(session => session.Apply(commands));

    /// <summary>Replace the whole source.</summary>
    public EditorState SetXml(string id, string xml) =>
        Entry(id).Mutate(session => session.SetXml(xml));

    /// <summary>Reverse the last edit; a no-op when there is nothing to undo.</summary>
    public EditorState Undo(string id) => Entry(id).Mutate(session => session.Undo());

    /// <summary>Reapply the last undone edit; a no-op when there is nothing to redo.</summary>
    public EditorState Redo(string id) => Entry(id).Mutate(session => session.Redo());

    /// <summary>Throw the session away and read the document from the CSDB again.</summary>
    public EditorState Revert(string id) => Entry(id).Revert();

    /// <summary>Write the document to the working directory.</summary>
    public EditorState Save(string id) => Entry(id).Save(WorkingDirectory);

    private DocumentEntry Entry(string id) =>
        _documents.TryGetValue(id, out DocumentEntry? entry)
            ? entry
            : throw new KeyNotFoundException($"No CSDB object with identifier '{id}'.");

    private static bool IsCsdbObject(string file) =>
        Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>One document and the session open on it.</summary>
    private sealed class DocumentEntry(string id, string path, EditProfile profile)
    {
        private readonly Lock _gate = new();
        private EditSession? _session;
        private bool _dirty;
        private DateTimeOffset? _savedAt;

        public string Id => id;

        public DocumentSummary Summarize()
        {
            lock (_gate)
            {
                EditDocument model = Session().Model;
                return new DocumentSummary(id, Path.GetFileName(path), model.Code, model.Title,
                    model.ObjectType, model.Schema, _dirty);
            }
        }

        public EditorState Read()
        {
            lock (_gate)
            {
                return Snapshot(Session());
            }
        }

        /// <summary>
        /// Run <paramref name="change"/> against the session and answer with the
        /// state that results. The document is marked dirty whatever the change
        /// was: an undo back to the text on disk still leaves a session holding a
        /// document the file does not know about, and telling the author it is
        /// saved because the bytes happen to match again would be a lie the moment
        /// they type once more.
        /// </summary>
        public EditorState Mutate(Action<EditSession> change)
        {
            lock (_gate)
            {
                EditSession session = Session();
                change(session);
                _dirty = true;
                return Snapshot(session);
            }
        }

        public EditorState Revert()
        {
            lock (_gate)
            {
                _session = null;
                _dirty = false;
                _savedAt = null;
                return Snapshot(Session());
            }
        }

        public EditorState Save(string workingDirectory)
        {
            lock (_gate)
            {
                EditSession session = Session();
                File.WriteAllText(Path.Combine(workingDirectory, Path.GetFileName(path)), session.Xml);
                _dirty = false;
                _savedAt = DateTimeOffset.UtcNow;
                return Snapshot(session);
            }
        }

        private EditSession Session() => _session ??= EditSession.Open(path, profile);

        private EditorState Snapshot(EditSession session)
        {
            EditDocument model = session.Model;
            return new EditorState(
                id,
                Path.GetFileName(path),
                model.Code,
                model.Title,
                model.ObjectType,
                model.Schema,
                session.Xml,
                model,
                new HistoryState(session.UndoDepth, session.UndoLabel),
                new HistoryState(session.RedoDepth, session.RedoLabel),
                _dirty,
                _savedAt?.ToString("O"));
        }
    }
}
