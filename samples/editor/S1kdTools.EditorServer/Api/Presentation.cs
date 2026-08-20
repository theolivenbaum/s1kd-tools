using System.Collections.Concurrent;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using S1kdTools.Tools;

namespace S1kdTools.EditorServer.Api;

/// <summary>
/// Lays a data module out as the page it will be published as, so the editor can
/// show the author what they are making rather than only what they are typing.
///
/// The stylesheets are the XSL-FO presentation stylesheets from the Airbus
/// technical-data demo (see <c>samples/editor/presentation/README.md</c>), read
/// from disk rather than embedded. That is the point of the arrangement: they are
/// a *house* style, one file per CSDB object type, and the way to change how a
/// warning box looks in this editor is to edit <c>presentation/common.xsl</c> and
/// press refresh. Nothing here is rebuilt.
///
/// The FO is rendered in-process by FOP.Sharp through
/// <see cref="RenderTool"/> — no Java, no external process, so the preview costs
/// what a transform and a layout cost and nothing else.
/// </summary>
public sealed class Presentation
{
    private readonly ConcurrentDictionary<string, XslCompiledTransform> _compiled = new(StringComparer.Ordinal);
    private readonly string _stylesheetDirectory;
    private readonly string _graphicsDirectory;

    /// <summary>
    /// Attribute written onto a <c>graphic</c> whose ICN was found on disk. The
    /// stylesheets place the image when it is there and draw a labelled
    /// placeholder when it is not, which is what an author wants while the
    /// illustration is still being drawn.
    /// </summary>
    private const string ResolvedGraphicAttribute = "s1kdResolvedGraphic";

    private static readonly string[] GraphicExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".svg"];

    /// <param name="stylesheetDirectory">The folder holding the presentation stylesheets.</param>
    /// <param name="graphicsDirectory">Where the ICNs live.</param>
    public Presentation(string stylesheetDirectory, string graphicsDirectory)
    {
        _stylesheetDirectory = Path.GetFullPath(stylesheetDirectory);
        _graphicsDirectory = Path.GetFullPath(graphicsDirectory);
    }

    /// <summary>Whether a stylesheet exists for objects declaring <paramref name="schema"/>.</summary>
    public bool CanPresent(string schema) => File.Exists(StylesheetPath(schema));

    /// <summary>Lay <paramref name="xml"/> out and return the PDF bytes.</summary>
    /// <param name="xml">The object, as the editor currently holds it.</param>
    /// <param name="schema">Which stylesheet to use, from the object's schema.</param>
    /// <param name="title">The publication title for the running header.</param>
    /// <exception cref="XmlException">The text is not well-formed.</exception>
    /// <exception cref="FileNotFoundException">There is no stylesheet for that schema.</exception>
    public byte[] RenderPdf(string xml, string schema, string title)
    {
        XmlDocument fo = TransformToFo(xml, schema, title);

        using var input = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(input, new XmlWriterSettings
        {
            CloseOutput = false,
            Encoding = new UTF8Encoding(false),
        }))
        {
            fo.Save(writer);
        }

        input.Position = 0;
        using var output = new MemoryStream();
        RenderTool.Render(input, output, RenderTool.RenderFormat.Pdf);
        return output.ToArray();
    }

    /// <summary>
    /// The XSL-FO the PDF is laid out from. Exposed because it is what an author
    /// looks at when the page is not what they expected, and because the check
    /// endpoint runs it to find out whether the module can be presented at all.
    /// </summary>
    public XmlDocument TransformToFo(string xml, string schema, string title)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.LoadXml(xml);

        ResolveGraphics(doc);

        var arguments = new XsltArgumentList();
        arguments.AddParam("publication-title", string.Empty, title);

        var fo = new XmlDocument { PreserveWhitespace = true };
        using var buffer = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            CloseOutput = false,
            Encoding = new UTF8Encoding(false),
        }))
        {
            Stylesheet(schema).Transform(doc, arguments, writer);
        }

        buffer.Position = 0;
        fo.Load(buffer);
        return fo;
    }

    /// <summary>
    /// Point every <c>graphic</c> at the ICN file it names, when there is one.
    ///
    /// A plain absolute path, not a <c>file://</c> URI: the renderer resolves the
    /// former and draws an empty frame for the latter.
    /// </summary>
    private void ResolveGraphics(XmlDocument doc)
    {
        if (!Directory.Exists(_graphicsDirectory))
        {
            return;
        }

        foreach (XmlElement element in doc.GetElementsByTagName("*").OfType<XmlElement>().ToList())
        {
            if (element.LocalName is not ("graphic" or "symbol" or "multimediaObject"))
            {
                continue;
            }

            string ident = element.GetAttribute("infoEntityIdent");
            if (ident.Length == 0)
            {
                continue;
            }

            foreach (string extension in GraphicExtensions)
            {
                string candidate = Path.Combine(_graphicsDirectory, ident + extension.ToUpperInvariant());
                string lower = Path.Combine(_graphicsDirectory, ident + extension);

                string? found = File.Exists(candidate) ? candidate : File.Exists(lower) ? lower : null;
                if (found != null)
                {
                    element.SetAttribute(ResolvedGraphicAttribute, Path.GetFullPath(found));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The stylesheet for a schema, compiled once.
    ///
    /// <see cref="XmlUrlResolver"/> rather than a null one, because the
    /// per-type stylesheets <c>xsl:import</c> <c>common.xsl</c> from beside them —
    /// that import is the whole reason the house style is one file and not thirty
    /// copies of a page header.
    /// </summary>
    private XslCompiledTransform Stylesheet(string schema) =>
        _compiled.GetOrAdd(schema, key =>
        {
            string path = StylesheetPath(key);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"No presentation stylesheet for '{key}' objects in {_stylesheetDirectory}.", path);
            }

            var xslt = new XslCompiledTransform();
            xslt.Load(path, XsltSettings.Default, new XmlUrlResolver());
            return xslt;
        });

    private string StylesheetPath(string schema)
    {
        // The schema name comes out of the object's own schema location and is
        // used as a file name, so a crafted document must not be able to reach out
        // of the stylesheet folder with it.
        string name = Path.GetFileName(schema);
        return Path.Combine(_stylesheetDirectory, name + ".xsl");
    }
}
