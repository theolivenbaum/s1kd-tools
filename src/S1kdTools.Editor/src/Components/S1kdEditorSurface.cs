using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tesserae;
using Transpose.Core;
using static Tesserae.UI;
using static Transpose.Core.dom;

// dom.Range and System.Range are both in scope through the static import and the
// implicit usings; the selection API means the first.
using Range = Transpose.Core.dom.Range;

namespace S1kdTools.Editor
{
    /// <summary>
    /// The WYSIWYG surface: a CSDB object drawn as the page it will be, with every
    /// part of it that can be typed into being typed into in place.
    ///
    /// <b>The surface holds no document.</b> It draws whatever
    /// <see cref="EditorClient.State"/> currently is, and an edit is a command sent
    /// to the server followed by a redraw of what comes back. There is no
    /// client-side model of S1000D here, no local mutation, and so no way for what
    /// the author sees to drift from what would be saved — the two failure modes
    /// that make an in-browser XML editor untrustworthy. It costs a round trip per
    /// committed edit, which is what the commit rules below are about.
    ///
    /// <b>An edit is committed when the author leaves the block</b>, not as they
    /// type. Typing would mean redrawing under the caret, and the alternative -
    /// keeping a local copy so the redraw can be skipped - is the drift this design
    /// exists to avoid. So a block commits on blur, on Enter, and before any
    /// structural command; the redraw then happens while nobody is typing, and the
    /// caret is put back where the author moved it (see <see cref="RestoreFocus"/>).
    ///
    /// What the author gets from that: a paragraph they have half-rewritten is
    /// still theirs to abandon, the undo stack has one entry per thought rather
    /// than one per keystroke, and the source and page views beside them are never
    /// showing a half-typed word.
    /// </summary>
    public sealed class S1kdEditorSurface : IComponent
    {
        private const string PathAttribute = BlockRenderer.PathAttribute;

        private readonly EditorClient _client;
        private readonly HTMLElement _root;
        private readonly HTMLElement _page;

        /// <summary>
        /// The block the caret is in, and where in it. Recorded as the author moves
        /// rather than read at redraw time, because by then the element it was in no
        /// longer exists.
        /// </summary>
        private string _focusPath;
        private int _focusOffset;

        /// <summary>
        /// Set while a redraw is replacing the DOM, so the blur events that causes
        /// are not mistaken for the author leaving a block and do not each start a
        /// commit of their own.
        /// </summary>
        private bool _redrawing;

        /// <summary>Open a surface against a client. Nothing is drawn until the client has a document.</summary>
        public S1kdEditorSurface(EditorClient client)
        {
            _client = client;

            _page = Div(Att("s1kd-page"));
            _root = Div(Att("s1kd-surface"), _page);

            // The surface is one delegated listener per event rather than a handful
            // per block: a procedure runs to a few hundred blocks, and attaching
            // six listeners to each of them is most of the cost of drawing one.
            _root.addEventListener("focusin", OnFocusIn);
            _root.addEventListener("focusout", OnFocusOut);
            _root.addEventListener("keydown", OnKeyDown);
            _root.addEventListener("keyup", RecordCaret);
            _root.addEventListener("mouseup", RecordCaret);
            _root.addEventListener("click", OnClick);

            // Dropping a component out of the palette. The listeners are on the
            // surface rather than on each block for the same reason the others are:
            // a procedure runs to a few hundred blocks, and four listeners each is
            // most of the cost of drawing one.
            _root.addEventListener("dragover", OnDragOver);
            _root.addEventListener("dragleave", OnDragLeave);
            _root.addEventListener("drop", OnDrop);

            _client.StateChanged += Draw;
        }

        /// <summary>Raised when the author asks for something the host has to answer — currently nothing.</summary>
        public HTMLElement Render() => _root;

