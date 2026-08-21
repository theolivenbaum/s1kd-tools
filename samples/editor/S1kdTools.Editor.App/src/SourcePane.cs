using System;
using System.Threading.Tasks;
using Tesserae;
using Tesserae.Monaco;
using Transpose.Core;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace S1kdTools.Editor.App
{
    /// <summary>
    /// The data module as its source, in Monaco.
    ///
    /// The source pane and the editing surface are the same document seen from
    /// opposite ends, and this is the end where an author does the things a
    /// structured editor is bad at: paste a block from another module, fix
    /// something the projection does not offer, read what an element actually says.
    ///
    /// <b>It writes through the same session as the surface.</b> Apply sends the
    /// whole text to <c>PUT …/xml</c>, which parses it, makes it the document and
    /// re-projects — so the surface next door is showing the author's hand-edited
    /// XML a moment later, and one undo takes it back. The alternative, letting the
    /// source pane own a copy that is reconciled later, is the drift the whole
    /// design avoids.
    ///
    /// <b>Incoming changes do not overwrite an edit in progress.</b> An edit made on
    /// the surface refreshes this pane, but only while the author is not typing in
    /// it: replacing the text under someone's caret because a warning box moved in
    /// another pane is the one behaviour that would make this pane untrustworthy.
    /// The Apply button says so instead.
    /// </summary>
    internal sealed class SourcePane : IComponent
    {
        private readonly EditorClient _client;
        private readonly CodeEditor _editor;
        private readonly Button _apply;
        private readonly Button _revert;
        private readonly TextBlock _status;
        private readonly Stack _root;

        /// <summary>What the server last sent, to tell an author's edit from a redraw.</summary>
        private string _serverText = "";

        private bool _editing;

        public SourcePane(EditorClient client)
        {
            _client = client;

            _editor = MonacoEditor.Editor()
                .SetLanguage("xml")
                .WordWrap();

            _editor.OnChanged(() =>
            {
                bool changed = _editor.Text != _serverText;
                _editing = changed;
                _apply.Disabled(!changed);
                _revert.Disabled(!changed);
                _status.Text = changed
                    ? "Edited here — not applied to the document yet"
                    : "In step with the document";
            });

            _apply = Button("Apply to document").SetIcon(UIcons.Check).LessPadding().Primary().Disabled()
                .OnClick(() => ApplyAsync().FireAndForget());

            _revert = Button("Discard").SetIcon(UIcons.RotateLeft).LessPadding().Disabled()
                .OnClick(() =>
                {
                    _editing = false;
                    Load(_serverText);
                });

            _status = TextBlock("No data module open").Tiny().Secondary();

            _root = VStack().S().Children(
                HStack().WS().AlignItemsCenter().Class("s1kd-commandbar").PL(8).PR(8).PT(6).PB(6)
                    .Children(_apply, _revert, Empty().Grow(), _status),
                VStack().S().Children(_editor.WS().HS()));

            _client.StateChanged += OnStateChanged;
        }

        public HTMLElement Render() => _root.Render();

        /// <summary>Whether the author has typed something here that is not in the document.</summary>
        public bool HasPendingEdit => _editing;

        /// <summary>
        /// Send the text as the document.
        ///
        /// A refusal — the text is not well-formed — leaves the pane exactly as it
        /// is, because the author's next move is to fix the line the parser named
        /// and they cannot do that if their text has been taken away. The client's
        /// failure handler shows the message.
        /// </summary>
        public async Task ApplyAsync()
        {
            if (!_editing)
            {
                return;
            }

            await _client.SetXmlAsync(_editor.Text);
        }

        private void OnStateChanged(IEditorState state)
        {
            if (state is null)
            {
                return;
            }

            _serverText = state.xml ?? "";

            if (_editing && _editor.Text != _serverText)
            {
                // Something else changed the document while there is unapplied text
                // here. Say so; do not take the author's text away.
                _status.Text = "The document changed elsewhere — applying will overwrite it";
                return;
            }

            Load(_serverText);
        }

        private void Load(string xml)
        {
            _editor.SetText(xml);
            _editing = false;
            _apply.Disabled();
            _revert.Disabled();
            _status.Text = "In step with the document";
        }
    }
}
