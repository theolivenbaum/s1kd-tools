using System;
using System.Threading.Tasks;
using Transpose;

namespace S1kdTools.Editor
{
    /// <summary>
    /// The browser's half of the editing session: one open document, and every way
    /// of changing it.
    ///
    /// <b>The client owns the state, and every call replaces it.</b> The server
    /// answers each editing request with the whole projection rather than a delta,
    /// because a block's path is only meaningful against the revision it came from
    /// (see <see cref="IEditBlock.path"/>). So there is no merging to do here and
    /// no way for the surface's model to drift from the document: after any call,
    /// <see cref="State"/> is what the server holds, and
    /// <see cref="OnStateChanged"/> has fired.
    ///
    /// That one event is also what lets three views of one document coexist. The
    /// WYSIWYG surface, the source pane and the page preview each subscribe; an
    /// edit made in any of them reaches the other two by the same route it reaches
    /// the server, and none of them has to know the others exist.
    ///
    /// <b>How the calls are made is not decided here.</b> This class holds the
    /// session - which document is open, what the server last said, who to tell -
    /// and hands every call to an <see cref="IEditorApi"/>. The default is fetch
    /// over the endpoints <c>S1kdTools.Editor.Server</c> maps; a different prefix
    /// is <see cref="EditorRoutes"/>, and a different protocol altogether is an
    /// <see cref="IEditorApi"/> of your own:
    ///
    /// <code>
    /// // the endpoints, somewhere else
    /// new EditorClient(routes: new EditorRoutes("/editor-api"));
    ///
    /// // your own calling logic
    /// new EditorClient(api: new MyEditorApi());
    /// </code>
    /// </summary>
    public sealed class EditorClient
    {
        private readonly IEditorApi _api;
        private readonly Action<IEditorState> _stateChanged;

        /// <summary>
        /// A monotonic counter stamped onto the PDF URL. The preview is of a
        /// document being typed into, and the one thing a cached page must never do
        /// is look like the current one.
        /// </summary>
        private int _revision;

        /// <summary>Open a client against an editor back-end.</summary>
        /// <param name="baseUrl">
        /// Where the API lives. Empty means "the origin this page was served from",
        /// which is the sample's case: the server hosts both. Ignored when
        /// <paramref name="routes"/> or <paramref name="api"/> is given, since
        /// those say where things are themselves.
        /// </param>
        /// <param name="onStateChanged">Called after every call that changes the document.</param>
        /// <param name="onFailed">
        /// Called with the server's own message when a request is refused. The
        /// messages are written for the author - the parser's line and column, the
        /// path that no longer resolves - so they are worth showing rather than
        /// logging. Ignored when <paramref name="api"/> is given, which reports its
        /// own failures.
        /// </param>
        /// <param name="routes">
        /// Where the endpoints are, when they are not where this library puts them.
        /// </param>
        /// <param name="api">
        /// How every call is made. The HTTP one over <paramref name="routes"/> when
        /// null, which is what an application talking to the shipped back end wants.
        /// </param>
        public EditorClient(string baseUrl = "", Action<IEditorState> onStateChanged = null,
            Action<string> onFailed = null, EditorRoutes routes = null, IEditorApi api = null)
        {
            _api = api ?? new HttpEditorApi(routes ?? new EditorRoutes("/api", baseUrl), onFailed);
            _stateChanged = onStateChanged;
        }

        /// <summary>How this client reaches the back end.</summary>
        public IEditorApi Api { get { return _api; } }

        /// <summary>The document as the server last reported it, or null before one is opened.</summary>
        public IEditorState State { get; private set; }

        /// <summary>The open document's identifier, or null.</summary>
        public string DocumentId { get; private set; }

        /// <summary>Every CSDB object the server offers.</summary>
        public Task<IDocumentSummary[]> ListAsync()
        {
            return _api.ListAsync();
        }