        /// <summary>Scroll to a block and put the caret in it, if it is still there.</summary>
        /// <param name="path">The block's path, as a check finding or a search result gives it.</param>
        /// <returns>Whether a block with that path is on the surface.</returns>
        public bool Reveal(string path)
        {
            HTMLElement block = BlockElement(path);
            if (block is null)
            {
                return false;
            }

            block.scrollIntoView(new ScrollIntoViewOptions { block = ScrollLogicalPosition.center });

            // A moment of highlight rather than a permanent mark: the author asked
            // to be brought here, and once they are looking at it the highlight is
            // only in the way.
            block.classList.add("s1kd-revealed");
            window.setTimeout(_ => block.classList.remove("s1kd-revealed"), 1600);

            HTMLElement text = Editable(block);
            if (text is object)
            {
                text.focus();
            }

            return true;
        }

        /// <summary>
        /// Commit whatever the author has in the block they are in, and wait for it.
        ///
        /// The host calls this before anything that reads the document from
        /// somewhere else — switching to the source view, rendering the page, saving
        /// — because otherwise those show the document without the sentence the
        /// author is in the middle of.
        /// </summary>
        public Task CommitPendingAsync()
        {
            HTMLElement editing = document.activeElement as HTMLElement;
            return editing is object && IsEditable(editing)
                ? CommitAsync(editing)
                : Task.CompletedTask;
        }

        // --------------------------------------------------------------------
        // drawing
        // --------------------------------------------------------------------

        private void Draw(IEditorState state)
        {
            _redrawing = true;

            try
            {
                _page.innerHTML = "";

                if (state is null || state.model is null)
                {
                    return;
                }

                _page.appendChild(PageHeader(state));

                IEditSection[] sections = state.model.sections;
                for (var i = 0; i < sections.Length; i++)
                {
                    _page.appendChild(Section(sections[i]));
                }
            }
            finally
            {
                _redrawing = false;
            }

            RestoreFocus();
        }

        private static HTMLElement PageHeader(IEditorState state)
        {
            HTMLElement header = Div(Att("s1kd-page-head"));
            header.appendChild(Div(Att("s1kd-page-type", text: state.objectType)));
            header.appendChild(Div(Att("s1kd-page-title", text: state.title)));
            header.appendChild(Div(Att("s1kd-page-code", text: state.code)));
            return header;
        }

        private HTMLElement Section(IEditSection section)
        {
            HTMLElement host = Div(Att("s1kd-section s1kd-section-" + section.key));
            host.appendChild(Div(Att("s1kd-section-head", text: section.label)));

            HTMLElement body = Div(Att("s1kd-section-body"));
            IEditBlock[] blocks = section.blocks;

            for (var i = 0; i < blocks.Length; i++)
            {
                body.appendChild(Block(blocks[i]));
            }

            host.appendChild(body);
            return host;
        }

        private HTMLElement Block(IEditBlock block)
        {
            return BlockRenderer.Draw(block, BlockRenderer.Mode.Interactive);
        }

        // --------------------------------------------------------------------
        // editing
        // --------------------------------------------------------------------

        private void OnFocusIn(Event e)
        {
            var target = e.target as HTMLElement;
            if (target is null)
            {
                return;
            }

            if (IsEditable(target) || target.tagName.ToLower() == "input" ||
                target.tagName.ToLower() == "select")
            {
                _focusPath = PathOf(target);
                _focusOffset = 0;
            }
        }

        private void OnFocusOut(Event e)
        {
            if (_redrawing)
            {
                return;
            }

            var target = e.target as HTMLElement;
            if (target is null)
            {
                return;
            }

            if (IsEditable(target))
            {
                CommitAsync(target).FireAndForget();
            }
            else if (target.tagName.ToLower() == "input")
            {
                CommitFieldAsync(target).FireAndForget();
            }
        }

