using System.Xml;

namespace S1kdTools;

/// <summary>
/// Source line-number map for an XML document.
///
/// <para>The C s1kd-tools obtain line numbers through libxml2's
/// <c>xmlGetLineNo</c>, which returns the line on which an element's start tag
/// <em>ends</em> (i.e. the line of the closing <c>&gt;</c> / <c>/&gt;</c> of the
/// start tag), counting from 1. <see cref="System.Xml.XmlDocument"/> nodes do
/// not implement <see cref="IXmlLineInfo"/>, so a DOM loaded via
/// <c>XmlDocument.Load</c> carries no line information at all, and even
/// <see cref="XmlReader"/>'s <see cref="IXmlLineInfo"/> reports the line of the
/// element <em>name</em> rather than the end of the start tag (the two differ
/// only for start tags that span multiple source lines).</para>
///
/// <para>To reproduce <c>xmlGetLineNo</c> faithfully this helper performs a
/// parallel pass with an <see cref="XmlReader"/> over the same source text,
/// recording every element in document order. For each element it determines the
/// line of the start tag's terminator by scanning the raw source from the
/// element's start position to the first <c>&gt;</c> that is not inside a quoted
/// attribute value. The resulting per-element lines are matched back to the
/// caller's <see cref="XmlDocument"/> by a parallel document-order walk: both the
/// reader and the DOM enumerate elements in the same order, so the i-th element
/// encountered corresponds to the i-th recorded line.</para>
///
/// <para>This makes line numbers reliable for the cases the C tools report on
/// (IDREF/IDREFS attribute owners in s1kd-validate, CIR reference elements in
/// s1kd-repcheck), provided the DOM has not been structurally rearranged after
/// loading. Tools that strip or remove nodes before looking up a line build the
/// map from the original (pre-modification) source, which is what the C does
/// too (it keeps line numbers from the original parse).</para>
/// </summary>
public sealed class LineInfo
{
    private readonly Dictionary<XmlElement, int> _lines = new(ReferenceEqualityComparer.Instance);

    private LineInfo()
    {
    }

    /// <summary>
    /// Build a line map for <paramref name="doc"/> from its raw source text.
    /// </summary>
    /// <param name="doc">The DOM whose elements should be mapped.</param>
    /// <param name="source">The exact source text the DOM was parsed from.</param>
    public static LineInfo Build(XmlDocument doc, string source)
    {
        var info = new LineInfo();
        if (doc.DocumentElement == null)
        {
            return info;
        }

        List<int> linesInOrder = ScanElementLines(source);

        // Walk the DOM in document order and pair each element with the
        // correspondingly-indexed scanned line.
        int index = 0;
        WalkDom(doc.DocumentElement, linesInOrder, info, ref index);
        return info;
    }

    /// <summary>
    /// Build a line map for <paramref name="doc"/> by reading the file at
    /// <paramref name="path"/>. Returns an empty map (all lookups yield 0) when
    /// the file cannot be read or has no source (e.g. stdin already consumed).
    /// </summary>
    public static LineInfo BuildFromFile(XmlDocument doc, string path)
    {
        try
        {
            string source = File.ReadAllText(path);
            return Build(doc, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new LineInfo();
        }
    }

    /// <summary>
    /// The source line of the start tag of <paramref name="element"/>, matching
    /// libxml2's <c>xmlGetLineNo</c>. Returns 0 when unknown.
    /// </summary>
    public int LineOf(XmlElement? element)
    {
        if (element != null && _lines.TryGetValue(element, out int line))
        {
            return line;
        }
        return 0;
    }

    /// <summary>
    /// The source line of <paramref name="node"/>. For an attribute this is the
    /// line of its owner element (mirroring the C's use of
    /// <c>xmlGetLineNo(node-&gt;parent)</c> for IDREF attributes); for an element
    /// it is that element's line. Returns 0 when unknown.
    /// </summary>
    public int LineOfNode(XmlNode? node)
    {
        return node switch
        {
            XmlAttribute attr => LineOf(attr.OwnerElement),
            XmlElement el => LineOf(el),
            _ => 0,
        };
    }

    private static void WalkDom(XmlElement el, List<int> linesInOrder, LineInfo info, ref int index)
    {
        if (index < linesInOrder.Count)
        {
            info._lines[el] = linesInOrder[index];
        }
        index++;

        for (XmlNode? child = el.FirstChild; child != null; child = child.NextSibling)
        {
            if (child is XmlElement childEl)
            {
                WalkDom(childEl, linesInOrder, info, ref index);
            }
        }
    }

    /// <summary>
    /// Scan the source for every element start tag in document order and return
    /// the line (1-based) on which each start tag's terminator appears.
    /// </summary>
    private static List<int> ScanElementLines(string source)
    {
        var lines = new List<int>();

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            // Preserve the exact source so positions line up with the raw text.
            CheckCharacters = false,
        };

        using var sr = new StringReader(source);
        using var reader = XmlReader.Create(sr, settings);
        var li = (IXmlLineInfo)reader;

        try
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                // (line, col) of the element name (just after the '<').
                int startLine = li.HasLineInfo() ? li.LineNumber : 0;
                int startCol = li.HasLineInfo() ? li.LinePosition : 0;
                lines.Add(StartTagEndLine(source, startLine, startCol));
            }
        }
        catch (XmlException)
        {
            // Best-effort: keep whatever was scanned before the error.
        }

        return lines;
    }

    /// <summary>
    /// Given the (1-based line, 1-based column) of an element name, find the line
    /// on which the start tag's terminating <c>&gt;</c> appears, skipping over
    /// any <c>&gt;</c> characters inside single- or double-quoted attribute
    /// values. Mirrors the line libxml2 reports for the element.
    /// </summary>
    private static int StartTagEndLine(string source, int startLine, int startCol)
    {
        if (startLine <= 0 || startCol <= 0)
        {
            return startLine > 0 ? startLine : 0;
        }

        int offset = OffsetOf(source, startLine, startCol);
        if (offset < 0)
        {
            return startLine;
        }

        int line = startLine;
        char quote = '\0';
        for (int i = offset; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\n')
            {
                line++;
                continue;
            }

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                continue;
            }

            if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return line;
            }
        }

        return startLine;
    }

    /// <summary>
    /// Convert a 1-based (line, column) position into a 0-based character offset
    /// into <paramref name="source"/>. Returns -1 when out of range.
    /// </summary>
    private static int OffsetOf(string source, int line, int column)
    {
        int curLine = 1;
        int i = 0;
        while (curLine < line && i < source.Length)
        {
            if (source[i] == '\n')
            {
                curLine++;
            }
            i++;
        }

        if (curLine != line)
        {
            return -1;
        }

        // column is 1-based and counts from the start of the line.
        int offset = i + (column - 1);
        return offset <= source.Length ? offset : -1;
    }
}
