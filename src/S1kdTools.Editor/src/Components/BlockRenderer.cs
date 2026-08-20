using Transpose.Core;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace S1kdTools.Editor
{
    /// <summary>
    /// A block, as DOM.
    ///
    /// Shared by the two places a projected block is drawn: the editing surface,
    /// where it is typed into, and the component palette, where it is a preview of
    /// what a new element would look like. One renderer rather than two, because a
    /// preview that draws a warning differently from the surface is a preview that
    /// lies about the thing it is offering.
    ///
    /// <see cref="Interactive"/> is the whole difference between the two. A preview
    /// has no path to address, so it has no gutter, its text is not editable and its
    /// fields are not typed into — everything that makes a block a *place* is left
    /// off, and what remains is the shape.
    /// </summary>
    internal static class BlockRenderer
    {
        /// <summary>The attribute a block's path is carried on.</summary>
        internal const string PathAttribute = "data-path";

        /// <summary>How a block is drawn.</summary>
        internal enum Mode
        {
            /// <summary>Editable, addressed, with its per-block commands.</summary>
            Interactive,

            /// <summary>A picture of the block: no path, no editing, no commands.</summary>
            Preview,
        }

        /// <summary>Draw <paramref name="block"/> and everything under it.</summary>
        internal static HTMLElement Draw(IEditBlock block, Mode mode)
        {
            HTMLElement host = Div(Att("s1kd-block s1kd-kind-" + block.kind));
            host.setAttribute("data-element", block.element);
            host.setAttribute("data-kind", block.kind);

            if (mode == Mode.Interactive)
            {
                host.setAttribute(PathAttribute, block.path);
            }

            if (!string.IsNullOrEmpty(block.label))
            {
                host.appendChild(Div(Att("s1kd-label", text: block.label)));
            }

            HTMLElement body = Div(Att("s1kd-body"));

            if (!string.IsNullOrEmpty(block.heading))
            {
                body.appendChild(Div(Att("s1kd-heading", text: block.heading)));
            }

            if (block.editable == EditModes.Text)
            {
                body.appendChild(Text(block, mode));
            }
            else if (block.editable == EditModes.Attr)
            {
                body.appendChild(Field(block, mode));
            }
            else if (!string.IsNullOrEmpty(block.value))
            {
                // A reference or a graphic: a value the author does not type, shown
                // so the block is not an empty box with a heading over it.
                body.appendChild(Div(Att("s1kd-value", text: block.value)));
            }

            IEditBlock[] children = block.blocks;
            if (children is object && children.Length > 0)
            {
                HTMLElement nested = Div(Att("s1kd-children"));
                for (var i = 0; i < children.Length; i++)
                {
                    nested.appendChild(Draw(children[i], mode));
                }
                body.appendChild(nested);
            }

            host.appendChild(body);

            if (mode == Mode.Interactive)
            {
                host.appendChild(Gutter(block));
            }

            return host;
        }

        /// <summary>The editable text of a block: a contenteditable holding its runs.</summary>
        private static HTMLElement Text(IEditBlock block, Mode mode)
        {
            HTMLElement text = Div(Att("s1kd-text"));
            text.setAttribute("data-placeholder", block.placeholder ?? "");

            if (mode == Mode.Interactive)
            {
                text.setAttribute("contenteditable", "true");
                text.setAttribute("spellcheck", "true");
            }

            RunCodec.Write(text, block.runs);

            // A field's label sits in front of its value on the same line; a
            // paragraph's does not. Both are the same editable element, so the
            // difference is a class rather than two shapes.
            if (block.kind == BlockKinds.Field || block.kind == BlockKinds.MetaField)
            {
                text.classList.add("s1kd-text-inline");
            }

            return text;
        }

        /// <summary>
        /// A block whose substance is one attribute — the issue number, the security
        /// classification. An input rather than a contenteditable, because these are
        /// values with a shape rather than prose, and a select when the projection
        /// knows what the shape is.
        /// </summary>
        private static HTMLElement Field(IEditBlock block, Mode mode)
        {
            // No label of its own: the block has already drawn one, and in the
            // identification section that label is the grid's first column. A second
            // one here would land in the value column and push every field down a row.
            HTMLElement row = Div(Att("s1kd-field"));

            string[] options = block.options;
            HTMLElement input;

            if (options is object && options.Length > 0)
            {
                var select = document.createElement("select") as HTMLSelectElement;
                select.className = "s1kd-field-value";

                for (var i = 0; i < options.Length; i++)
                {
                    var option = document.createElement("option") as HTMLOptionElement;
                    option.value = options[i];
                    option.textContent = options[i];
                    select.appendChild(option);
                }

                select.value = block.value ?? "";
                input = select;
            }
            else
            {
                var box = document.createElement("input") as HTMLInputElement;
                box.className = "s1kd-field-value";
                box.type = "text";
                box.value = block.value ?? "";
                input = box;
            }

            input.setAttribute("data-attr", block.attrName);

            if (mode == Mode.Preview)
            {
                input.setAttribute("disabled", "disabled");
            }

            row.appendChild(input);
            return row;
        }

        /// <summary>
        /// The per-block commands, in a gutter that appears on hover.
        ///
        /// Buttons rather than a context menu because an author editing a procedure
        /// reorders steps constantly, and a right-click and a menu for every one of
        /// them is three actions where one would do. They are drawn for every block
        /// and hidden by CSS rather than built on hover: building them on hover
        /// means measuring and positioning on a pointer move.
        /// </summary>
        private static HTMLElement Gutter(IEditBlock block)
        {
            HTMLElement gutter = Div(Att("s1kd-gutter"));

            IInsertOption[] siblings = block.insertSiblings;
            if (siblings is object && siblings.Length > 0)
            {
                gutter.appendChild(GutterButton("insert", "+", "Insert after this", block.path));
            }

            if (block.canMove)
            {
                gutter.appendChild(GutterButton("up", "↑", "Move up", block.path));
                gutter.appendChild(GutterButton("down", "↓", "Move down", block.path));
            }

            if (block.canDelete)
            {
                gutter.appendChild(GutterButton("delete", "×", "Delete", block.path));
            }

            return gutter;
        }

        private static HTMLElement GutterButton(string action, string glyph, string title, string path)
        {
            var button = document.createElement("button") as HTMLButtonElement;
            button.className = "s1kd-gutter-button s1kd-gutter-" + action;
            button.type = "button";
            button.title = title;
            button.setAttribute("data-action", action);
            button.setAttribute("data-target", path);
            button.textContent = glyph;
            return button;
        }
    }
}