        private void OnKeyDown(Event e)
        {
            var key = e as KeyboardEvent;
            var target = e.target as HTMLElement;

            if (key is null || target is null)
            {
                return;
            }

            if (target.tagName.ToLower() == "select" || target.tagName.ToLower() == "input")
            {
                if (key.key == "Enter")
                {
                    key.preventDefault();
                    target.blur();
                }
                return;
            }

            if (!IsEditable(target))
            {
                return;
            }

            if (key.key == "Enter" && !key.shiftKey)
            {
                // Enter makes another one of whatever this is. Shift+Enter is left
                // to the browser, which is the escape hatch for an author who wants
                // a break inside the paragraph rather than a new one.
                key.preventDefault();
                SplitAsync(target).FireAndForget();
                return;
            }

            if (key.key == "Escape")
            {
                // Abandon: redraw from the server's copy, which is the text as it was
                // before the author started typing into this block.
                key.preventDefault();
                _redrawing = true;
                RunCodec.Write(target, BlockOf(target)?.runs);
                _redrawing = false;
                target.blur();
            }
        }

        private void RecordCaret(Event e)
        {
            var target = e.target as HTMLElement;
            if (target is object && IsEditable(target))
            {
                _focusPath = PathOf(target);
                _focusOffset = CaretOffset(target);
            }
        }

        private void OnClick(Event e)
        {
            var target = e.target as HTMLElement;
            if (target is null)
            {
                return;
            }

            HTMLElement button = Closest(target, "s1kd-gutter-button");
            if (button is null)
            {
                return;
            }

            e.preventDefault();

            string action = button.getAttribute("data-action");
            string path = button.getAttribute("data-target");

            if (action == "insert")
            {
                ShowInsertMenuAsync(button, path).FireAndForget();
                return;
            }

            GutterAsync(action, path).FireAndForget();
        }

        /// <summary>
        /// Offer everything that may go here, rather than guessing.
        ///
        /// The projection knows what the schema allows beside a block and inside it,
        /// and an author reaching for the plus wants a warning about as often as they
        /// want another paragraph. Picking the first option for them would make the
        /// button useless for everything else, and a button that always inserts a
        /// paragraph is a button that teaches the author the editor cannot do more.
        ///
        /// Siblings first, then children under a divider, because "beside this" is
        /// the common case and "inside this" is the one a container needs.
        /// </summary>
        private async Task ShowInsertMenuAsync(HTMLElement button, string path)
        {
            await CommitPendingAsync();

            IEditBlock block = FindBlock(path);
            if (block is null)
            {
                return;
            }

            var items = new List<ContextMenu.Item>();

            IInsertOption[] siblings = block.insertSiblings;
            if (siblings is object)
            {
                for (var i = 0; i < siblings.Length; i++)
                {
                    items.Add(InsertItem(siblings[i], path, EditPositions.After));
                }
            }

            IInsertOption[] children = block.insertChildren;
            if (children is object && children.Length > 0)
            {
                items.Add(ContextMenuItem("Inside this " + Readable(block.element)).Header());

                for (var i = 0; i < children.Length; i++)
                {
                    items.Add(InsertItem(children[i], path, EditPositions.LastChild));
                }
            }

            if (items.Count == 0)
            {
                return;
            }

            ContextMenu().Items(items.ToArray()).ShowFor(button);
        }

        private ContextMenu.Item InsertItem(IInsertOption option, string path, string position)
        {
            string element = option.element;

            return ContextMenuItem(option.label).OnClick(() =>
            {
                // The caret follows only when the new element is the same kind as the
                // one it was put beside, because that is the only case where its path
                // can be worked out rather than guessed at.
                _focusPath = position == EditPositions.After && element == LeafElementOf(path)
                    ? NextSiblingPath(path)
                    : null;
                _focusOffset = 0;

                _client.ApplyAsync(EditCommand.Insert(path, position, element)).FireAndForget();
            });
        }

