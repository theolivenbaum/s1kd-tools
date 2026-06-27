using System.Xml;

namespace S1kdTools.DocBook;

/// <summary>
/// An <see cref="XmlResolver"/> that serves XSLT <c>xsl:include</c>/<c>xsl:import</c>
/// references from embedded resources rather than the filesystem.
///
/// The Smart Avionics <c>s1000dtodb.xsl</c> stylesheet pulls in eleven sibling
/// files via <c>xsl:include</c>. When the stylesheet is loaded from the assembly
/// manifest there is no directory for the compiler to resolve those relative
/// hrefs against, so we give the loader a synthetic base URI
/// (<c>s1kdres://&lt;dir&gt;/&lt;file&gt;</c>) and map every resolved URI back to
/// an embedded resource under <c>Resources/&lt;dir&gt;/</c>.
/// </summary>
public sealed class EmbeddedXslResolver : XmlResolver
{
    public const string Scheme = "s1kdres";

    private readonly string _resourceDir;

    /// <param name="resourceDir">
    /// Resource directory the stylesheet and its includes live in, relative to
    /// <c>Resources/</c> (e.g. <c>"s1000dtodb"</c>).
    /// </param>
    public EmbeddedXslResolver(string resourceDir) => _resourceDir = resourceDir.Trim('/');

    /// <summary>The base URI to hand the XML reader when loading the main sheet.</summary>
    public Uri BaseUriFor(string fileName) =>
        new($"{Scheme}://{_resourceDir}/{fileName}");

    public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
    {
        // Relative includes resolve against the synthetic base; the default
        // base-relative combination keeps the s1kdres:// scheme intact.
        if (baseUri != null && baseUri.Scheme == Scheme && !string.IsNullOrEmpty(relativeUri))
            return new Uri(baseUri, relativeUri);
        return base.ResolveUri(baseUri, relativeUri);
    }

    public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        if (absoluteUri.Scheme != Scheme)
            throw new XmlException($"Unexpected XSLT include URI: {absoluteUri}");

        // s1kdres://<dir>/<file>  ->  Resources/<dir>/<file>
        string dir = absoluteUri.Host;
        string file = absoluteUri.AbsolutePath.TrimStart('/');
        string relative = $"{dir}/{file}";
        return EmbeddedResources.Open(relative)
            ?? throw new FileNotFoundException($"Embedded stylesheet not found: {relative}");
    }
}
