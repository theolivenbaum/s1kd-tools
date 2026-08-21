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
/// rather than thirty copies of a page header.
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
    /// </summary>
    private const string ResolvedGraphicAttribute = "s1kdResolvedGraphic";

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
        using PresentationFo fo = TransformToFo(xml, schema, title);

        using var input = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(input, new XmlWriterSettings
        {
            CloseOutput = false,
            Encoding = new UTF8Encoding(false),
        }))
        {
            fo.Document.Save(writer);
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
    ///
    /// Disposable because an illustration that a resolver could only hand over as
    /// a stream has to exist as a file for the layout engine to place it, and the
    /// result owns those files — see <see cref="PresentationFo"/>.
    /// </summary>
    public PresentationFo TransformToFo(string xml, string schema, string title)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.LoadXml(xml);

        var materialized = new MaterializedGraphics();
        try
        {
            ResolveGraphics(doc, materialized);

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
            return new PresentationFo(fo, materialized);
        }
        catch
        {
            materialized.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Point every <c>graphic</c> at the ICN file it names, when there is one.
    ///
    /// A plain absolute path, not a <c>file://</c> URI: the renderer resolves the
    /// former and draws an empty frame for the latter. It resolves an
    /// <c>external-graphic</c> by path and by nothing else — a <c>data:</c> URI is
    /// treated exactly as a missing file — so a resolver that can only produce
    /// bytes has its bytes written to a temporary file, which the returned
    /// <see cref="PresentationFo"/> owns and deletes.
    /// </summary>
    private void ResolveGraphics(XmlDocument doc, MaterializedGraphics materialized)
    {
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

            if (materialized.Write(ident, _graphics) is string temporary)
            {
                element.SetAttribute(ResolvedGraphicAttribute, temporary);
            }
        }
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
/// An XSL-FO document, plus whatever had to exist on disk for it to be laid out.
///
/// Dispose it when the layout is done. Nothing is written for a CSDB whose
/// illustrations are already files, which is the ordinary case — the temporary
/// directory is created the first time a resolver answers with bytes and no path.
/// </summary>
public sealed class PresentationFo : IDisposable
{
    private readonly IDisposable _materialized;

    internal PresentationFo(XmlDocument document, IDisposable materialized)
    {
        Document = document;
        _materialized = materialized;
    }

    /// <summary>The FO.</summary>
    public XmlDocument Document { get; }

    /// <summary>Delete anything that was written out for this layout.</summary>
    public void Dispose() => _materialized.Dispose();
}

/// <summary>
/// The illustrations a resolver could only produce as bytes, written into a
/// temporary directory for the length of one layout.
/// </summary>
internal sealed class MaterializedGraphics : IDisposable
{
    private readonly Dictionary<string, string> _written = new(StringComparer.Ordinal);
    private string? _directory;

    internal string? Write(string ident, IResourceResolver resolver)
    {
        if (_written.TryGetValue(ident, out string? existing))
        {
            return existing;
        }

        using Stream? source = resolver.Open(ident);
        if (source is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();
        source.CopyTo(bytes);

        _directory ??= System.IO.Directory.CreateTempSubdirectory("s1kd-icn-").FullName;

        // The name only has to be unique and to end in something the layout engine
        // recognises; the identifier itself came out of a document and is not used
        // as a path.
        string path = Path.Combine(_directory,
            _written.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + Extension(bytes.GetBuffer(), (int)bytes.Length));

        File.WriteAllBytes(path, bytes.ToArray());
        _written[ident] = path;
        return path;
    }

    public void Dispose()
    {
        if (_directory is null)
        {
            return;
        }

        try
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A preview is not worth failing over a file the OS is still holding.
        }
        catch (UnauthorizedAccessException)
        {
        }

        _directory = null;
        _written.Clear();
    }

    /// <summary>
    /// What to call the file. An ICN identifier carries no extension and the
    /// layout engine picks the decoder by one, so the bytes have to say what they
    /// are.
    /// </summary>
    private static string Extension(byte[] bytes, int length)
    {
        ReadOnlySpan<byte> head = bytes.AsSpan(0, Math.Min(length, 16));

        // Written out rather than compared against string literals: a UTF-8
        // literal would re-encode PNG's leading 0x89 as two bytes.
        if (head.Length >= 4 && head[0] == 0x89 && head[1] == (byte)'P'
            && head[2] == (byte)'N' && head[3] == (byte)'G') return ".png";
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ".jpg";
        if (head.StartsWith("GIF8"u8)) return ".gif";
        if (head.StartsWith("BM"u8)) return ".bmp";
        if (head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8)) return ".tif";

        // SVG is XML, which may open with a declaration, a byte-order mark or the
        // element itself; anything else textual is likelier to be SVG than not,
        // since every raster format above is recognised by its magic bytes.
        return ".svg";
    }
}
