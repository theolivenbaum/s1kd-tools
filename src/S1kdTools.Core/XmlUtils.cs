using System.Text;
using System.Xml;

namespace S1kdTools;

/// <summary>
/// XML helpers ported from <c>tools/common/s1kd_tools.c</c>. These wrap the
/// <see cref="XmlDocument"/> DOM in the same way the C code wraps libxml2, so
/// the rest of the port can stay close to the original logic.
/// </summary>
public static class XmlUtils
{
    /// <summary>
    /// Read an XML document from a file, preserving whitespace (libxml2 keeps
    /// significant whitespace and the s1kd-tools rely on round-tripping it).
    /// </summary>
    public static XmlDocument ReadDoc(string path)
    {
        var doc = NewDocument();
        doc.Load(path);
        return doc;
    }

    /// <summary>Read an XML document from an in-memory string.</summary>
    public static XmlDocument ReadMem(string xml)
    {
        var doc = NewDocument();
        doc.LoadXml(xml);
        return doc;
    }

    /// <summary>Read an XML document from a stream.</summary>
    public static XmlDocument ReadStream(Stream stream)
    {
        var doc = NewDocument();
        doc.Load(stream);
        return doc;
    }

    /// <summary>Create an empty document configured the way the tools expect.</summary>
    public static XmlDocument NewDocument()
    {
        return new XmlDocument { PreserveWhitespace = true };
    }

    /// <summary>Save an XML document to a file (mirrors <c>save_xml_doc</c>).</summary>
    public static void SaveDoc(XmlDocument doc, string path)
    {
        using var stream = File.Create(path);
        SaveDoc(doc, stream);
    }

    /// <summary>Serialize an XML document to a stream.</summary>
    public static void SaveDoc(XmlDocument doc, Stream stream)
    {
        var settings = new XmlWriterSettings
        {
            Indent = false,
            Encoding = new UTF8Encoding(false), // no BOM, matching libxml2
            OmitXmlDeclaration = false,
        };
        using var writer = XmlWriter.Create(stream, settings);
        doc.Save(writer);
    }

    /// <summary>Serialize an XML document to a string.</summary>
    public static string ToXmlString(XmlDocument doc)
    {
        using var ms = new MemoryStream();
        SaveDoc(doc, ms);
        return new UTF8Encoding(false).GetString(ms.ToArray());
    }

    /// <summary>
    /// Return the first node matching an XPath expression, evaluated relative to
    /// <paramref name="context"/> (or the document root when null).
    /// Mirrors <c>xpath_first_node</c>.
    /// </summary>
    public static XmlNode? XPathFirstNode(XmlDocument? doc, XmlNode? context, string xpath)
    {
        XmlNode? root = context ?? doc;
        return root?.SelectSingleNode(xpath);
    }

    /// <summary>
    /// Return the string value of the first node matching an XPath expression,
    /// or null when there is no match. Mirrors <c>xpath_first_value</c>.
    /// </summary>
    public static string? XPathFirstValue(XmlDocument? doc, XmlNode? context, string xpath)
    {
        return XPathFirstNode(doc, context, xpath)?.Value ?? XPathFirstNode(doc, context, xpath)?.InnerText;
    }

    /// <summary>
    /// Generate an absolute XPath expression that uniquely identifies a node,
    /// using positional predicates. Mirrors <c>xpath_of</c>.
    /// </summary>
    public static string XPathOf(XmlNode node)
    {
        var segments = new List<string>();

        XmlNode? cur = node;
        while (cur != null && cur.NodeType != XmlNodeType.Document)
        {
            string name = cur.NodeType switch
            {
                XmlNodeType.Comment => "comment()",
                XmlNodeType.ProcessingInstruction => "processing-instruction()",
                XmlNodeType.Text => "text()",
                _ => cur.Name,
            };

            if (cur.NodeType == XmlNodeType.Attribute)
            {
                segments.Add("@" + name);
                cur = ((XmlAttribute)cur).OwnerElement;
                continue;
            }

            int pos = 1;
            for (XmlNode? sib = cur.ParentNode?.FirstChild; sib != null; sib = sib.NextSibling)
            {
                if (ReferenceEquals(sib, cur))
                {
                    break;
                }
                if (sib.NodeType == cur.NodeType &&
                    (cur.NodeType != XmlNodeType.Element || sib.Name == cur.Name))
                {
                    pos++;
                }
            }

            segments.Add($"{name}[{pos}]");
            cur = cur.ParentNode;
        }

        segments.Reverse();
        return "/" + string.Join("/", segments);
    }

    /// <summary>
    /// Remove elements/attributes marked as <c>change="delete"</c> (or
    /// <c>changeType="delete"</c>). Mirrors <c>rem_delete_elems</c>.
    /// </summary>
    public static void RemoveDeleteElements(XmlDocument doc)
    {
        if (doc.DocumentElement != null)
        {
            RemoveDeleteNodes(doc.DocumentElement);
        }
    }

    private static void RemoveDeleteNodes(XmlNode node)
    {
        if (node is XmlElement el)
        {
            string? change = el.GetAttribute("change");
            if (string.IsNullOrEmpty(change))
            {
                change = el.GetAttribute("changeType");
            }
            if (change == "delete")
            {
                node.ParentNode?.RemoveChild(node);
                return;
            }
        }

        XmlNode? cur = node.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            RemoveDeleteNodes(cur);
            cur = next;
        }
    }
}
