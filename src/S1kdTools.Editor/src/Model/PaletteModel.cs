using Transpose;

namespace S1kdTools.Editor
{
    /// <summary>
    /// One thing the component palette offers, as the server describes it.
    ///
    /// <see cref="preview"/> is the point of the shape. It is a real projected
    /// block — the server created the element with the same template an insert
    /// command uses and ran it through the editing stylesheet — so the palette
    /// draws it with the same renderer as the surface, and a card cannot promise a
    /// shape that dropping it would not produce.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IPaletteEntry
    {
        /// <summary>The element name to send in an insert command.</summary>
        string element { get; }

        /// <summary>What the palette calls it.</summary>
        string label { get; }

        /// <summary>The block kind it projects as, for the card's icon.</summary>
        string kind { get; }

        /// <summary>One line on what it is for.</summary>
        string summary { get; }

        /// <summary>
        /// The container the preview was built in — which is why a step's preview
        /// carries the number it does.
        /// </summary>
        string previewedIn { get; }

        /// <summary>The block, exactly as the surface would draw it.</summary>
        IEditBlock preview { get; }
    }
}
