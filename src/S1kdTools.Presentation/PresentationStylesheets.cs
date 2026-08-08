using System.Reflection;
using System.Text;
using System.Xml;

namespace S1kdTools.Presentation;

/// <summary>
/// The presentation stylesheets embedded in this assembly, and the
/// <see cref="XmlResolver"/> that lets one stylesheet
/// <c>xsl:import</c> another out of the same assembly.
/// </summary>
public static class PresentationStylesheets
{
    private const string ResourcePrefix = "S1kdTools.Presentation.Xsl.";

    /// <summary>
    /// Base URI the stylesheets are compiled under. Imports are resolved
    /// relative to it by <see cref="Resolver"/>, never from disk.
    /// </summary>
    internal static readonly Uri BaseUri = new("file:///s1kd-presentation/");

    /// <summary>The resolver that serves <c>xsl:import</c>/<c>xsl:include</c> from the assembly.</summary>
    internal static XmlResolver Resolver { get; } = new EmbeddedResolver();

    /// <summary>Names of every embedded stylesheet, including the shared ones.</summary>
    public static IReadOnlyList<string> Names { get; } = BuildNames();

    /// <summary>Read an embedded stylesheet by file name (e.g. <c>"proced.xsl"</c>).</summary>
    /// <exception cref="FileNotFoundException">No stylesheet of that name is embedded.</exception>
    public static string Read(string name)
    {
        using Stream stream = Open(name)
            ?? throw new FileNotFoundException($"No embedded presentation stylesheet named '{name}'.", name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Open an embedded stylesheet by file name, or null when there is none.</summary>
    public static Stream? Open(string name) =>
        typeof(PresentationStylesheets).Assembly.GetManifestResourceStream(ResourcePrefix + name);

    private static string[] BuildNames()
    {
        Assembly assembly = typeof(PresentationStylesheets).Assembly;
        var names = new List<string>();
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                names.Add(resource[ResourcePrefix.Length..]);
            }
        }
        names.Sort(StringComparer.Ordinal);
        return [.. names];
    }

    private sealed class EmbeddedResolver : XmlResolver
    {
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
            new(baseUri ?? BaseUri, relativeUri ?? string.Empty);

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            ArgumentNullException.ThrowIfNull(absoluteUri);

            string name = absoluteUri.Segments.Length == 0
                ? string.Empty
                : Uri.UnescapeDataString(absoluteUri.Segments[^1]);

            return Open(name)
                ?? throw new FileNotFoundException(
                    $"The presentation stylesheet imports '{name}', which is not embedded in this assembly.", name);
        }
    }
}
