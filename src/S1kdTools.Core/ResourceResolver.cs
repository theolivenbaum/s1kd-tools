namespace S1kdTools;

/// <summary>
/// Where a name a CSDB object refers to — an imported stylesheet, an illustration,
/// anything else fetched by name — turns into bytes.
///
/// The tools resolve names from disk by default, because that is where a CSDB
/// usually is. A CSDB that lives somewhere else — a content management system, an
/// object store, a database, a zip — supplies one of these instead, and nothing
/// else changes:
///
/// <code>
/// var icns = ResourceResolvers.FromDelegate(name =>
///     _store.TryOpen("icn/" + name, out Stream? s) ? s : null);
/// </code>
///
/// Only <see cref="Open"/> has to be implemented. <see cref="LocalPath"/> is an
/// optimisation with a default of "there isn't one".
/// </summary>
public interface IResourceResolver
{
    /// <summary>
    /// Open <paramref name="name"/>, or return null when this resolver does not
    /// have it. The caller disposes the stream.
    /// </summary>
    /// <param name="name">
    /// The name as the document refers to it — a stylesheet's file name, an ICN's
    /// identifier. Treat it as untrusted: it came out of a file.
    /// </param>
    Stream? Open(string name);

    /// <summary>
    /// A path on the local file system for <paramref name="name"/>, when there
    /// happens to be one. Null otherwise, which is the default.
    ///
    /// It is an optimisation, and only that. A resolver that already has the file
    /// on disk says so here, and its bytes are never read into memory: the name is
    /// passed on and whatever opens it opens the file. A resolver that answers null
    /// has its bytes read once and held for as long as the operation needs them,
    /// which for a page preview is one layout.
    /// </summary>
    string? LocalPath(string name) => null;
}

/// <summary>The resolvers this library ships, and how to combine them.</summary>
public static class ResourceResolvers
{
    /// <summary>A resolver that has nothing. Useful as a default and in tests.</summary>
    public static IResourceResolver None { get; } = new NoResources();

    /// <summary>
    /// Files in one or more directories, searched in order.
    ///
    /// <paramref name="extensions"/> is for names that arrive without one: an ICN
    /// is referenced as <c>ICN-AE100-A-278100-A-U8025-00001-A-001-01</c> and stored
    /// as that with <c>.PNG</c> after it. Each extension is tried in the order
    /// given, upper- and lower-case, so a raster wins over a vector when both are
    /// present and the list says so.
    /// </summary>
    public static IResourceResolver Directory(IEnumerable<string> directories,
        IEnumerable<string>? extensions = null) =>
        new FileResources(directories, extensions);

    /// <inheritdoc cref="Directory(IEnumerable{string}, IEnumerable{string})"/>
    public static IResourceResolver Directory(params string[] directories) =>
        new FileResources(directories, null);

    /// <summary>
    /// Files embedded in this assembly under <c>Resources/</c>.
    /// </summary>
    /// <param name="prefix">A folder within <c>Resources/</c>, e.g. <c>editing</c>.</param>
    public static IResourceResolver Embedded(string prefix = "") => new EmbeddedResourceSet(prefix);

    /// <summary>
    /// The first resolver that has the name wins. Nulls are skipped, so a caller
    /// can compose optional layers without checking each one.
    /// </summary>
    public static IResourceResolver Compose(params IResourceResolver?[] resolvers) =>
        new CompositeResources([.. resolvers.Where(r => r is not null)!]);

    /// <summary>A resolver from a function, for the common case of a one-liner.</summary>
    public static IResourceResolver FromDelegate(Func<string, Stream?> open) =>
        new DelegateResources(open ?? throw new ArgumentNullException(nameof(open)));

    private sealed class NoResources : IResourceResolver
    {
        public Stream? Open(string name) => null;
    }

    private sealed class DelegateResources(Func<string, Stream?> open) : IResourceResolver
    {
        public Stream? Open(string name) => open(name);
    }

