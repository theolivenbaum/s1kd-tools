using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Editing;

/// <summary>
/// The stylesheet an object is projected with — where it comes from, and the
/// compiled transform it becomes.
///
/// <b>This is the seam the whole design turns on.</b> Which parts of a CSDB object
/// are editable, what each block is called, how a step is numbered and which
/// elements are shown as chips are all decisions the projection makes, and they
/// belong to a publishing organisation rather than to this library. A project with
/// a schema this port has never seen, or a house rule that a certain title is
/// never retyped, should be able to change them without forking anything.
///
/// So a stylesheet can come from the assembly (the default), from a file, from a
/// string, or from a transform the caller compiled themselves:
///
/// <code>
/// var house = EditStylesheet.FromFile("editing/house.xsl");
/// var model = EditProjection.Project(doc, new EditProfile(house));
/// </code>
///
/// <b>A stylesheet of your own does not start from nothing.</b> However it is
/// loaded, its <c>xsl:import</c> and <c>xsl:include</c> hrefs resolve against its
/// own location first and then against this assembly's <c>Resources/editing/</c> —
/// so a house stylesheet is usually a handful of templates over ours:
///
/// <code>
/// &lt;xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"&gt;
///   &lt;xsl:import href="edit.xsl"/&gt;
///
///   &lt;!-- import precedence: this wins over the imported one --&gt;
///   &lt;xsl:template match="houseSpecificThing"&gt;
///     &lt;xsl:call-template name="text-block"&gt;
///       &lt;xsl:with-param name="kind" select="'para'"/&gt;
///     &lt;/xsl:call-template&gt;
///   &lt;/xsl:template&gt;
/// &lt;/xsl:stylesheet&gt;
/// </code>
///
/// Use <c>xsl:import</c> to *override* a template ours already has, and
/// <c>xsl:include</c> to add matches for elements it does not — an included
/// template that collides with an existing one at the same priority is an error,
/// which is XSLT telling you that you meant to import.
///
/// Compiling costs tens of milliseconds and an editor re-projects after every
/// committed edit, so each instance compiles once and holds the result. Hold the
/// instance, not the file name.
/// </summary>
public sealed class EditStylesheet
{
    /// <summary>The stylesheet shipped with this library.</summary>
    public const string DefaultResource = "editing/edit.xsl";

