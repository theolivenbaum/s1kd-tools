using System.Reflection;
using System.Xml;

namespace S1kdTools;

/// <summary>
/// Loads files embedded under the project's <c>Resources/</c> directory. The C
/// tools embed their templates, XSLT and data files into each executable with
/// <c>xxd -i</c>; in the .NET port those files live under
/// <c>src/S1kdTools.Core/Resources/&lt;tool&gt;/…</c> and are embedded via a
/// wildcard in the csproj.
///
/// Resources are addressed by their path relative to <c>Resources/</c>, using
/// forward slashes, e.g. <c>"newdm/descript.xml"</c>. Lookups match by manifest
/// suffix so they are independent of the assembly's root namespace.
/// </summary>
public static class EmbeddedResources
{
    private static readonly Assembly Asm = typeof(EmbeddedResources).Assembly;

    private static string Suffix(string relativePath) =>
        ".Resources." + relativePath.Replace('\\', '/').Replace('/', '.');

    /// <summary>Open an embedded resource stream, or null if not found.</summary>
    public static Stream? Open(string relativePath)
    {
        string suffix = Suffix(relativePath);
        string? name = Asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
        return name == null ? null : Asm.GetManifestResourceStream(name);
    }

    /// <summary>Whether an embedded resource exists.</summary>
    public static bool Exists(string relativePath) => Open(relativePath) != null;

    /// <summary>Read an embedded resource as text (UTF-8), or throw if missing.</summary>
    public static string ReadText(string relativePath)
    {
        using Stream stream = Open(relativePath)
            ?? throw new FileNotFoundException($"Embedded resource not found: {relativePath}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Read an embedded resource as bytes, or throw if missing.</summary>
    public static byte[] ReadBytes(string relativePath)
    {
        using Stream stream = Open(relativePath)
            ?? throw new FileNotFoundException($"Embedded resource not found: {relativePath}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Load an embedded XML resource into a document.</summary>
    public static XmlDocument LoadXml(string relativePath)
    {
        using Stream stream = Open(relativePath)
            ?? throw new FileNotFoundException($"Embedded resource not found: {relativePath}");
        return XmlUtils.ReadStream(stream);
    }

    /// <summary>
    /// List the relative paths (under <c>Resources/</c>) of embedded resources
    /// whose path begins with <paramref name="prefix"/> (e.g. a tool name).
    /// </summary>
    public static IEnumerable<string> List(string prefix)
    {
        const string marker = ".Resources.";
        string normalized = prefix.Replace('\\', '/').Replace('/', '.');
        foreach (string n in Asm.GetManifestResourceNames())
        {
            int idx = n.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                continue;
            }
            string rel = n[(idx + marker.Length)..];
            if (rel.StartsWith(normalized, StringComparison.Ordinal))
            {
                yield return rel;
            }
        }
    }
}