        /// <summary>
        /// The components an author can add to the open document, each with the
        /// block it projects as.
        ///
        /// <b>Per document, and it has to be.</b> What may be inserted is a property
        /// of the object, not of the stylesheet: a procedure takes nearly everything
        /// the vocabulary can build, and a publication module - whose content is
        /// entries and references - takes almost none of it. Asking once and keeping
        /// the answer offers an author a rail of components that refuse every drop
        /// without saying why, which reads as a broken editor rather than as the
        /// schema doing its job.
        ///
        /// With no document open this is the whole catalogue, which is what a
        /// front-end wanting to show the vocabulary itself asks for.
        /// </summary>
        public Task<IPaletteEntry[]> PaletteAsync()
        {
            return _api.PaletteAsync(DocumentId);
        }

        /// <summary>Open a document, replacing whatever was open.</summary>
        public Task<IEditorState> OpenAsync(string id)
        {
            DocumentId = id;
            return Adopt(_api.ReadAsync(id));
        }

        /// <summary>Re-read the open document without changing it.</summary>
        public Task<IEditorState> RefreshAsync()
        {
            return Adopt(_api.ReadAsync(DocumentId));
        }

        /// <summary>Apply a batch of edits as one undoable step.</summary>
        public Task<IEditorState> ApplyAsync(params EditCommand[] commands)
        {
            return Adopt(_api.ApplyAsync(DocumentId, commands));
        }

        /// <summary>Replace the whole source - what the code editor saves.</summary>
        public Task<IEditorState> SetXmlAsync(string xml)
        {
            return Adopt(_api.SetXmlAsync(DocumentId, xml));
        }

        /// <summary>Reverse the last edit.</summary>
        public Task<IEditorState> UndoAsync()
        {
            return Adopt(_api.SessionAsync(DocumentId, "undo"));
        }

        /// <summary>Reapply the last undone edit.</summary>
        public Task<IEditorState> RedoAsync()
        {
            return Adopt(_api.SessionAsync(DocumentId, "redo"));
        }

        /// <summary>Throw the session away and read the document from the CSDB again.</summary>
        public Task<IEditorState> RevertAsync()
        {
            return Adopt(_api.SessionAsync(DocumentId, "revert"));
        }

        /// <summary>Write the document out.</summary>
        public Task<IEditorState> SaveAsync()
        {
            return Adopt(_api.SessionAsync(DocumentId, "save"));
        }

        /// <summary>Check the document: well-formedness, business rules, and whether it can be laid out.</summary>
        public Task<ICheckReport> CheckAsync()
        {
            return _api.CheckAsync(DocumentId);
        }

        /// <summary>
        /// Where the open document's page can be fetched from, as of the last
        /// change. The revision is what makes a re-render a different URL, so the
        /// browser fetches the page the author has just changed rather than the one
        /// it already has.
        /// </summary>
        public string PdfUrl()
        {
            return _api.PdfUrl(DocumentId, _revision);
        }

        /// <summary>Subscribe to state changes for as long as the client lives.</summary>
        public event Action<IEditorState> StateChanged;

        /// <summary>
        /// Make what the back end answered the session's state, and tell everyone.
        ///
        /// Every call that can change the document goes through here, so there is
        /// one place where the open document, the revision and the subscribers are
        /// brought up to date - and an <see cref="IEditorApi"/> of someone else's
        /// gets that for free rather than having to remember it.
        /// </summary>
        private async Task<IEditorState> Adopt(Task<IEditorState> call)
        {
            IEditorState state = await call;

            if (state is object)
            {
                State = state;
                DocumentId = state.id;
                _revision++;

                if (_stateChanged is object) _stateChanged(state);
                if (StateChanged is object) StateChanged(state);
            }

            return state;
        }
    }

    /// <summary>A refused request, in the words the author needs to act on it.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IErrorResponse
    {
        /// <summary>What went wrong.</summary>
        string error { get; }
    }

    /// <summary>A batch of edits, applied as one undoable step.</summary>
    [ObjectLiteral]
    public class CommandsRequest
    {
        /// <summary>The edits, in order.</summary>
        public EditCommand[] commands;
    }

    /// <summary>The whole source, as the code editor holds it.</summary>
    [ObjectLiteral]
    public class XmlRequest
    {
        /// <summary>The document text.</summary>
        public string xml;
    }
}
