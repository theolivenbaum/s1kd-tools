using System;
using System.Threading.Tasks;
using Transpose;
using Transpose.Core;
using static Transpose.Core.dom;
using static Transpose.Core.es5;

namespace S1kdTools.Editor
{
    /// <summary>
    /// <see cref="IEditorApi"/> over HTTP: the endpoints
    /// <c>S1kdTools.Editor.Server</c> maps, reached with fetch.
    ///
    /// The default, and the one nearly every application wants. Where the endpoints
    /// live is <see cref="EditorRoutes"/>, so moving them is a constructor argument
    /// rather than an implementation of anything:
    ///
    /// <code>
    /// var client = new EditorClient(routes: new EditorRoutes("/editor-api"));
    /// </code>
    ///
    /// Subclass to put something around the wire itself - a bearer token on every
    /// request, a retry, a trace header - by overriding <see cref="SendAsync"/>.
    /// Replace <see cref="IEditorApi"/> outright when the calls are not these calls.
    /// </summary>
    public class HttpEditorApi : IEditorApi
    {
        private readonly EditorRoutes _routes;
        private readonly Action<string> _failed;

        /// <param name="routes">Where the endpoints are. The library's own when null.</param>
        /// <param name="onFailed">
        /// Called with the server's own message when a request is refused. The
        /// messages are written for the author - the parser's line and column, the
        /// path that no longer resolves - so they are worth showing rather than
        /// logging.
        /// </param>
        public HttpEditorApi(EditorRoutes routes = null, Action<string> onFailed = null)
        {
            _routes = routes ?? new EditorRoutes();
            _failed = onFailed;
        }

        /// <summary>Where the endpoints are.</summary>
        public EditorRoutes Routes { get { return _routes; } }

        /// <inheritdoc/>
        public async Task<IDocumentSummary[]> ListAsync()
        {
            object parsed = await SendAsync("GET", _routes.Documents(), null);
            return Script.Write<IDocumentSummary[]>("{0}", parsed);
        }

        /// <inheritdoc/>
        public async Task<IPaletteEntry[]> PaletteAsync(string documentId)
        {
            string url = documentId is null ? _routes.Palette() : _routes.Palette(documentId);
            object parsed = await SendAsync("GET", url, null);
            return Script.Write<IPaletteEntry[]>("{0}", parsed);
        }

        /// <inheritdoc/>
        public async Task<IEditorState> ReadAsync(string id)
        {
            object parsed = await SendAsync("GET", _routes.Document(id), null);
            return Script.Write<IEditorState>("{0}", parsed);
        }

        /// <inheritdoc/>
        public async Task<IEditorState> ApplyAsync(string id, EditCommand[] commands)
        {
            var request = new CommandsRequest { commands = commands };
            object parsed = await SendAsync("POST", _routes.Commands(id), request);
            return Script.Write<IEditorState>("{0}", parsed);
        }

        /// <inheritdoc/>
        public async Task<IEditorState> SetXmlAsync(string id, string xml)
        {
            var request = new XmlRequest { xml = xml };
            object parsed = await SendAsync("PUT", _routes.Xml(id), request);
            return Script.Write<IEditorState>("{0}", parsed);
        }

        /// <inheritdoc/>
        public async Task<IEditorState> SessionAsync(string id, string action)
        {
            object parsed = await SendAsync("POST", _routes.Session(id, action), null);
            return Script.Write<IEditorState>("{0}", parsed);
        }

        /// <inheritdoc/>
        public async Task<ICheckReport> CheckAsync(string id)
        {
            object parsed = await SendAsync("GET", _routes.Check(id), null);
            return Script.Write<ICheckReport>("{0}", parsed);
        }

        /// <inheritdoc/>
        public string PdfUrl(string id, int revision)
        {
            return _routes.Pdf(id, revision);
        }

        /// <summary>
        /// One request, and the parsed body or null.
        ///
        /// Deliberately not generic. The result is a plain parsed JSON value the
        /// caller names with <c>Script.Write</c> - which compiles to nothing, since
        /// the wire types are <c>[External]</c> declarations of the shape the
        /// payload already has. A generic async method here would make the compiler
        /// emit a type for the result and the runtime look it up, which is work to
        /// describe a type that has no representation at all.
        /// </summary>
        protected virtual async Task<object> SendAsync(string method, string url, object body)
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

        /// <summary>Report a refused request in the words the author needs to act on it.</summary>
        protected void Fail(string message)
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
    }
}
