using System;
using System.Threading.Tasks;
using Tesserae;
using Transpose.Core;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace S1kdTools.Editor
{
    /// <summary>
    /// A complete editor for one CSDB object: a command bar over the WYSIWYG
    /// surface, with the document's state — dirty, saved, undoable — in front of
    /// the author rather than behind a menu.
    ///
    /// The bar carries three groups, and the grouping is the point:
    ///
    /// * <b>the document</b> — undo, redo, save, revert. Each labelled with what it
    ///   would actually do (<c>Undo insert warning</c>), because "undo" alone asks
    ///   the author to remember.
    /// * <b>the text</b> — bold, italic, subscript, superscript. Applied to the
    ///   selection through the browser's own editing commands, which is what makes
    ///   them work with the caret rather than with a model of where it is.
    /// * <b>the document's health</b> — a check, whose findings carry the path of
    ///   the element each is about, so clicking one lands the author on it.
    ///
    /// The bar is part of the library rather than the sample because these are the
    /// commands the editing model makes possible, and every host would otherwise
    /// write the same ones. A host that wants its own puts
    /// <see cref="S1kdEditorSurface"/> in its own chrome instead; the surface has no
    /// dependency on any of this.
    /// </summary>
    public sealed class S1kdEditor : IComponent
    {
        private readonly EditorClient _client;
        private readonly S1kdEditorSurface _surface;

        private readonly Button _undo;
        private readonly Button _redo;
        private readonly Button _save;
        private readonly Button _revert;
        private readonly Button _check;
        private readonly TextBlock _status;
        private readonly Stack _root;
        private readonly Stack _findings;

        /// <summary>Build an editor over a client.</summary>
        /// <param name="client">The session this editor drives.</param>
        /// <param name="surface">
        /// The surface to command, when the host is laying one out itself — a host
        /// showing the surface in one tab and a source editor in another needs the
        /// same surface instance in both places. A new one is built when null.
        /// </param>
        public S1kdEditor(EditorClient client, S1kdEditorSurface surface = null)
        {
            _client = client;
            _surface = surface ?? new S1kdEditorSurface(client);

            _undo = Button("Undo").SetIcon(UIcons.Undo).LessPadding().Disabled()
                .OnClick(() => _client.UndoAsync().FireAndForget());

            _redo = Button("Redo").SetIcon(UIcons.Redo).LessPadding().Disabled()
                .OnClick(() => _client.RedoAsync().FireAndForget());

            _save = Button("Save").SetIcon(UIcons.Disk).LessPadding().Primary().Disabled()
                .OnClick(() => SaveAsync().FireAndForget());

            _revert = Button("Revert").SetIcon(UIcons.RotateLeft).LessPadding().Disabled()
                .OnClick(() => RevertAsync().FireAndForget());

            _check = Button("Check").SetIcon(UIcons.ShieldCheck).LessPadding()
                .OnClick(() => CheckAsync().FireAndForget());

            _status = TextBlock("No data module open").Tiny().Secondary();
            _findings = VStack().WS().Collapse();

            _root = VStack().S().Children(
                CommandBar(),
                _findings,
                VStack().S().Children(_surface));

            _client.StateChanged += Reflect;
        }

        /// <inheritdoc/>
        public HTMLElement Render() => _root.Render();

        /// <summary>The surface this editor commands, for a host laying it out itself.</summary>
        public S1kdEditorSurface Surface => _surface;

        /// <summary>Open a document.</summary>
        public Task OpenAsync(string id) => _client.OpenAsync(id);

        /// <summary>
        /// Commit whatever is being typed, then save.
        ///
        /// The commit first is not a nicety: a save that wrote the document without
        /// the sentence the author was in the middle of would be a data loss the
        /// author could not see, since the surface would go on showing the sentence.
        /// </summary>
        public async Task SaveAsync()
        {
            await _surface.CommitPendingAsync();
            await _client.SaveAsync();

            if (_client.State is object && !_client.State.dirty)
            {
                Toast().Success("Saved", _client.State.fileName);
            }
        }

        /// <summary>Throw the session away and read the document from the CSDB again.</summary>
        public async Task RevertAsync()
        {
            if (!window.confirm("Discard every change made to this data module since it was opened?"))
            {
                return;
            }

            await _client.RevertAsync();
        }

        /// <summary>Commit, then check the document and show what came back.</summary>
        public async Task CheckAsync()
        {
            await _surface.CommitPendingAsync();

            ICheckReport report = await _client.CheckAsync();
            if (report is null)
            {
                return;
            }

            ShowFindings(report);
        }

        // --------------------------------------------------------------------
        // the bar
        // --------------------------------------------------------------------

        private IComponent CommandBar()
        {
            return HStack().WS().AlignItemsCenter().Class("s1kd-commandbar").PL(8).PR(8).PT(6).PB(6)
                .Children(
                    _undo, _redo,
                    Separator(),
                    _save, _revert,
                    Separator(),
                    FormatButton("Bold", UIcons.Bold, "bold"),
                    FormatButton("Italic", UIcons.Italic, "italic"),
                    FormatButton("Subscript", UIcons.Subscript, "subscript"),
                    FormatButton("Superscript", UIcons.Superscript, "superscript"),
                    Separator(),
                    _check,
                    Empty().Grow(),
                    _status);
        }

        private static IComponent Separator()
        {
            return Raw(Div(Att("s1kd-commandbar-sep"))).MR(6).ML(6);
        }

        /// <summary>
        /// A formatting button.
        ///
        /// <c>document.execCommand</c> is deprecated and is still the only thing in
        /// a browser that will apply a style to a selection the user made, honouring
        /// partial words, multiple ranges and the caret's own idea of where it is.
        /// Reimplementing that on top of the Range API is a project, and one whose
        /// bugs land in the middle of someone's sentence.
        ///
        /// <c>styleWithCSS</c> is turned off first so the browser writes
        /// <c>&lt;b&gt;</c> and <c>&lt;i&gt;</c> rather than styled spans — which is
        /// exactly what <see cref="RunCodec"/> reads back as bold and italic. A
        /// <c>mousedown</c> handler rather than a click, so the button never takes
        /// the focus off the text it is meant to be styling.
        /// </summary>
        private IComponent FormatButton(string label, UIcons icon, string command)
        {
            Button button = Button().SetIcon(icon).LessPadding().Tooltip(label);

            button.Render().addEventListener("mousedown", e =>
            {
                e.preventDefault();

                document.execCommand("styleWithCSS", false, "false");
                document.execCommand(command, false, null);
            });

            return button;
        }

        // --------------------------------------------------------------------
        // state
        // --------------------------------------------------------------------

        private void Reflect(IEditorState state)
        {
            if (state is null)
            {
                return;
            }

            _undo.Disabled(state.undo.depth == 0);
            _redo.Disabled(state.redo.depth == 0);
            _save.Disabled(!state.dirty);
            _revert.Disabled(!state.dirty);

            // What the button would do, not what it is called. "Undo" alone asks the
            // author to remember what they last did; the label is the answer.
            _undo.SetText(state.undo.label is null ? "Undo" : "Undo " + Lower(state.undo.label));
            _redo.SetText(state.redo.label is null ? "Redo" : "Redo " + Lower(state.redo.label));

            _status.Text = state.dirty
                ? state.fileName + " — unsaved changes"
                : state.savedAt is null
                    ? state.fileName
                    : state.fileName + " — saved";

            // Any edit invalidates the findings on screen: they name paths in a
            // document that has just been renumbered.
            _findings.Collapse();
        }

        private void ShowFindings(ICheckReport report)
        {
            _findings.Clear();

            ICheckFinding[] findings = report.findings;

            if (report.ok && (findings is null || findings.Length == 0))
            {
                _findings.Add(Banner("s1kd-findings-ok",
                    "This data module is well-formed and follows the business rules in " +
                    (report.brex ?? "the default BREX") + "."));
                _findings.Show();
                return;
            }

            for (var i = 0; i < findings.Length; i++)
            {
                _findings.Add(Finding(findings[i]));
            }

            _findings.Show();
        }

        private IComponent Finding(ICheckFinding finding)
        {
            string prefix = finding.source == "brex" ? "Business rule"
                          : finding.source == "render" ? "Presentation"
                          : "XML";

            IComponent row;

            if (string.IsNullOrEmpty(finding.path))
            {
                TextBlock text = TextBlock(prefix + ": " + finding.message).XSmall();
                row = finding.severity == "error" ? text.Danger() : text.Secondary();
            }
            else
            {
                // A finding that names an element is a place, not a sentence. Making
                // it a button is what turns "objectPath /dmodule[1]/…" from something
                // the author has to decode into somewhere they can go.
                row = Button(prefix + ": " + finding.message)
                    .NoBorder().LessPadding()
                    .Tooltip(finding.rule ?? finding.path)
                    .OnClick(() =>
                    {
                        if (!_surface.Reveal(finding.path))
                        {
                            Toast().Warning("That element is no longer in this data module.");
                        }
                    });
            }

            string className = finding.severity == "error"
                ? "s1kd-finding s1kd-finding-error"
                : "s1kd-finding";

            return HStack().WS().AlignItemsCenter().Class(className).Children(row);
        }

        private static IComponent Banner(string className, string message)
        {
            return HStack().WS().AlignItemsCenter().Class("s1kd-finding " + className)
                .Children(TextBlock(message).XSmall().Secondary());
        }

        /// <summary>
        /// The history label, lower-cased for the middle of a sentence — "Undo edit
        /// text" rather than "Undo Edit text". Only the first letter: the labels hold
        /// element names such as <c>Insert warning</c> that are already lower-case
        /// where they should be.
        /// </summary>
        private static string Lower(string label)
        {
            return string.IsNullOrEmpty(label)
                ? label
                : label.Substring(0, 1).ToLower() + label.Substring(1);
        }
    }
}
