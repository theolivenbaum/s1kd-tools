namespace S1kdTools.Editor
{
    /// <summary>
    /// What is being dragged out of the palette, while it is being dragged.
    ///
    /// A field rather than the drag event's own <c>dataTransfer</c>, because the
    /// question the surface has to answer is asked during <c>dragover</c> — "may
    /// this go here?" — and <c>dataTransfer.getData</c> is deliberately blank until
    /// the drop. The alternative is encoding the element name into a MIME type so
    /// it can be read off <c>dataTransfer.types</c>, which works and is a trick.
    ///
    /// One field is enough because a browser drags one thing at a time, and the
    /// palette and the surface it drops into are always in one page.
    /// </summary>
    internal static class PaletteDrag
    {
        /// <summary>The element being dragged, or null when nothing is.</summary>
        internal static string Element { get; private set; }

        /// <summary>What the palette called it, for the drop's toast.</summary>
        internal static string Label { get; private set; }

        internal static void Begin(string element, string label)
        {
            Element = element;
            Label = label;
        }

        internal static void End()
        {
            Element = null;
            Label = null;
        }
    }
}
