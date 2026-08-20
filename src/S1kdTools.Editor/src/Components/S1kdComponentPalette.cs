using System;
using System.Threading.Tasks;
using Tesserae;
using Transpose;
using Transpose.Core;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace S1kdTools.Editor
{
    /// <summary>
    /// The things an author can add to a data module, as a rail of cards to drag
    /// into it.
    ///
    /// <b>Nothing in this list is written here.</b> The server derives it from the
    /// same template table the gutter's insert menu uses, and hands back a real
    /// projected block for each entry — so every card is drawn by
    /// <see cref="BlockRenderer"/>, the surface's own renderer, from blocks the
    /// editing stylesheet produced. A card therefore shows what dropping it will
    /// actually make: change the stylesheet and the card changes with it, and an
    /// element added to the templates appears here without being named twice.
    ///
    /// <b>Dragging is not the only way in.</b> A card is also a button: clicking it
    /// inserts after whichever block the author was last in. That is not a
    /// convenience — a palette that can only be used by dragging cannot be used
    /// with a keyboard at all.
    ///
    /// The cards are <see cref="ContextCard"/>s because that is what they are: a
    /// named thing with an icon, a line about what it is for, and a preview behind
    /// it. Their <i>preview</i> is a tooltip rather than part of the card, because a
    /// rail wide enough to show a table and a parts row at their real shape is a
    /// rail too wide to keep open beside a page.
    /// </summary>
    public sealed class S1kdComponentPalette : IComponent
    {
        private readonly EditorClient _client;
        private readonly S1kdEditorSurface _surface;
        private readonly Stack _root;
        private readonly Stack _cards;
        private readonly TextBlock _status;

        private bool _loaded;

        /// <summary>Build a palette that drops into <paramref name="surface"/>.</summary>
        public S1kdComponentPalette(EditorClient client, S1kdEditorSurface surface)
        {
            _client = client;
            _surface = surface;

            _cards = VStack().WS().Class("s1kd-palette-cards");
            _status = TextBlock("Loading…").Tiny().Secondary();

            // Height from the host, width from the host: a rail is a piece of the
            // application's layout, and a component that decides how wide it is
            // cannot be put anywhere its author did not foresee.
            _root = VStack().HS().Class("s1kd-palette").Children(
                TextBlock("Components").Tiny().SemiBold().Secondary().Class("s1kd-palette-head"),
                VStack().S().ScrollY().Children(_cards),
                _status.Class("s1kd-palette-status"));
        }

        /// <inheritdoc/>
        public HTMLElement Render() => _root.Render();

        /// <summary>
        /// Fetch the catalogue and build the cards. Once per page: what may be
        /// inserted is a property of the stylesheet, not of the open document.
        /// </summary>
        public async Task LoadAsync()
        {
            if (_loaded)
            {
                return;
            }

            IPaletteEntry[] entries = await _client.PaletteAsync();

            if (entries is null || entries.Length == 0)
            {
                _status.Text = "The server offers no components.";
                return;
            }

            _cards.Clear();

            for (var i = 0; i < entries.Length; i++)
            {
                _cards.Add(Card(entries[i]));
            }

            _loaded = true;
            _status.Text = "Drag a component into the page, or click to add it where you were.";
        }

        private IComponent Card(IPaletteEntry entry)
        {
            // Not compact: a compact card puts the name and the line about it on one
            // row, and in a rail this narrow that turns every card into an ellipsis.
            // The label is allowed the full width for the same reason - a card
            // reading "Support equi…" is a card that has to be hovered to be read.
            ContextCard card = ContextCard(entry.label, IconFor(entry.kind))
                .SetSubLabel(entry.summary)
                .MaxLabelWidth(100.percent())
                .NoRemove();

            // The preview is the projection, drawn by the surface's own renderer,
            // wrapped in the page's own class so it inherits the page's type and
            // spacing rather than the tooltip's.
            HTMLElement preview = Div(Att("s1kd-page s1kd-palette-preview"),
                BlockRenderer.Draw(entry.preview, BlockRenderer.Mode.Preview));

            card.Tooltip(Raw(preview), interactive: false, maxWidth: 460);

            HTMLElement element = card.Render();
            element.classList.add("s1kd-palette-card");
            element.setAttribute("draggable", "true");
            element.setAttribute("data-element", entry.element);
            element.setAttribute("role", "button");
            element.setAttribute("tabindex", "0");
            element.setAttribute("aria-label", "Add " + entry.label);

            element.addEventListener("dragstart", e =>
            {
                var drag = e as DragEvent;
                PaletteDrag.Begin(entry.element, entry.label);

                if (drag is object && drag.dataTransfer is object)
                {
                    // Firefox will not start a drag at all unless something is set,
                    // and the payload is also what makes the drop meaningful to
                    // anything outside this page.
                    drag.dataTransfer.setData("text/plain", entry.element);
                    drag.dataTransfer.effectAllowed = "copy";
                }

                element.classList.add("s1kd-palette-dragging");
            });

            element.addEventListener("dragend", _ =>
            {
                PaletteDrag.End();
                element.classList.remove("s1kd-palette-dragging");
            });

            element.addEventListener("click", _ => AddAsync(entry).FireAndForget());

            element.addEventListener("keydown", e =>
            {
                var key = e as KeyboardEvent;
                if (key is object && (key.key == "Enter" || key.key == " "))
                {
                    key.preventDefault();
                    AddAsync(entry).FireAndForget();
                }
            });

            return Raw(element);
        }

        /// <summary>
        /// Add a component without dragging it: after the block the author was last
        /// in, which is the one they were looking at when they reached for the rail.
        /// </summary>
        private async Task AddAsync(IPaletteEntry entry)
        {
            if (!await _surface.InsertFromPaletteAsync(entry.element))
            {
                Toast().Warning(
                    entry.label + " cannot go there",
                    "Put the caret in the part of the data module it belongs in, then try again.");
            }
        }

        /// <summary>
        /// A glyph per block kind. The rail is read by shape as much as by label —
        /// an author who has used it twice reaches for the triangle, not for the
        /// word "Warning".
        /// </summary>
        private static UIcons IconFor(string kind)
        {
            switch (kind)
            {
                case BlockKinds.Para: return UIcons.Paragraph;
                case BlockKinds.Step: return UIcons.ListCheck;
                case BlockKinds.Warning: return UIcons.TriangleWarning;
                case BlockKinds.Caution: return UIcons.TriangleWarning;
                case BlockKinds.Note: return UIcons.Info;
                case BlockKinds.Attention: return UIcons.Bulb;
                case BlockKinds.Title: return UIcons.Heading;
                case BlockKinds.Section: return UIcons.Heading;
                case BlockKinds.Figure: return UIcons.Picture;
                case BlockKinds.Table: return UIcons.Table;
                case BlockKinds.Row: return UIcons.Grid;
                case BlockKinds.List: return UIcons.List;
                case BlockKinds.ListItem: return UIcons.CircleSmall;
                case BlockKinds.Requirement: return UIcons.Tools;
                default: return UIcons.Box;
            }
        }
    }
}