    private sealed class CompositeResources(IReadOnlyList<IResourceResolver> resolvers) : IResourceResolver
    {
        public Stream? Open(string name)
        {
            foreach (IResourceResolver resolver in resolvers)
            {
                Stream? stream = resolver.Open(name);
                if (stream is not null)
                {
                    return stream;
                }
            }
            return null;
        }

        public string? LocalPath(string name)
        {
            foreach (IResourceResolver resolver in resolvers)
            {
                // The first resolver that *has* the name decides, whether or not it
                // has a path for it. Asking the rest would answer with a path to a
                // different file from the one Open would return.
                if (resolver.LocalPath(name) is string path)
                {
                    return path;
                }

                using Stream? stream = resolver.Open(name);
                if (stream is not null)
                {
                    return null;
                }
            }
            return null;
        }
    }

    private sealed class EmbeddedResourceSet(string prefix) : IResourceResolver
    {
        public Stream? Open(string name) => EmbeddedResources.Open(Combine(name));

        private string Combine(string name) =>
            prefix.Length == 0 ? name : prefix.TrimEnd('/') + "/" + name;
    }

    private sealed class FileResources : IResourceResolver
    {
        private readonly string[] _directories;
        private readonly string[] _extensions;

        internal FileResources(IEnumerable<string> directories, IEnumerable<string>? extensions)
        {
            ArgumentNullException.ThrowIfNull(directories);

            _directories = [.. directories.Where(d => !string.IsNullOrWhiteSpace(d))
                                          .Select(Path.GetFullPath)];
            _extensions = extensions is null ? [] : [.. extensions];
        }

        public Stream? Open(string name) =>
            LocalPath(name) is string path ? File.OpenRead(path) : null;

        public string? LocalPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // The name came out of a document, and it is about to be joined to a
            // directory. Only the leaf is used, so a crafted reference cannot read
            // its way out of the folder it was pointed at.
            string leaf = Path.GetFileName(name);
            if (leaf.Length == 0)
            {
                return null;
            }

            foreach (string directory in _directories)
            {
                string candidate = Path.Combine(directory, leaf);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                foreach (string extension in _extensions)
                {
                    // Case is a real problem here rather than a theoretical one:
                    // S1000D writes ICN identifiers in upper case and the files
                    // beside them are named either way, on file systems that may or
                    // may not care.
                    string upper = Path.Combine(directory, leaf + extension.ToUpperInvariant());
                    if (File.Exists(upper))
                    {
                        return upper;
                    }

                    string lower = Path.Combine(directory, leaf + extension.ToLowerInvariant());
                    if (File.Exists(lower))
                    {
                        return lower;
                    }
                }
            }

            return null;
        }
    }
}

/// <summary>
/// An <see cref="System.Xml.XmlResolver"/> over an <see cref="IResourceResolver"/>,
/// for the hrefs a stylesheet reaches for: <c>xsl:import</c>, <c>xsl:include</c>,
/// <c>document()</c>.
///
/// The resolver is asked first, by leaf name. Only if it does not have the name is
/// the file system tried, and only when the href resolved to a <c>file://</c> URI
/// in the first place — which it does when the stylesheet itself came from disk,
/// and does not when it came from a stream.
/// </summary>
public sealed class ResourceXmlResolver(IResourceResolver resources, bool allowFileSystem = true)
    : System.Xml.XmlResolver
{
    /// <summary>The scheme given to a stylesheet that has no address of its own.</summary>
    public const string OpaqueBaseUri = "s1kd-resource:///";

    /// <inheritdoc/>
    public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
        new(baseUri ?? new Uri(OpaqueBaseUri), relativeUri ?? string.Empty);

    /// <inheritdoc/>
    public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        ArgumentNullException.ThrowIfNull(absoluteUri);

        string name = Path.GetFileName(absoluteUri.LocalPath);

        if (resources.Open(name) is Stream stream)
        {
            return stream;
        }

        if (allowFileSystem && absoluteUri.IsFile && File.Exists(absoluteUri.LocalPath))
        {
            return File.OpenRead(absoluteUri.LocalPath);
        }

        throw new FileNotFoundException(
            $"'{name}' was not found by the resource resolver.", name);
    }
}
