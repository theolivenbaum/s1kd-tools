using System.Collections.Concurrent;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using S1kdTools.Tools;

namespace S1kdTools.Presentation;

/// <summary>
/// Turns an S1000D CSDB object into a page-oriented document: the object is
/// transformed by the presentation stylesheet for its type, and the resulting
/// XSL-FO is laid out and rendered by FOP.Sharp through
/// <see cref="RenderTool"/>.
/// </summary>
/// <example>
/// <code>
/// var dm = new XmlDocument();
/// dm.Load("DMC-A350X-A-27-31-00-00A-720A-A_001-00_EN-GB.XML");
///
/// using Stream pdf = S1000DPresentation.RenderToPdf(dm);
/// </code>
/// </example>
public static class S1000DPresentation
{
    private static readonly ConcurrentDictionary<string, XslCompiledTransform> Compiled = new(StringComparer.Ordinal);

    /// <summary>
    /// Attribute added to <c>graphic</c>/<c>symbol</c> elements by
    /// <see cref="PrepareForPresentation"/> when the ICN they reference is found
    /// in one of the configured graphics directories. The stylesheets place the
    /// image when it is present and draw a labelled placeholder when it is not.
    /// </summary>
    public const string ResolvedGraphicAttribute = "s1kdResolvedGraphic";