    /// <summary>
    /// The base URI compiled stylesheets are given. Never dereferenced as a real
    /// URI: <see cref="EditStylesheetResolver"/> takes the file name off it and
    /// looks the resource up by name.
    /// </summary>
    internal const string EmbeddedBaseUri = "s1kd-editing:///";

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
    };

    private readonly Func<XslCompiledTransform> _compile;
    private readonly Lock _gate = new();

    private XslCompiledTransform? _compiled;

    private EditStylesheet(string name, Func<XslCompiledTransform> compile)
    {
        Name = name;
        _compile = compile;
    }

    /// <summary>The stylesheet this library ships, and what every overload defaults to.</summary>
    public static EditStylesheet Default { get; } = Embedded(DefaultResource);

    /// <summary>What to call this stylesheet in a message. Not an address.</summary>
    public string Name { get; }

    /// <summary>A stylesheet embedded in this assembly, under <c>Resources/</c>.</summary>
    /// <param name="resourcePath">e.g. <c>editing/edit.xsl</c>.</param>
    public static EditStylesheet Embedded(string resourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

        return new EditStylesheet(resourcePath, () =>
        {
            using Stream stream = EmbeddedResources.Open(resourcePath)
                ?? throw new FileNotFoundException(
                    $"Embedded editing stylesheet not found: {resourcePath}", resourcePath);

            return Compile(stream, EmbeddedBaseUri + Path.GetFileName(resourcePath), null);
        });
    }

    /// <summary>
    /// A stylesheet on disk. Its imports resolve from its own directory first, then
    /// from this assembly — so it can import <c>edit.xsl</c> and override a template
    /// or two.
    /// </summary>
    public static EditStylesheet FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);

        return new EditStylesheet(full, () =>
        {
            if (!File.Exists(full))
            {
                throw new FileNotFoundException($"Editing stylesheet not found: {full}", full);
            }

            using Stream stream = File.OpenRead(full);
            return Compile(stream, new Uri(full).AbsoluteUri, Path.GetDirectoryName(full));
        });
    }

    /// <summary>
    /// A stylesheet held in a string. Its imports resolve from
    /// <paramref name="baseDirectory"/> when one is given, then from this assembly.
    /// </summary>
    public static EditStylesheet FromXml(string xml, string? baseDirectory = null, string name = "(inline)")
    {
        ArgumentNullException.ThrowIfNull(xml);

        return new EditStylesheet(name, () =>
        {
            using var reader = new StringReader(xml);
            using XmlReader xmlReader = XmlReader.Create(reader, ReaderSettings,
                EmbeddedBaseUri + "inline.xsl");

            var xslt = new XslCompiledTransform();
            xslt.Load(xmlReader, XsltSettings.Default, new EditStylesheetResolver(baseDirectory));
            return xslt;
        });
    }

    /// <summary>
    /// A transform the caller compiled. The escape hatch: extension objects, a
    /// script block, an XSLT 2.0 processor's output — anything this class's own
    /// loading does not do.
    /// </summary>
    public static EditStylesheet FromTransform(XslCompiledTransform compiled, string name = "(compiled)")
    {
        ArgumentNullException.ThrowIfNull(compiled);
        return new EditStylesheet(name, () => compiled);
    }

    /// <summary>The compiled transform, compiled on first use and held after.</summary>
    internal XslCompiledTransform Compiled
    {
        get
        {
            // Double-checked rather than a Lazy<T>: a Lazy would hold the closure
            // for the life of the object, and the closure holds the file's bytes.
            if (_compiled is not null)
            {
                return _compiled;
            }

            lock (_gate)
            {
                return _compiled ??= _compile();
            }
        }
    }

    private static XslCompiledTransform Compile(Stream stream, string baseUri, string? baseDirectory)
    {
        using XmlReader reader = XmlReader.Create(stream, ReaderSettings, baseUri);

        var xslt = new XslCompiledTransform();
        xslt.Load(reader, XsltSettings.Default, new EditStylesheetResolver(baseDirectory));
        return xslt;
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// Resolves <c>xsl:import</c> and <c>xsl:include</c> for an editing stylesheet:
/// the including stylesheet's own directory first, then this assembly's
/// <c>Resources/editing/</c>.
///
/// The fallback is what makes a house stylesheet worth writing. Without it, a
/// project wanting to change how one element is projected would have to copy a
/// thousand lines of XSLT to change ten of them, and would then own the copy.
/// </summary>
internal sealed class EditStylesheetResolver(string? baseDirectory) : XmlResolver
{
    public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
    {
        // A relative href against a file:// base resolves to a file:// URI, which
        // GetEntity will look for on disk. Against the embedded base it stays in the
        // s1kd-editing scheme and is looked up by name.
        return new Uri(baseUri ?? new Uri(EditStylesheet.EmbeddedBaseUri), relativeUri ?? string.Empty);
    }

    public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        ArgumentNullException.ThrowIfNull(absoluteUri);

        if (absoluteUri.IsFile && File.Exists(absoluteUri.LocalPath))
        {
            return File.OpenRead(absoluteUri.LocalPath);
        }

        string name = Path.GetFileName(absoluteUri.LocalPath);

        // A stylesheet loaded from a string has no directory of its own, so a
        // caller can name one; a stylesheet on disk has already been tried above.
        if (baseDirectory is not null)
        {
            string beside = Path.Combine(baseDirectory, name);
            if (File.Exists(beside))
            {
                return File.OpenRead(beside);
            }
        }

        return EmbeddedResources.Open("editing/" + name)
            ?? throw new FileNotFoundException(
                $"Editing stylesheet '{name}' was not found beside the stylesheet that " +
                $"references it, and this library embeds none by that name.", name);
    }
}