        /// <summary>An element name as a menu says it: <c>proceduralStep</c> to "step".</summary>
        private static string Readable(string element)
        {
            switch (element)
            {
                case "proceduralStep":
                case "isolationStep":
                case "crewDrillStep": return "step";
                case "levelledPara": return "section";
                case "para": return "paragraph";
                case "listItem": return "list item";
                default: return element;
            }
        }

        private static string LeafElementOf(string path)
        {
            string[] steps = (path ?? "").Split('/');
            if (steps.Length == 0)
            {
                return "";
            }

            string last = steps[steps.Length - 1];
            int bracket = last.IndexOf('[');
            return bracket < 0 ? last : last.Substring(0, bracket);
        }

        /// <summary>Send a block's text, if the author changed it.</summary>
        private async Task CommitAsync(HTMLElement text)
        {
            IEditBlock block = BlockOf(text);
            if (block is null || !RunCodec.Differs(text, block.runs))
            {
                return;
            }

            await _client.ApplyAsync(EditCommand.SetText(block.path, RunCodec.Read(text)));
        }

        /// <summary>Send an attribute field's value, if the author changed it.</summary>
        private async Task CommitFieldAsync(HTMLElement input)
        {
            IEditBlock block = BlockOf(input);
            string value = ValueOf(input);

            if (block is null || value == (block.value ?? ""))
            {
                return;
            }

            await _client.ApplyAsync(EditCommand.SetAttr(block.path, block.attrName, value));
        }

        /// <summary>
        /// Enter: commit what is in the block, then make another one after it and put
        /// the caret in it.
        ///
        /// Both edits go in one batch so they are one step on the undo stack — an
        /// author who presses Enter and then thinks better of it expects one undo,
        /// not two.
        /// </summary>
        private async Task SplitAsync(HTMLElement text)
        {
            IEditBlock block = BlockOf(text);
            if (block is null)
            {
                return;
            }

            var commands = RunCodec.Differs(text, block.runs)
                ? new[]
                {
                    EditCommand.SetText(block.path, RunCodec.Read(text)),
                    EditCommand.Insert(block.path, EditPositions.After, block.element),
                }
                : new[] { EditCommand.Insert(block.path, EditPositions.After, block.element) };

            // The new element is the same name as the one it follows, so its path is
            // that path with the last predicate stepped on. Worked out rather than
            // asked for: the server answers with the whole projection, and hunting
            // for "the block that was not there before" would be guesswork where
            // this is arithmetic.
            _focusPath = NextSiblingPath(block.path);
            _focusOffset = 0;

            await _client.ApplyAsync(commands);
        }

        private async Task GutterAsync(string action, string path)
        {
            // Whatever the author was typing counts, and counts first: a delete that
            // discarded an uncommitted sentence in another block would be a
            // surprise, and a move that reordered stale text would be worse.
            await CommitPendingAsync();

            IEditBlock block = FindBlock(path);
            if (block is null)
            {
                return;
            }

            switch (action)
            {
                case "up":
                    _focusPath = null;
                    await _client.ApplyAsync(EditCommand.Move(path, MoveDirections.Up));
                    break;

                case "down":
                    _focusPath = null;
                    await _client.ApplyAsync(EditCommand.Move(path, MoveDirections.Down));
                    break;

                case "delete":
                    _focusPath = null;
                    await _client.ApplyAsync(EditCommand.Delete(path));
                    break;
            }
        }

        // --------------------------------------------------------------------
        // dropping a component
        // --------------------------------------------------------------------

        /// <summary>
        /// Add a palette component without dragging it: after the block the author
        /// was last in, or inside it when that is the only place it can go.
        /// </summary>
        /// <returns>Whether there was anywhere for it to go.</returns>
        public async Task<bool> InsertFromPaletteAsync(string element)
        {
            await CommitPendingAsync();

            IEditBlock target = FindBlock(_focusPath);

            if (target is null)
            {
                // Nothing has been typed into yet. The last block of the content is
                // where an author adding their first component means, far more often
                // than the first one.
                target = LastContentBlock();
            }

            Drop drop = Where(target, element, after: true);

            if (drop is null)
            {
                return false;
            }

            await ApplyDropAsync(drop);
            return true;
        }