    private static readonly string[] GraphicExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".svg"];

    // ---------------------------------------------------------------- detection

    /// <summary>
    /// Work out which CSDB object type <paramref name="csdbObject"/> is, from its
    /// schema location and, failing that, from its document and content elements.
    /// </summary>
    /// <exception cref="NotSupportedException">The object is not a type this library presents.</exception>
    public static CsdbObjectType DetectObjectType(XmlDocument csdbObject)
    {
        ArgumentNullException.ThrowIfNull(csdbObject);
        if (!TryDetectObjectType(csdbObject, out CsdbObjectTypeInfo info))
        {
            string root = csdbObject.DocumentElement?.LocalName ?? "(none)";
            throw new NotSupportedException(
                $"Cannot determine the S1000D object type: document element <{root}> matches no known schema.");
        }
        return info.Type;
    }

    /// <summary>
    /// Work out which CSDB object type <paramref name="csdbObject"/> is, returning
    /// false rather than throwing when it is not one this library presents.
    /// </summary>
    public static bool TryDetectObjectType(XmlDocument csdbObject, out CsdbObjectTypeInfo info)
    {
        ArgumentNullException.ThrowIfNull(csdbObject);
        info = default;

        XmlElement? root = csdbObject.DocumentElement;
        if (root == null)
        {
            return false;
        }

        // The schema location names the schema outright and is the most reliable
        // signal: several data module types share a document element.
        string schemaLocation = root.GetAttribute("noNamespaceSchemaLocation",
            "http://www.w3.org/2001/XMLSchema-instance");
        if (CsdbObjectTypes.TryFromSchema(schemaLocation, out info))
        {
            return true;
        }

        // No usable schema location: for a data module the single child of
        // <content> identifies the type; other objects are named by their
        // document element.
        if (string.Equals(root.LocalName, "dmodule", StringComparison.Ordinal))
        {
            XmlNode? content = FirstElement(root, "content");
            if (content != null)
            {
                foreach (XmlNode child in content.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element &&
                        CsdbObjectTypes.TryFromContentElement(child.LocalName, out info))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        return CsdbObjectTypes.TryFromContentElement(root.LocalName, out info);
    }

    // -------------------------------------------------------------- stylesheets

    /// <summary>
    /// The source of the presentation stylesheet used for <paramref name="type"/>.
    /// Handy as a starting point for a house style: copy it, edit it, and pass the
    /// copy to <c>s1kd render -s</c> or to
    /// <see cref="TransformToFo(XmlDocument, XslCompiledTransform, PresentationOptions?, CsdbObjectTypeInfo?)"/>.
    /// </summary>
    public static string GetStylesheet(CsdbObjectType type) =>
        PresentationStylesheets.Read(CsdbObjectTypes.Info(type).StylesheetName);

    /// <summary>
    /// The compiled presentation stylesheet for <paramref name="type"/>. Compiled
    /// once and cached; safe to use from several threads.
    /// </summary>
    public static XslCompiledTransform GetCompiledStylesheet(CsdbObjectType type)
    {
        string name = CsdbObjectTypes.Info(type).StylesheetName;
        return Compiled.GetOrAdd(name, Compile);
    }

    private static XslCompiledTransform Compile(string name)
    {
        string xsl = PresentationStylesheets.Read(name);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };

        using var stringReader = new StringReader(xsl);
        using XmlReader reader = XmlReader.Create(stringReader, settings,
            new Uri(PresentationStylesheets.BaseUri, name).AbsoluteUri);

        var transform = new XslCompiledTransform();
        transform.Load(reader, XsltSettings.Default, PresentationStylesheets.Resolver);
        return transform;
    }

    // ------------------------------------------------------------------ loading

    /// <summary>Read a CSDB object from a file, ignoring any DTD it declares.</summary>
    public static XmlDocument Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using FileStream stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Read a CSDB object from a stream, ignoring any DTD it declares.</summary>
    public static XmlDocument Load(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };
        using XmlReader reader = XmlReader.Create(xml, settings);
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.Load(reader);
        return doc;
    }

    // --------------------------------------------------------------- transforms

    /// <summary>
    /// Copy <paramref name="csdbObject"/> and annotate the copy for presentation:
    /// every <c>graphic</c>/<c>symbol</c> whose ICN is found under
    /// <see cref="PresentationOptions.GraphicsDirectories"/> gets a
    /// <see cref="ResolvedGraphicAttribute"/> holding the file's path. The input
    /// is never modified.
    /// </summary>
    public static XmlDocument PrepareForPresentation(XmlDocument csdbObject, PresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csdbObject);
        options ??= PresentationOptions.Default;

        var prepared = (XmlDocument)csdbObject.CloneNode(true);
        if (options.GraphicsDirectories.Count == 0)
        {
            return prepared;
        }

        Dictionary<string, string> icns = IndexGraphics(options.GraphicsDirectories);
        if (icns.Count == 0)
        {
            return prepared;
        }

        foreach (XmlElement element in prepared.GetElementsByTagName("*").OfType<XmlElement>().ToList())
        {
            if (element.LocalName is not ("graphic" or "symbol" or "multimediaObject"))
            {
                continue;
            }

            string ident = element.GetAttribute("infoEntityIdent");
            if (!string.IsNullOrEmpty(ident) && icns.TryGetValue(ident.ToUpperInvariant(), out string? file))
            {
                // A plain absolute path, not a file:// URI: the renderer resolves
                // the former and draws an empty frame for the latter.
                element.SetAttribute(ResolvedGraphicAttribute, Path.GetFullPath(file));
            }
        }

        return prepared;
    }

    private static Dictionary<string, string> IndexGraphics(IReadOnlyList<string> directories)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                int rank = Array.IndexOf(GraphicExtensions, extension);
                if (rank < 0)
                {
                    continue;
                }

                string key = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
                // Earlier directories win; within a directory, the earlier
                // extension in GraphicExtensions wins (raster before vector).
                if (!index.TryGetValue(key, out string? existing) ||
                    Array.IndexOf(GraphicExtensions, Path.GetExtension(existing).ToLowerInvariant()) > rank)
                {
                    index[key] = file;
                }
            }
        }
        return index;
    }

    /// <summary>
    /// Transform a CSDB object into XSL-FO with the presentation stylesheet for
    /// its type.
    /// </summary>
    public static XmlDocument TransformToFo(XmlDocument csdbObject, PresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csdbObject);
        if (!TryDetectObjectType(csdbObject, out CsdbObjectTypeInfo info))
        {
            string root = csdbObject.DocumentElement?.LocalName ?? "(none)";
            throw new NotSupportedException(
                $"Cannot determine the S1000D object type: document element <{root}> matches no known schema.");
        }

        return TransformToFo(csdbObject, GetCompiledStylesheet(info.Type), options, info);
    }

    /// <summary>
    /// Transform a CSDB object into XSL-FO with a stylesheet of your own — the
    /// same path <see cref="TransformToFo(XmlDocument, PresentationOptions?)"/>
    /// takes, but with the built-in stylesheet replaced.
    /// </summary>
    /// <param name="csdbObject">The object to present.</param>
    /// <param name="stylesheet">The compiled presentation stylesheet to apply.</param>
    /// <param name="options">Layout options, passed to the stylesheet as parameters.</param>
    /// <param name="info">
    /// The catalogue entry whose defaults fill in the options (mainly the header's
    /// publication title). Detected from the object when null.
    /// </param>
    public static XmlDocument TransformToFo(XmlDocument csdbObject, XslCompiledTransform stylesheet,
        PresentationOptions? options = null, CsdbObjectTypeInfo? info = null)
    {
        ArgumentNullException.ThrowIfNull(csdbObject);
        ArgumentNullException.ThrowIfNull(stylesheet);
        options ??= PresentationOptions.Default;

        CsdbObjectTypeInfo typeInfo = info ?? (TryDetectObjectType(csdbObject, out CsdbObjectTypeInfo detected)
            ? detected
            : CsdbObjectTypes.Info(CsdbObjectType.Description));

        XmlDocument prepared = PrepareForPresentation(csdbObject, options);

        var arguments = new XsltArgumentList();
        foreach (KeyValuePair<string, string> parameter in options.ToStylesheetParameters(typeInfo))
        {
            arguments.AddParam(parameter.Key, string.Empty, parameter.Value);
        }

        var fo = new XmlDocument { PreserveWhitespace = true };
        using var buffer = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            CloseOutput = false,
            Encoding = new UTF8Encoding(false),
        }))
        {
            stylesheet.Transform(prepared, arguments, writer);
        }

        buffer.Position = 0;
        fo.Load(buffer);
        return fo;
    }

    // ----------------------------------------------------------------- rendering

    /// <summary>
    /// Render a CSDB object to PDF. The returned stream is positioned at the start
    /// and is the caller's to dispose.
    /// </summary>
    public static Stream RenderToPdf(XmlDocument csdbObject, PresentationOptions? options = null) =>
        Render(csdbObject, RenderTool.RenderFormat.Pdf, options);

    /// <summary>Render a CSDB object read from <paramref name="xml"/> to PDF.</summary>
    public static Stream RenderToPdf(Stream xml, PresentationOptions? options = null) =>
        RenderToPdf(Load(xml), options);

    /// <summary>Render the CSDB object stored at <paramref name="path"/> to PDF.</summary>
    public static Stream RenderFileToPdf(string path, PresentationOptions? options = null) =>
        RenderToPdf(Load(path), options);

    /// <summary>Render a CSDB object to PDF, writing it straight to <paramref name="output"/>.</summary>
    public static void RenderToPdf(XmlDocument csdbObject, Stream output, PresentationOptions? options = null) =>
        Render(csdbObject, output, RenderTool.RenderFormat.Pdf, options);

    /// <summary>
    /// Render a CSDB object to any format FOP.Sharp supports — PDF, plain text,
    /// Markdown or HTML. The returned stream is positioned at the start.
    /// </summary>
    public static Stream Render(XmlDocument csdbObject, RenderTool.RenderFormat format,
        PresentationOptions? options = null)
    {
        var output = new MemoryStream();
        Render(csdbObject, output, format, options);
        output.Position = 0;
        return output;
    }

    /// <summary>
    /// Render a CSDB object to <paramref name="output"/> in the requested format.
    /// The stream is written but not closed.
    /// </summary>
    public static void Render(XmlDocument csdbObject, Stream output, RenderTool.RenderFormat format,
        PresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        options ??= PresentationOptions.Default;

        XmlDocument fo = TransformToFo(csdbObject, options);
        using MemoryStream foStream = Serialize(fo);
        RenderTool.Render(foStream, output, format, options.FontDirectories, options.UseNativePdfRenderer);
    }

    /// <summary>
    /// Render several CSDB objects into a single document: each is transformed by
    /// the stylesheet for its own type, the XSL-FO is merged
    /// (<see cref="RenderTool.MergeFo"/>) and the result rendered once — one PDF
    /// with the objects back to back, in the order given.
    /// </summary>
    public static Stream RenderToPdf(IEnumerable<XmlDocument> csdbObjects, PresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csdbObjects);
        options ??= PresentationOptions.Default;

        var fos = new List<XmlDocument>();
        foreach (XmlDocument csdbObject in csdbObjects)
        {
            fos.Add(TransformToFo(csdbObject, options));
        }
        if (fos.Count == 0)
        {
            throw new ArgumentException("No CSDB objects to render.", nameof(csdbObjects));
        }

        XmlDocument merged = RenderTool.MergeFo(fos);
        using MemoryStream foStream = Serialize(merged);
        var output = new MemoryStream();
        RenderTool.Render(foStream, output, RenderTool.RenderFormat.Pdf,
            options.FontDirectories, options.UseNativePdfRenderer);
        output.Position = 0;
        return output;
    }

    private static MemoryStream Serialize(XmlDocument document)
    {
        var buffer = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            CloseOutput = false,
            Encoding = new UTF8Encoding(false),
        }))
        {
            document.Save(writer);
        }
        buffer.Position = 0;
        return buffer;
    }

    private static XmlNode? FirstElement(XmlNode parent, string localName)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element &&
                string.Equals(child.LocalName, localName, StringComparison.Ordinal))
            {
                return child;
            }
        }
        return null;
    }
}
