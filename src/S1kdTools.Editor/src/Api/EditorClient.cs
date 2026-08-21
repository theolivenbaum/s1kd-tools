using System;
using System.Threading.Tasks;
using Transpose;
using Transpose.Core;
using static Transpose.Core.dom;
using static Transpose.Core.es5;

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
    /// WYSIWYG surface, the source editor and the page preview each subscribe; an
    /// edit made in any of them reaches the other two by the same route it reaches
    /// the server, and none of them has to know the others exist.
    /// </summary>
    public sealed class EditorClient
    {
        private readonly string _baseUrl;
        private readonly Action<IEditorState> _stateChanged;
        private readonly Action<string> _failed;

        /// <summary>
        /// A monotonic counter stamped onto the PDF URL. The preview is of a
        /// document being typed into, and the one thing a cached page must never do
        /// is look like the current one.
        /// </summary>
        private int _revision;

        /// <summary>Open a client against an editor back-end.</summary>
        /// <param name="baseUrl">
        /// Where the API lives. Empty means "the origin this page was served from",
        /// which is the sample's case: the server hosts both.
        /// </param>
        /// <param name="onStateChanged">Called after every call that changes the document.</param>
        /// <param name="onFailed">
        /// Called with the server's own message when a request is refused. The
        /// messages are written for the author - the parser's line and column, the
        /// path that no longer resolves - so they are worth showing rather than
        /// logging.
        /// </param>
        public EditorClient(string baseUrl = "", Action<IEditorState> onStateChanged = null,
            Action<string> onFailed = null)
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            _stateChanged = onStateChanged;
            _failed = onFailed;
        }

        /// <summary>The document as the server last reported it, or null before one is opened.</summary>
        public IEditorState State { get; private set; }

        /// <summary>The open document's identifier, or null.</summary>
        public string DocumentId { get; private set; }

        /// <summary>
        /// Every CSDB object the server offers.
        ///
        /// <c>Script.Write</c> rather than a cast or a generic helper, here and at
        /// every other place a payload is named. The wire types are
        /// <c>[External]</c> declarations of a shape the parsed object already has,
        /// so there is nothing to convert — but a generic method would make the
        /// compiler pass a runtime type token for one of them, and an external type
        /// has no runtime type to pass. <c>Script.Write</c> emits the value and
        /// nothing else.
        /// </summary>
        public async Task<IDocumentSummary[]> ListAsync()
        {
            object parsed = await SendAsync("GET", _baseUrl + "/api/documents", null);
            return Script.Write<IDocumentSummary[]>("{0}", parsed);
        }

        /// <summary>
        /// The catalogue of components an author can add, each with the block it
        /// projects as.
        ///
        /// Not per-document: what may be inserted is a property of the editing
        /// stylesheet, so the palette asks once and keeps the answer.
        /// </summary>
        public async Task<IPaletteEntry[]> PaletteAsync()
        {
            object parsed = await SendAsync("GET", _baseUrl + "/api/palette", null);
            return Script.Write<IPaletteEntry[]>("{0}", parsed);
        }

        /// <summary>Open a document, replacing whatever was open.</summary>
        public Task<IEditorState> OpenAsync(string id)
        {
            DocumentId = id;
            return StateAsync("GET", "/documents/" + Escape(id), null);
        }

        /// <summary>Re-read the open document without changing it.</summary>
        public Task<IEditorState> RefreshAsync()
        {
            return StateAsync("GET", "/documents/" + Escape(DocumentId), null);
        }

        /// <summary>Apply a batch of edits as one undoable step.</summary>
        public Task<IEditorState> ApplyAsync(params EditCommand[] commands)
        {
            var request = new CommandsRequest { commands = commands };
            return StateAsync("POST", "/documents/" + Escape(DocumentId) + "/commands", request);
        }

        /// <summary>Replace the whole source - what the code editor saves.</summary>
        public Task<IEditorState> SetXmlAsync(string xml)
        {
            var request = new XmlRequest { xml = xml };
            return StateAsync("PUT", "/documents/" + Escape(DocumentId) + "/xml", request);
        }

        /// <summary>Reverse the last edit.</summary>
        public Task<IEditorState> UndoAsync()
        {
            return StateAsync("POST", "/documents/" + Escape(DocumentId) + "/undo", null);
        }

        /// <summary>Reapply the last undone edit.</summary>
        public Task<IEditorState> RedoAsync()
        {
            return StateAsync("POST", "/documents/" + Escape(DocumentId) + "/redo", null);
        }

        /// <summary>Throw the session away and read the document from the CSDB again.</summary>
        public Task<IEditorState> RevertAsync()
        {
            return StateAsync("POST", "/documents/" + Escape(DocumentId) + "/revert", null);
        }

        /// <summary>Write the document out.</summary>
        public Task<IEditorState> SaveAsync()
        {
            return StateAsync("POST", "/documents/" + Escape(DocumentId) + "/save", null);
        }

        /// <summary>Check the document: well-formedness, business rules, and whether it can be laid out.</summary>
        public async Task<ICheckReport> CheckAsync()
        {
            object parsed = await SendAsync("GET",
                _baseUrl + "/api/documents/" + Escape(DocumentId) + "/check", null);
            return Script.Write<ICheckReport>("{0}", parsed);
        }

        /// <summary>
        /// Where the open document's page can be fetched from, as of the last
        /// change. The revision in the query string is what makes a re-render a
        /// different URL, so the browser fetches the page the author has just
        /// changed rather than the one it already has.
        /// </summary>
        public string PdfUrl()
        {
            return _baseUrl + "/api/documents/" + Escape(DocumentId) + "/pdf?r=" + _revision;
        }

        /// <summary>Subscribe to state changes for as long as the client lives.</summary>
        public event Action<IEditorState> StateChanged;

        private async Task<IEditorState> StateAsync(string method, string path, object body)
        {
            object parsed = await SendAsync(method, _baseUrl + "/api" + path, body);
            IEditorState state = Script.Write<IEditorState>("{0}", parsed);

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

        /// <summary>
        /// One request, and the parsed body or null.
        ///
        /// Deliberately not generic. The result is a plain parsed JSON value that
        /// the caller names with <see cref="As{T}"/> — a cast that compiles to
        /// nothing, since the wire types are <c>[External]</c> declarations of the
        /// shape the payload already has. A generic async method here would make the
        /// compiler emit a type for the result and the runtime look it up, which is
        /// work to describe a type that has no representation at all.
        /// </summary>
        private async Task<object> SendAsync(string method, string url, object body)
        {
            var init = new RequestInit { method = method };

            if (body is object)
            {
                init.body = es5.JSON.stringify(body);
                init.headers = new Headers(new[] { new[] { "Content-Type", "application/json" } });
            }

            Response response = await fetch(url, init).ToTask();
            string text = await response.text().ToTask();

            if (!response.ok)
            {
                // The body is an ErrorResponse whenever the server produced it; a
                // proxy or a crashed process will not have that shape, so the raw
                // text is the fallback rather than an exception about JSON.
                Fail(ReadError(text, response.status));
                return null;
            }

            return text.Length == 0 ? null : es5.JSON.parse(text);
        }

        private void Fail(string message)
        {
            if (_failed is object)
            {
                _failed(message);
            }
            else
            {
                console.error("s1kd editor: " + message);
            }
        }

        private static string ReadError(string body, int status)
        {
            try
            {
                IErrorResponse parsed = Script.Write<IErrorResponse>("{0}", es5.JSON.parse(body));
                if (parsed is object && !string.IsNullOrEmpty(parsed.error))
                {
                    return parsed.error;
                }
            }
            catch (Exception)
            {
                // Not JSON. The status and whatever came back is all there is.
            }

            return body.Length > 0 ? body : "The server answered " + status + ".";
        }

        private static string Escape(string value)
        {
            return encodeURIComponent(value ?? "");
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