        private void OnDragOver(Event e)
        {
            if (PaletteDrag.Element is null)
            {
                return;
            }

            Drop drop = DropAt(e as DragEvent);
            Highlight(drop);

            if (drop is null)
            {
                return;
            }

            // Calling preventDefault on dragover is what makes an element a drop
            // target at all. Not calling it is how a place a component may not go
            // refuses it, with the browser's own "no" cursor rather than a message.
            e.preventDefault();

            var drag = e as DragEvent;
            if (drag is object && drag.dataTransfer is object)
            {
                drag.dataTransfer.dropEffect = "copy";
            }
        }

        private void OnDragLeave(Event e)
        {
            // Only when the pointer has left the surface, not when it crosses from
            // one block to the next - which fires dragleave for the block behind it
            // and would make the insertion line flicker on every move.
            var target = e.target as HTMLElement;
            if (target is object && target == _root)
            {
                Highlight(null);
            }
        }

        private void OnDrop(Event e)
        {
            string element = PaletteDrag.Element;
            if (element is null)
            {
                return;
            }

            e.preventDefault();

            Drop drop = DropAt(e as DragEvent);
            Highlight(null);
            PaletteDrag.End();

            if (drop is object)
            {
                ApplyDropAsync(drop).FireAndForget();
            }
        }

        /// <summary>Where the pointer currently is, as a place a component could go.</summary>
        private Drop DropAt(DragEvent e)
        {
            if (e is null)
            {
                return null;
            }

            HTMLElement block = Closest(document.elementFromPoint(e.clientX, e.clientY) as HTMLElement,
                "s1kd-block");

            IEditBlock target = block is null ? null : FindBlock(block.getAttribute(PathAttribute));
            if (target is null)
            {
                return null;
            }

            // Above or below the block's midpoint - the same gesture every list
            // editor uses, and the reason the insertion line is drawn on an edge
            // rather than around the block.
            DOMRect box = block.getBoundingClientRect().As<DOMRect>();
            bool after = e.clientY > box.top + (box.height / 2);

            return Where(target, PaletteDrag.Element, after);
        }

        /// <summary>
        /// Decide where a component would land relative to a block, or null when it
        /// may not go there.
        ///
        /// Beside it if the schema allows it beside it; otherwise inside it, which is
        /// what makes dropping a step onto a container work without the author having
        /// to find its last child. Both answers come from the projection - the
        /// front-end is not deciding what S1000D permits, it is reading it.
        /// </summary>
        private Drop Where(IEditBlock target, string element, bool after)
        {
            if (target is null || element is null)
            {
                return null;
            }

            if (Offers(target.insertSiblings, element))
            {
                return new Drop(target, element,
                    after ? EditPositions.After : EditPositions.Before);
            }

            if (Offers(target.insertChildren, element))
            {
                return new Drop(target, element, EditPositions.LastChild);
            }

            return null;
        }

