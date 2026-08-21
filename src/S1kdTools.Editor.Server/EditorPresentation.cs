using System.Collections.Concurrent;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using S1kdTools.Tools;

namespace S1kdTools.Editor.Server;

/// <summary>
/// Lays a data module out as the page it will be published as, so the editor can
/// show the author what they are making rather than only what they are typing.
///
/// The stylesheets are resolved rather than embedded, and this library ships none
/// of its own. That is deliberate: how a page looks is a publishing decision,
/// S1000D does not make it, and neither should a NuGet package. Point
/// <see cref="EditorOptions.PresentationDirectory"/> at yours and the way to
/// change how a warning box looks is to edit a stylesheet and press refresh —
/// nothing is rebuilt. The editor sample has a set to start from.
///
/// <b>Neither the stylesheets nor the illustrations have to be files.</b> Both are
/// <see cref="IResourceResolver"/>s, so a CSDB held in a content management
/// system, an object store or a zip supplies one of those and nothing else
/// changes — see <see cref="EditorOptions.PresentationStylesheets"/> and
/// <see cref="EditorOptions.Graphics"/>. A stylesheet's <c>xsl:import</c> hrefs go
/// back through the same resolver, which is what keeps a house style one file
/// rather than thirty copies of a page header; an illustration's bytes go
/// through to the layout engine's own resolver hook.
///
/// The FO is rendered in-process by FOP.Sharp through
/// <see cref="RenderTool"/> — no Java, no external process, so the preview costs
/// what a transform and a layout cost and nothing else.
/// </summary>
public sealed class EditorPresentation
{
    private readonly ConcurrentDictionary<string, XslCompiledTransform> _compiled = new(StringComparer.Ordinal);
    private readonly IResourceResolver _stylesheets;
    private readonly IResourceResolver _graphics;

    /// <summary>
    /// Attribute written onto a <c>graphic</c> whose ICN was resolved. The
    /// stylesheets place the image when it is there and draw a labelled
    /// placeholder when it is not, which is what an author wants while the
    /// illustration is still being drawn.
    ///
    /// Its value is a path when the resolver had one and a
    /// <see cref="IllustrationScheme"/> URI otherwise; a stylesheet never has to
    /// know which, because it only ever copies it into <c>src</c>.
    /// </summary>
    private const string ResolvedGraphicAttribute = "s1kdResolvedGraphic";

    /// <summary>
    /// The URI scheme an illustration with no path of its own is named by. It reaches
    /// the layout engine as written and comes back to
    /// <see cref="ResolvedIllustrations"/>, which holds the bytes already read for it.
    /// </summary>
    internal const string IllustrationScheme = "s1kd-icn:";

