using System;
using System.Threading.Tasks;
using Tesserae;
using Transpose.Core;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace S1kdTools.Editor.App
{
    /// <summary>
    /// The sample: one CSDB, one data module open, and three ways of looking at it.
    ///
    /// <code>
    ///   ┌ CSDB ────────┬ Slat actuation power control unit — Installation ─────────┐
    ///   │ · Procedure  │ [ Edit | Source | Page ]                                  │
    ///   │ · Descriptive├───────────────────────────────────────────────────────────┤
    ///   │ · Parts data │  [undo] [redo] [save] [B I x₂ x²] [check]                 │
    ///   │ · Fault iso  │                                                           │
    ///   │ …            │  the data module, drawn as its page and typed into        │
    ///   └──────────────┴───────────────────────────────────────────────────────────┘
    /// </code>
    ///
    /// <b>The three tabs are one document seen three ways, not three documents.</b>
    /// They share one <see cref="EditorClient"/>, so they share the server session
    /// behind it: an edit made on the surface is in the source pane's text and in
    /// the page's layout without any of them knowing the others exist. That is the
    /// whole architectural claim this sample is here to demonstrate, and it is why
    /// they are tabs rather than columns — the same document twice at half the width
    /// each is worse than either at full width, and a technical author reads a page
    /// of A4, not a strip of one.
    ///
    /// Switching tabs commits first (see <see cref="OnTabSelected"/>), so no view
    /// ever shows the document without the sentence the author is in the middle of.
    /// </summary>
    internal static class App
    {
        private const string EditTab = "edit";
        private const string SourceTab = "source";
        private const string PageTab = "page";

        private static EditorClient _client;
        private static S1kdEditorSurface _surface;
        private static S1kdEditor _editor;
        private static SourcePane _source;
        private static S1kdPdfPreview _preview;
        private static SegmentedPivot _pivot;
        private static Sidebar _sidebar;
        private static TextBlock _heading;
        private static TextBlock _subheading;

        private static string _current = EditTab;

        private static void Main()
        {
            document.body.style.overflow = "hidden";
            Theme.EnableMobileDetection(breakpoint: 768);

            // Served from the same origin as the API, so the base URL is empty. A
            // front-end hosted elsewhere passes the server's origin here and nothing
            // else changes.
            _client = new EditorClient(
                baseUrl: "",
                onFailed: message => Toast().Error("The server refused that", message));

            _surface = new S1kdEditorSurface(_client);
            _editor = new S1kdEditor(_client, _surface);
            _source = new SourcePane(_client);
            _preview = new S1kdPdfPreview(_client);

            _heading = TextBlock("No data module open").SemiBold().Ellipsis();
            _subheading = TextBlock("Pick one from the CSDB on the left").Tiny().Secondary().Ellipsis();

            _pivot = SegmentedPivot()
                // Every tab is cached. Monaco holds the author's cursor, selection
                // and undo history; the preview holds a laid-out document and the
                // page it was scrolled to. Rebuilding either on a tab switch throws
                // away exactly the state the switch exists to come back to.
                .SegmentedPivot(EditTab, SegmentTitle("Edit", UIcons.PenField),
                    () => VStack().S().Children(_editor), cached: true)
                .SegmentedPivot(SourceTab, SegmentTitle("Source", UIcons.FileCode),
                    () => VStack().S().Children(_source), cached: true)
                .SegmentedPivot(PageTab, SegmentTitle("Page", UIcons.FilePdf),
                    () => VStack().S().Children(_preview), cached: true)
                .OnNavigate((_, e) => OnTabSelected(e.TargetPivot));

            _sidebar = BuildSidebar();

            IComponent layout = Theme.IsMobileMode
                ? (IComponent)VStack().S().Children(_sidebar.WS(), Workspace().WS().H(1).Grow())
                : HStack().S().Children(_sidebar.HS(), Workspace().HS().W(1).Grow());

            MountToBody(layout);

            LoadCsdbAsync().FireAndForget();
        }

        /// <summary>
        /// The document's name over the three views of it.
        ///
        /// The heading is a band of its own rather than something inside the tabs,
        /// because which data module is open does not change when the author looks
        /// at it differently — and a title that redraws on every tab switch reads as
        /// if it might have.
        /// </summary>
        private static IComponent Workspace()
        {
            return VStack().S().Children(
                HStack().WS().AlignItemsCenter().Class("s1kd-app-head").PL(12).PR(12).PT(8).PB(8)
                    .Children(VStack().Grow().Children(_heading, _subheading)),
                _pivot.S());
        }

        private static Sidebar BuildSidebar()
        {
            var sidebar = Sidebar();

            if (Theme.IsMobileMode)
            {
                sidebar.AsNavbar();
            }

            sidebar.AddHeader(new SidebarText("header", "s1kd editor", "S1KD",
                textSize: TextSize.Large, textWeight: TextWeight.Bold));

            return sidebar;
        }

        /// <summary>
        /// Fill the sidebar with the CSDB and open the first data module.
        ///
        /// The list is what the server has, not a list compiled into the app: point
        /// the server at another folder and this is that CSDB.
        /// </summary>
        private static async Task LoadCsdbAsync()
        {
            IDocumentSummary[] documents = await _client.ListAsync();

            if (documents is null || documents.Length == 0)
            {
                Toast().Warning("There is nothing in the CSDB the server was pointed at.");
                return;
            }

            for (var i = 0; i < documents.Length; i++)
            {
                IDocumentSummary summary = documents[i];
                string id = summary.id;

                var item = new SidebarButton(id, IconFor(summary.objectType), summary.objectType)
                    .Tooltip(summary.title + "\n" + summary.code)
                    .OnClick(() => OpenAsync(id).FireAndForget());

                sidebarItems.Add(item);
                _sidebar.AddContent(item);
            }

            await OpenAsync(documents[0].id);
        }

        private static readonly System.Collections.Generic.List<SidebarButton> sidebarItems =
            new System.Collections.Generic.List<SidebarButton>();

        private static async Task OpenAsync(string id)
        {
            IEditorState state = await _client.OpenAsync(id);

            if (state is null)
            {
                return;
            }

            _heading.Text = state.title;
            _subheading.Text = state.objectType + " · " + state.code;

            for (var i = 0; i < sidebarItems.Count; i++)
            {
                sidebarItems[i].IsSelected = sidebarItems[i].Identifier == id;
            }

            // The preview is only asked for a layout if it is the tab on screen. It
            // is a real page render on the server, and spending one on a pane nobody
            // is looking at is the difference between a preview that feels instant
            // and one that does not.
            await _preview.SetVisibleAsync(_current == PageTab);
        }

        /// <summary>
        /// A tab switch, which is also the only moment the three views have to agree.
        ///
        /// Committing first is what makes them agree: whatever the author has typed
        /// into the block they are in becomes part of the document before the view
        /// that is about to read the document appears. Without it, the source pane
        /// would show the sentence as it was before they started, and the page would
        /// lay out the same stale text.
        /// </summary>
        private static void OnTabSelected(string tab)
        {
            _current = tab;
            SwitchAsync(tab).FireAndForget();
        }

        private static async Task SwitchAsync(string tab)
        {
            await _surface.CommitPendingAsync();

            if (tab == SourceTab && _source.HasPendingEdit)
            {
                Toast().Information("There is unapplied text in the source pane.");
            }

            await _preview.SetVisibleAsync(tab == PageTab);
        }

        /// <summary>
        /// A glyph per object type, so the CSDB list reads by shape as much as by
        /// label — which is what a sidebar of ten data modules with similar names
        /// needs to be navigable.
        /// </summary>
        private static UIcons IconFor(string objectType)
        {
            switch (objectType)
            {
                case "Procedure": return UIcons.ListCheck;
                case "Descriptive": return UIcons.Document;
                case "Illustrated parts data": return UIcons.Blueprint;
                case "Fault isolation": return UIcons.Bug;
                case "Crew": return UIcons.Users;
                case "Service bulletin": return UIcons.Megaphone;
                case "Checklist": return UIcons.ListCheck;
                case "Maintenance planning": return UIcons.Calendar;
                case "Process": return UIcons.Share;
                case "Front matter": return UIcons.Book;
                default: return UIcons.File;
            }
        }
    }
}