        private static bool Offers(IInsertOption[] options, string element)
        {
            if (options is null)
            {
                return false;
            }

            for (var i = 0; i < options.Length; i++)
            {
                if (options[i].element == element)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task ApplyDropAsync(Drop drop)
        {
            // The caret follows a component that landed beside one of its own kind,
            // because that is the only case where its path can be worked out rather
            // than guessed at - and it is the case where the author is about to type
            // into it.
            _focusPath = drop.Position == EditPositions.After && drop.Element == drop.Target.element
                ? NextSiblingPath(drop.Target.path)
                : null;
            _focusOffset = 0;

            await _client.ApplyAsync(
                EditCommand.Insert(drop.Target.path, drop.Position, drop.Element));
        }

        /// <summary>
        /// Draw the insertion line, and only there.
        ///
        /// The previous one is cleared by query rather than by remembering it: a
        /// redraw between two pointer moves would have thrown away the element the
        /// class was on, and a line left behind on a block nothing will land on is
        /// worse than no line at all.
        /// </summary>
        private void Highlight(Drop drop)
        {
            NodeList marked = _page.querySelectorAll(".s1kd-drop-before, .s1kd-drop-after, .s1kd-drop-into");
            for (uint i = 0; i < marked.length; i++)
            {
                var element = marked[i] as HTMLElement;
                element.classList.remove("s1kd-drop-before");
                element.classList.remove("s1kd-drop-after");
                element.classList.remove("s1kd-drop-into");
            }

            if (drop is null)
            {
                return;
            }

            HTMLElement block = BlockElement(drop.Target.path);
            if (block is null)
            {
                return;
            }

            block.classList.add(
                drop.Position == EditPositions.Before ? "s1kd-drop-before" :
                drop.Position == EditPositions.After ? "s1kd-drop-after" : "s1kd-drop-into");
        }

        /// <summary>The last block of the content section, for a palette click with no caret.</summary>
        private IEditBlock LastContentBlock()
        {
            IEditorState state = _client.State;

            if (state is null || state.model is null)
            {
                return null;
            }

            IEditSection[] sections = state.model.sections;
            for (var i = sections.Length - 1; i >= 0; i--)
            {
                IEditBlock[] blocks = sections[i].blocks;
                if (blocks is object && blocks.Length > 0)
                {
                    return Deepest(blocks[blocks.Length - 1]);
                }
            }

            return null;
        }

        private static IEditBlock Deepest(IEditBlock block)
        {
            IEditBlock[] children = block.blocks;
            return children is object && children.Length > 0
                ? Deepest(children[children.Length - 1])
                : block;
        }

        /// <summary>A place a component would land: which block, and on which side of it.</summary>
        private sealed class Drop
        {
            internal Drop(IEditBlock target, string element, string position)
            {
                Target = target;
                Element = element;
                Position = position;
            }

            internal IEditBlock Target { get; }
            internal string Element { get; }
            internal string Position { get; }
        }

        // --------------------------------------------------------------------
        // finding things
        // --------------------------------------------------------------------

        /// <summary>The block the DOM element belongs to, in the state as it stands now.</summary>
        private IEditBlock BlockOf(HTMLElement element)
        {
            return FindBlock(PathOf(element));
        }

        private IEditBlock FindBlock(string path)
        {
            IEditorState state = _client.State;

            if (string.IsNullOrEmpty(path) || state is null || state.model is null)
            {
                return null;
            }

            IEditSection[] sections = state.model.sections;
            for (var i = 0; i < sections.Length; i++)
            {
                IEditBlock found = Search(sections[i].blocks, path);
                if (found is object)
                {
                    return found;
                }
            }

            return null;
        }

        private static IEditBlock Search(IEditBlock[] blocks, string path)
        {
            if (blocks is null)
            {
                return null;
            }

            for (var i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].path == path)
                {
                    return blocks[i];
                }

                IEditBlock found = Search(blocks[i].blocks, path);
                if (found is object)
                {
                    return found;
                }
            }

            return null;
        }

        private HTMLElement BlockElement(string path)
        {
            return string.IsNullOrEmpty(path)
                ? null
                : _page.querySelector("[" + PathAttribute + "=\"" + CssEscape(path) + "\"]") as HTMLElement;
        }

        private static HTMLElement Editable(HTMLElement block)
        {
            return block?.querySelector(".s1kd-text") as HTMLElement;
        }

        private static string PathOf(HTMLElement element)
        {
            HTMLElement block = Closest(element, "s1kd-block");
            return block?.getAttribute(PathAttribute);
        }

        private static bool IsEditable(HTMLElement element)
        {
            string className = element.className ?? "";
            return className.Contains("s1kd-text");
        }