    /// <summary>
    /// The extensions an ICN identifier is tried with. S1000D names an ICN without
    /// one, so the file beside it is <c>ICN-….PNG</c> and the reference is not.
    /// </summary>
    public static readonly string[] GraphicExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".svg"];

    /// <param name="stylesheetDirectory">The folder holding the presentation stylesheets.</param>
    /// <param name="graphicsDirectory">Where the ICNs live.</param>
    public EditorPresentation(string stylesheetDirectory, string graphicsDirectory)
        : this(ResourceResolvers.Directory([stylesheetDirectory]),
               ResourceResolvers.Directory([graphicsDirectory], GraphicExtensions))
    {
    }

    /// <param name="stylesheets">
    /// Where <c>&lt;schema&gt;.xsl</c> and anything it imports comes from.
    /// </param>
    /// <param name="graphics">Where an ICN identifier turns into image bytes.</param>
    public EditorPresentation(IResourceResolver stylesheets, IResourceResolver graphics)
    {
        _stylesheets = stylesheets ?? throw new ArgumentNullException(nameof(stylesheets));
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
    }

    /// <summary>Whether a stylesheet exists for objects declaring <paramref name="schema"/>.</summary>
    public bool CanPresent(string schema)
    {
        string name = StylesheetName(schema);

        // A resolver backed by files answers the cheap question; anything else has
        // to be asked for the bytes, since "do you have this" is the same question.
        if (_stylesheets.LocalPath(name) is not null)
        {
            return true;
        }

        using Stream? stream = _stylesheets.Open(name);
        return stream is not null;
    }

    /// <summary>Lay <paramref name="xml"/> out and return the PDF bytes.</summary>
    /// <param name="xml">The object, as the editor currently holds it.</param>
    /// <param name="schema">Which stylesheet to use, from the object's schema.</param>
    /// <param name="title">The publication title for the running header.</param>
    /// <exception cref="XmlException">The text is not well-formed.</exception>
    /// <exception cref="FileNotFoundException">There is no stylesheet for that schema.</exception>
    public byte[] RenderPdf(string xml, string schema, string title)
    {
        PresentationLayout layout = LayOut(xml, schema, title);

        using var input = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(input, new XmlWriterSettings
        {
            CloseOutput = false,
            Encoding = new UTF8Encoding(false),
        }))
        {
            layout.Fo.Save(writer);
        }

        input.Position = 0;
        using var output = new MemoryStream();
        RenderTool.Render(input, output, RenderTool.RenderFormat.Pdf,
            resources: layout.Illustrations);
        return output.ToArray();
    }

    /// <summary>
    /// The XSL-FO the PDF is laid out from. Exposed because it is what an author
    /// looks at when the page is not what they expected, and because the check
    /// endpoint runs it to find out whether the module can be presented at all.
    ///
    /// An illustration the resolver could only produce as bytes appears here as a
    /// <c>s1kd-icn:</c> URI rather than a path. Only <see cref="RenderPdf"/> can turn
    /// one back into an image, since it holds the bytes; FO taken from here and laid
    /// out elsewhere has the placeholder.
    /// </summary>
    public XmlDocument TransformToFo(string xml, string schema, string title) =>
        LayOut(xml, schema, title).Fo;

    /// <summary>The FO, and the illustrations that were read while producing it.</summary>
    private PresentationLayout LayOut(string xml, string schema, string title)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.LoadXml(xml);

        Dictionary<string, byte[]> illustrations = ResolveGraphics(doc);

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
        return new PresentationLayout(fo, new ResolvedIllustrations(illustrations));
    }

    /// <summary>
    /// Point every <c>graphic</c> at the ICN the layout engine should draw there.
    ///
    /// A resolver that has the file on disk says so and the attribute is a plain
    /// absolute path, which costs nothing to produce and nothing to hold. One that
    /// can only produce bytes has them read once, here, and the attribute names them
    /// instead; the engine asks <see cref="ResolvedIllustrations"/> for them when it
    /// reaches the image. Either way the attribute is written only when the
    /// illustration exists, which is how a stylesheet knows to draw the placeholder.
    /// </summary>
    private Dictionary<string, byte[]> ResolveGraphics(XmlDocument doc)
    {
        var illustrations = new Dictionary<string, byte[]>(StringComparer.Ordinal);

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

            if (_graphics.LocalPath(ident) is string path)
            {
                element.SetAttribute(ResolvedGraphicAttribute, Path.GetFullPath(path));
                continue;
            }

            if (illustrations.ContainsKey(ident))
            {
                element.SetAttribute(ResolvedGraphicAttribute, IllustrationScheme + ident);
                continue;
            }

            using Stream? stream = _graphics.Open(ident);
            if (stream is null)
            {
                continue;
            }

            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            if (bytes.Length == 0)
            {
                continue;
            }

            illustrations[ident] = bytes.ToArray();
            element.SetAttribute(ResolvedGraphicAttribute, IllustrationScheme + ident);
        }

        return illustrations;
    }

    /// <summary>
    /// The stylesheet for a schema, compiled once.
    ///
    /// A resolving <see cref="XmlResolver"/> rather than a null one, because the
    /// per-type stylesheets <c>xsl:import</c> <c>common.xsl</c> from beside them —
    /// that import is the whole reason the house style is one file and not thirty
    /// copies of a page header.
    /// </summary>
    private XslCompiledTransform Stylesheet(string schema) =>
        _compiled.GetOrAdd(schema, key =>
        {
            string name = StylesheetName(key);

            using Stream stream = _stylesheets.Open(name)
                ?? throw new FileNotFoundException(
                    $"No presentation stylesheet for '{key}' objects: nothing resolved '{name}'.", name);

            // A stylesheet that is on disk keeps its own address, so a relative
            // href beside it resolves the ordinary way; one that came from a
            // resolver has no address and its hrefs go back through the resolver.
            string baseUri = _stylesheets.LocalPath(name) is string path
                ? new Uri(Path.GetFullPath(path)).AbsoluteUri
                : ResourceXmlResolver.OpaqueBaseUri + Uri.EscapeDataString(name);

            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            }, baseUri);

            var xslt = new XslCompiledTransform();
            xslt.Load(reader, XsltSettings.Default, new ResourceXmlResolver(_stylesheets));
            return xslt;
        });

    private static string StylesheetName(string schema)
    {
        // The schema name comes out of the object's own schema location and is
        // used as a file name, so a crafted document must not be able to reach out
        // of the stylesheet folder with it.
        return Path.GetFileName(schema) + ".xsl";
    }
}

/// <summary>
/// The illustrations read while laying an object out, offered back to the layout
/// engine by the URI they were written into the FO as.
///
/// Only <see cref="EditorPresentation.IllustrationScheme"/> URIs are answered.
/// Anything else -- a path an author wrote by hand, a path a resolver gave us --
/// returns null and the engine opens it as a file, exactly as it would have.
/// </summary>
internal sealed class ResolvedIllustrations(IReadOnlyDictionary<string, byte[]> illustrations)
    : IResourceResolver
{
    public Stream? Open(string name) =>
        name.StartsWith(EditorPresentation.IllustrationScheme, StringComparison.Ordinal)
        && illustrations.TryGetValue(
            name[EditorPresentation.IllustrationScheme.Length..], out byte[]? bytes)
            ? new MemoryStream(bytes, writable: false)
            : null;
}

/// <summary>An XSL-FO document and the illustrations it names.</summary>
/// <param name="Fo">The FO.</param>
/// <param name="Illustrations">What the layout engine resolves its images through.</param>
internal readonly record struct PresentationLayout(XmlDocument Fo, IResourceResolver Illustrations);
