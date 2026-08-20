using System;
using System.Threading.Tasks;
using Tesserae;
using Transpose.Core;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace S1kdTools.Editor
{
    /// <summary>
    /// The document in the editor, laid out as the page it will be published as.
    ///
    /// It shows what is <b>in the editor</b>, not what is on disk — that is the
    /// whole reason the pane exists. An author changing a warning wants to see the
    /// warning box move before deciding whether to keep the change, so every
    /// refresh asks the server to lay out the session's current document.
    ///
    /// The page is a real PDF in an iframe rather than an HTML approximation of
    /// one. The approximation is the surface next door; if this pane were another
    /// one, the author would have two guesses and no answer. What arrives here has
    /// been through the same XSL-FO stylesheets and the same layout engine that
    /// produce the published document, so a line that breaks badly here breaks
    /// badly in print.
    ///
    /// <b>Rendering is deferred until the pane is looked at.</b> A layout costs
    /// real time on the server, and a preview that re-rendered on every keystroke
    /// would spend all of it on pages nobody was looking at.
    /// <see cref="RefreshIfChangedAsync"/> is what a tab switch calls, so coming
    /// back to a page nobody has changed costs nothing and does not make the pane
    /// flash.
    /// </summary>
    public sealed class S1kdPdfPreview : IComponent
    {
        private readonly EditorClient _client;
        private readonly HTMLElement _root;
        private readonly HTMLElement _status;
        private readonly HTMLIFrameElement _frame;

        private string _shownRevision;
        private string _objectUrl;
        private bool _visible;

        /// <summary>Open a preview against a client.</summary>
        public S1kdPdfPreview(EditorClient client)
        {
            _client = client;

            _frame = document.createElement("iframe") as HTMLIFrameElement;
            _frame.className = "s1kd-pdf-frame";
            _frame.setAttribute("title", "Rendered page");
            _frame.style.display = "none";

            _status = Div(Att("s1kd-pdf-status", text: "Open a data module to see the page it makes."));
            _root = Div(Att("s1kd-pdf"), _frame, _status);

            // A change while the pane is hidden is not rendered, only remembered:
            // the next time it is shown, what is on screen no longer matches.
            _client.StateChanged += _ =>
            {
                if (_visible)
                {
                    RefreshIfChangedAsync().FireAndForget();
                }
            };
        }

        /// <inheritdoc/>
        public HTMLElement Render() => _root;

        /// <summary>
        /// Tell the preview whether anyone is looking at it. Showing it renders if
        /// the document has changed since the page on screen was made.
        /// </summary>
        public Task SetVisibleAsync(bool visible)
        {
            _visible = visible;
            return visible ? RefreshIfChangedAsync() : Task.CompletedTask;
        }

        /// <summary>Render, unless the page on screen is already of this revision.</summary>
        public Task RefreshIfChangedAsync()
        {
            string url = CurrentUrl();

            return url is null || url == _shownRevision
                ? Task.CompletedTask
                : RefreshAsync();
        }

        /// <summary>Lay the document out again, whatever is on screen.</summary>
        public async Task RefreshAsync()
        {
            string url = CurrentUrl();
            if (url is null)
            {
                return;
            }

            Status("Laying this data module out…");

            Response response;
            try
            {
                response = await fetch(url).ToTask();
            }
            catch (Exception e)
            {
                Status("The page could not be fetched: " + e.Message);
                return;
            }

            if (!response.ok)
            {
                // Almost always the module refusing to be laid out, with the reason
                // in the body. That belongs in front of the author.
                string body = await response.text().ToTask();
                Status(Reason(body, response.status));
                return;
            }

            Blob pdf = await response.blob().ToTask();

            // The blob is fetched and shown rather than the URL being handed
            // straight to the iframe, so a failed render is a message in this pane
            // rather than the browser's own error page inside it.
            Show(pdf);
            _shownRevision = url;
        }

        private string CurrentUrl()
        {
            return string.IsNullOrEmpty(_client.DocumentId) ? null : _client.PdfUrl();
        }

        private void Show(Blob pdf)
        {
            // The previous object URL is released as the new one is made. An author
            // previewing every few edits would otherwise leak a document's worth of
            // memory per render, and a long session is hundreds of them.
            if (!string.IsNullOrEmpty(_objectUrl))
            {
                URL.revokeObjectURL(_objectUrl);
            }

            _objectUrl = URL.createObjectURL(pdf);

            // #view=FitH: the pane is a column beside the editor, and a page at its
            // natural size in it is unreadable.
            _frame.src = _objectUrl + "#view=FitH&toolbar=1";
            _frame.style.display = "block";
            _status.style.display = "none";
        }

        private void Status(string message)
        {
            _status.textContent = message;
            _status.style.display = "block";
        }

        private static string Reason(string body, int status)
        {
            try
            {
                var parsed = Transpose.Core.es5.JSON.parse(body);
                var error = Transpose.Script.Write<IErrorResponse>("{0}", parsed);
                if (error is object && !string.IsNullOrEmpty(error.error))
                {
                    return "This module could not be laid out: " + error.error;
                }
            }
            catch (Exception)
            {
                // Not JSON; the status is all there is to say.
            }

            return "This module could not be laid out (the server answered " + status + ").";
        }
    }
}