        /// <summary>
        /// The nearest ancestor carrying a class, self included.
        ///
        /// Spelled out rather than <c>Element.closest</c> with a selector, because
        /// the values involved — a class name here, an XPath in
        /// <see cref="BlockElement"/> — would have to be escaped into a selector,
        /// and one of them is document content.
        /// </summary>
        private static HTMLElement Closest(HTMLElement element, string className)
        {
            for (HTMLElement current = element; current is object; current = current.parentElement)
            {
                string names = current.className ?? "";
                if (names.Contains(className))
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>
        /// An XPath inside a CSS attribute selector. Only the quote and the backslash
        /// can end the string; a block path holds neither, but it is built from
        /// element names that came out of a file.
        /// </summary>
        private static string CssEscape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// The path of the element that would follow <paramref name="path"/> among
        /// its same-named siblings: the last positional predicate, stepped on.
        /// </summary>
        internal static string NextSiblingPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith("]"))
            {
                return null;
            }

            int open = path.LastIndexOf('[');
            if (open < 0)
            {
                return null;
            }

            string index = path.Substring(open + 1, path.Length - open - 2);
            int position;

            return int.TryParse(index, out position)
                ? path.Substring(0, open + 1) + (position + 1) + "]"
                : null;
        }

        // --------------------------------------------------------------------
        // the caret
        // --------------------------------------------------------------------

        /// <summary>How many characters of the block's text sit before the caret.</summary>
        private static int CaretOffset(HTMLElement text)
        {
            Selection selection = window.getSelection();
            if (selection is null || selection.rangeCount == 0)
            {
                return 0;
            }

            Range range = selection.getRangeAt(0);
            Range measured = range.cloneRange();
            measured.selectNodeContents(text);
            measured.setEnd(range.endContainer, range.endOffset);
            return measured.toString().Length;
        }

        /// <summary>
        /// Put the caret back after a redraw.
        ///
        /// The element it was in is gone — the whole page was rebuilt — so it is
        /// found again by path and the offset is walked back through the new text
        /// nodes. A block that no longer exists (the author deleted it, or a
        /// structural command moved things) simply takes no focus, which is why the
        /// gutter commands clear the path before they run.
        /// </summary>
        private void RestoreFocus()
        {
            if (string.IsNullOrEmpty(_focusPath))
            {
                return;
            }

            HTMLElement text = Editable(BlockElement(_focusPath));
            if (text is null)
            {
                return;
            }

            text.focus();
            PlaceCaret(text, _focusOffset);
        }

        private static void PlaceCaret(HTMLElement text, int offset)
        {
            Selection selection = window.getSelection();
            if (selection is null)
            {
                return;
            }

            Range range = document.createRange();
            int remaining = offset;
            Node found = null;

            // Depth-first over the text nodes, spending the offset as it goes. The
            // first node that can hold what is left is where the caret belongs.
            void Walk(Node node)
            {
                if (found is object)
                {
                    return;
                }

                if (node.nodeType == 3)
                {
                    int length = node.nodeValue.Length;
                    if (remaining <= length)
                    {
                        found = node;
                        return;
                    }
                    remaining -= length;
                    return;
                }

                NodeList children = node.childNodes;
                for (uint i = 0; i < children.length; i++)
                {
                    Walk(children[i]);
                    if (found is object)
                    {
                        return;
                    }
                }
            }

            Walk(text);

            if (found is object)
            {
                range.setStart(found, (uint)remaining);
            }
            else
            {
                // The block is shorter than it was, or empty. The end of it is the
                // only sensible place left.
                range.selectNodeContents(text);
                range.collapse(false);
            }

            range.collapse(true);
            selection.removeAllRanges();
            selection.addRange(range);
        }

        private static string ValueOf(HTMLElement input)
        {
            var box = input as HTMLInputElement;
            if (box is object)
            {
                return box.value ?? "";
            }

            var select = input as HTMLSelectElement;
            return select is object ? select.value ?? "" : "";
        }
    }
}
