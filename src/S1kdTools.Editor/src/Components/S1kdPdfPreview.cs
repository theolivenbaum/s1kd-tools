using System;
using System.Threading.Tasks;
using Tesserae;
using Tesserae.Pdf;
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
    /// refresh asks the server to lay out the session's current document, and what
    /// comes back has been through the same XSL-FO stylesheets and the same layout
    /// engine as the published one. A line that breaks badly here breaks badly in
    /// print.
    ///
    /// Drawn by <see cref="PdfJs.Viewer"/> rather than by handing the file to an
    /// <c>&lt;iframe&gt;</c>. An iframe gives the page to whatever PDF plugin the
    /// browser has: Chrome has one, headless Chrome downloads the file instead, and
    /// none of them can be scrolled to a page, fitted to the pane, or tested. The
    /// viewer also brings the things an author reaches for in a preview — text
    /// selection, search, links between data modules — that a canvas of our own
    /// would have had to grow one at a time.
    ///
    /// <b>Rendering is deferred until the pane is looked at.</b> A layout costs real
    /// time on the server, and a preview that re-rendered on every keystroke would
    /// spend all of it on pages nobody is looking at.
    /// <see cref="RefreshIfChangedAsync"/> is what a tab switch calls, so coming
    /// back to a page nobody has changed costs nothing and does not make the pane
    /// flash.
    /// </summary>
    public sealed class S1kdPdfPreview : IComponent
    {
        private readonly EditorClient _client;
        private readonly PdfViewer _viewer;
        private readonly Stack _root;
        private readonly Stack _bar;
        private readonly TextBlock _status;

        private string _shownRevision;
        private bool _visible;

        /// <summary>Open a preview against a client.</summary>
        public S1kdPdfPreview(EditorClient client)
        {
            _client = client;

            _viewer = PdfJs.Viewer()
                // A fit mode rather than a number, and this one: the pane is a column
                // beside an editor, so a page at its natural size is unreadable in it.
                // Tesserae.Pdf re-applies a fit mode when the container resizes, which
                // a zoom level would not survive.
                .FitWidth()
                .KeepFitOnResize()
                .OnDocumentLoaded(_ => Status(null))
                .OnPageChanged(page => Status(null))
                .OnError(error => Status("This module could not be laid out: " + error.Message));

            _status = TextBlock("Open a data module to see the page it makes.").Tiny().Secondary();

            // Tesserae.Pdf draws no toolbar of its own - deliberately, so it looks
            // like the application around it. These are the controls an author uses
            // in a preview beside an editor; page navigation is left to scrolling,
            // which is what a four-page data module wants.
            _bar = HStack().WS().AlignItemsCenter().Class("s1kd-commandbar").PL(8).PR(8).PT(6).PB(6)
                .Children(
                    Button().SetIcon(UIcons.ZoomOut).LessPadding().Tooltip("Zoom out")
                        .OnClick(() => _viewer.ZoomOut()),
                    Button().SetIcon(UIcons.ZoomIn).LessPadding().Tooltip("Zoom in")
                        .OnClick(() => _viewer.ZoomIn()),
                    Button().SetIcon(UIcons.ExpandArrows).LessPadding().Tooltip("Fit page width")
                        .OnClick(() => _viewer.FitWidth()),
                    Button().SetIcon(UIcons.Expand).LessPadding().Tooltip("Fit whole page")
                        .OnClick(() => _viewer.FitPage()),
                    Button().SetIcon(UIcons.Refresh).LessPadding().Tooltip("Lay out again")
                        .OnClick(() => RefreshAsync().FireAndForget()),
                    Empty().Grow(),
                    _status);

            _root = VStack().S().Class("s1kd-pdf").Children(
                _bar,
                VStack().S().Children(_viewer.S()));

            // A change while the pane is hidden is not rendered, only remembered: the
            // next time it is shown, what is on screen no longer matches.
            _client.StateChanged += _ =>
            {
                if (_visible)
                {
                    RefreshIfChangedAsync().FireAndForget();
                }
            };
        }

        /// <inheritdoc/>
        public HTMLElement Render() => _root.Render();

        /// <summary>The viewer, for a host that wants its own toolbar or its own search.</summary>
        public PdfViewer Viewer => _viewer;

        /// <summary>How many pages the document on screen has, or 0.</summary>
        public int PageCount => _viewer.PageCount;

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

        /// <summary>
        /// Lay the document out again, whatever is on screen.
        ///
        /// Nothing is fetched here. The URL carries the session's revision, so
        /// pointing the viewer at it is what makes the request — and pointing it at
        /// the same URL twice would be a no-op, which is why the revision is part of
        /// it rather than a header.
        /// </summary>
        public Task RefreshAsync()
        {
            string url = CurrentUrl();
            if (url is null)
            {
                return Task.CompletedTask;
            }

            Status("Laying this data module out…");
            _viewer.Url(url);
            _shownRevision = url;

            return Task.CompletedTask;
        }

        private string CurrentUrl()
        {
            return string.IsNullOrEmpty(_client.DocumentId) ? null : _client.PdfUrl();
        }

        /// <summary>
        /// The line beside the zoom controls: a message while something is happening
        /// or has gone wrong, and the page count once the document is up. Null means
        /// "say where we are", which is what the viewer's own events ask for.
        /// </summary>
        private void Status(string message)
        {
            if (message is object)
            {
                _status.Text = message;
                return;
            }

            int pages = _viewer.PageCount;
            _status.Text = pages <= 0 ? "" : pages == 1 ? "1 page" : pages + " pages";
        }
    }
}
