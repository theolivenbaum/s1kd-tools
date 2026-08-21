using Transpose;

namespace S1kdTools.Editor
{
    /// <summary>
    /// One edit, addressed at a block's path.
    ///
    /// An <c>[ObjectLiteral]</c> rather than a class with a serializer: it becomes
    /// the plain JavaScript object <c>JSON.stringify</c> sends, field for field,
    /// with nothing emitted for the type itself. The names are the wire's, which is
    /// why they are lower-case.
    ///
    /// A single flat shape covers every operation - <see cref="op"/> says which
    /// fields matter - because it has to be read by a C# server that does not want
    /// a polymorphic deserializer and written by a browser that does not want a
    /// serializer at all. Use the factory methods rather than the fields: they are
    /// what make an invalid combination hard to write.
    /// </summary>
    [ObjectLiteral]
    public class EditCommand
    {
        /// <summary>One of the constants on <see cref="EditOps"/>.</summary>
        public string op;

        /// <summary>The path of the block the edit addresses.</summary>
        public string path;

        /// <summary>For <c>setText</c>: the block's new inline content.</summary>
        public EditRunValue[] runs;

        /// <summary>For <c>setAttr</c>: the attribute name.</summary>
        public string name;

        /// <summary>For <c>setAttr</c>: the new value; empty removes the attribute.</summary>
        public string value;

        /// <summary>For <c>insert</c>: the element to create.</summary>
        public string element;

        /// <summary>For <c>insert</c>: where it goes, from <see cref="EditPositions"/>.</summary>
        public string position;

        /// <summary>For <c>insert</c>: initial text for the new element.</summary>
        public string text;

        /// <summary>For <c>move</c>: <c>up</c> or <c>down</c>.</summary>
        public string direction;

        /// <summary>Replace a block's inline content.</summary>
        public static EditCommand SetText(string path, EditRunValue[] runs)
        {
            return new EditCommand { op = EditOps.SetText, path = path, runs = runs };
        }

        /// <summary>Set an attribute of a block's element; an empty value removes it.</summary>
        public static EditCommand SetAttr(string path, string name, string value)
        {
            return new EditCommand { op = EditOps.SetAttr, path = path, name = name, value = value };
        }

        /// <summary>Insert a new element relative to a block.</summary>
        public static EditCommand Insert(string path, string position, string element, string text = "")
        {
            return new EditCommand
            {
                op = EditOps.Insert, path = path, position = position, element = element, text = text,
            };
        }

        /// <summary>Remove a block's element.</summary>
        public static EditCommand Delete(string path)
        {
            return new EditCommand { op = EditOps.Delete, path = path };
        }

        /// <summary>Move a block's element among its siblings.</summary>
        public static EditCommand Move(string path, string direction)
        {
            return new EditCommand { op = EditOps.Move, path = path, direction = direction };
        }
    }

    /// <summary>
    /// A run on its way back to the server.
    ///
    /// Separate from <see cref="IEditRun"/>, which is a view of what the server
    /// sent: this is a value the surface builds from the DOM the author typed into.
    /// The two carry the same fields because the round trip is the point -
    /// <see cref="src"/> in particular, which is how a reference the author left
    /// alone is put back as the very element it came from.
    /// </summary>
    [ObjectLiteral]
    public class EditRunValue
    {
        /// <summary>The run's text.</summary>
        public string text;

        /// <summary>One of <see cref="RunStyles"/>, or empty for plain text.</summary>
        public string style;

        /// <summary>Whether this is a chip rather than typed text.</summary>
        public bool atomic;

        /// <summary>
        /// The 1-based position of the element this run came from among the block's
        /// original child elements, or 0 for text the author typed.
        /// </summary>
        public int src;

        /// <summary>Plain text the author typed.</summary>
        public static EditRunValue Plain(string text)
        {
            return new EditRunValue { text = text, style = "", atomic = false, src = 0 };
        }

        /// <summary>Styled text, carrying the source element it came from when it had one.</summary>
        public static EditRunValue Styled(string text, string style, int src)
        {
            return new EditRunValue { text = text, style = style, atomic = false, src = src };
        }

        /// <summary>A chip: the server puts element <paramref name="src"/> back untouched.</summary>
        public static EditRunValue Chip(int src)
        {
            return new EditRunValue { text = "", style = "", atomic = true, src = src };
        }
    }

    /// <summary>The kinds of edit the server understands.</summary>
    public static class EditOps
    {
        /// <summary>Replace a block's inline content.</summary>
        public const string SetText = "setText";

        /// <summary>Set or remove an attribute.</summary>
        public const string SetAttr = "setAttr";

        /// <summary>Insert a new element.</summary>
        public const string Insert = "insert";

        /// <summary>Remove an element.</summary>
        public const string Delete = "delete";

        /// <summary>Reorder an element among its siblings.</summary>
        public const string Move = "move";
    }

    /// <summary>Where an insert puts the new element.</summary>
    public static class EditPositions
    {
        /// <summary>As the preceding sibling.</summary>
        public const string Before = "before";

        /// <summary>As the following sibling.</summary>
        public const string After = "after";

        /// <summary>As the first child.</summary>
        public const string FirstChild = "firstChild";

        /// <summary>As the last child.</summary>
        public const string LastChild = "lastChild";
    }

    /// <summary>Which way <see cref="EditCommand.Move"/> goes.</summary>
    public static class MoveDirections
    {
        /// <summary>Towards the start of the parent.</summary>
        public const string Up = "up";

        /// <summary>Towards the end of the parent.</summary>
        public const string Down = "down";
    }
}
