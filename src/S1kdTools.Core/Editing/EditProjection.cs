using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Editing;

/// <summary>
/// Projects a CSDB object into an <see cref="EditDocument"/> by running the
/// editing stylesheet over it.
///
/// The projection is XSLT rather than a hand-written walk of the DOM for the same
/// reason the presentation layer is: <b>which parts of an object are editable, and
/// what they look like, is a publishing decision, not a program's</b>. A project
/// with its own house rules — a schema this port has never seen, a business rule
/// that a certain step's title is never retyped — changes a stylesheet and gets a
/// different editor, with no C# rebuilt. <see cref="Transform(XmlDocument, string?)"/>
/// takes a stylesheet argument for exactly that.
///
/// What the C# keeps is the half XSLT cannot do: resolving a path back to a node
/// and writing to it (<see cref="EditCommands"/>), and holding the document across
/// a sequence of edits (<see cref="EditSession"/>).
/// </summary>
public static class EditProjection
{
    /// <summary>The stylesheet used when none is given.</summary>
    public const string DefaultStylesheet = "editing/edit.xsl";

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
    };

    /// <summary>Compiled stylesheets, keyed by resource path. Compiling one costs
    /// tens of milliseconds and an editor re-projects after every keystroke's
    /// worth of committed edit, so it is done once.</summary>
    private static readonly Dictionary<string, XslCompiledTransform> Cache = [];

    private static readonly Lock CacheLock = new();

    /// <summary>Project <paramref name="doc"/> into an editable model.</summary>
    /// <param name="doc">The CSDB object.</param>
    /// <param name="stylesheet">
    /// The embedded stylesheet to project with, relative to <c>Resources/</c>.
    /// Defaults to <see cref="DefaultStylesheet"/>.
    /// </param>
    public static EditDocument Project(XmlDocument doc, string? stylesheet = null)
    {
        XmlDocument projected = Transform(doc, stylesheet);
        return Parse(projected);
    }

    /// <summary>Run the editing stylesheet and return its raw output, for tests
    /// and for callers writing their own reader.</summary>
    public static XmlDocument Transform(XmlDocument doc, string? stylesheet = null)
    {
        XslCompiledTransform xslt = Load(stylesheet ?? DefaultStylesheet);

        var output = XmlUtils.NewDocument();
        using (var ms = new MemoryStream())
        {
            using (XmlWriter writer = XmlWriter.Create(ms, new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Auto,
                OmitXmlDeclaration = true,
            }))
            {
                xslt.Transform(doc, null, writer);
            }

            ms.Position = 0;
            output.Load(ms);
        }

        return output;
    }

    private static XslCompiledTransform Load(string resourcePath)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(resourcePath, out XslCompiledTransform? cached))
            {
                return cached;
            }

            var xslt = new XslCompiledTransform();
            var resolver = new EmbeddedStylesheetResolver();

            using (Stream stream = EmbeddedResources.Open(resourcePath)
                ?? throw new FileNotFoundException($"Embedded editing stylesheet not found: {resourcePath}"))
            using (XmlReader reader = XmlReader.Create(stream, ReaderSettings,
                       EmbeddedStylesheetResolver.BaseUri + Path.GetFileName(resourcePath)))
            {
                xslt.Load(reader, XsltSettings.Default, resolver);
            }

            Cache[resourcePath] = xslt;
            return xslt;
        }
    }

    /// <summary>
    /// Serves <c>xsl:include</c> and <c>xsl:import</c> out of the assembly's
    /// embedded resources, so the editing stylesheets can be split into a shared
    /// half and per-schema halves without ever touching the file system.
    /// </summary>
    private sealed class EmbeddedStylesheetResolver : XmlResolver
    {
        internal const string BaseUri = "s1kd-editing:///";

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
            new(baseUri ?? new Uri(BaseUri), relativeUri ?? string.Empty);

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            string name = absoluteUri.AbsolutePath.TrimStart('/');
            return EmbeddedResources.Open("editing/" + name)
                ?? throw new FileNotFoundException($"Embedded editing stylesheet not found: {name}");
        }
    }

    // ------------------------------------------------------------------------
    // reading the projection
    // ------------------------------------------------------------------------

    /// <summary>Read the stylesheet's output into the model.</summary>
    public static EditDocument Parse(XmlDocument projected)
    {
        XmlElement root = projected.DocumentElement
            ?? throw new InvalidOperationException("The editing stylesheet produced no output.");

        return new EditDocument
        {
            Root = Attr(root, "root"),
            Schema = Attr(root, "schema"),
            ObjectType = Attr(root, "objectType"),
            Code = Attr(root, "code"),
            Title = Attr(root, "title"),
            Sections = [.. Elements(root, "section").Select(ParseSection)],
        };
    }

    private static EditSection ParseSection(XmlElement element) => new()
    {
        Key = Attr(element, "key"),
        Label = Attr(element, "label"),
        Blocks = ParseBlocks(element),
    };

    private static IReadOnlyList<EditBlock> ParseBlocks(XmlElement parent)
    {
        XmlElement? holder = Elements(parent, "blocks").FirstOrDefault();
        return holder == null ? [] : [.. Elements(holder, "block").Select(ParseBlock)];
    }

    private static EditBlock ParseBlock(XmlElement element)
    {
        XmlElement? runs = Elements(element, "runs").FirstOrDefault();
        XmlElement? attrs = Elements(element, "attrs").FirstOrDefault();

        return new EditBlock
        {
            Path = Attr(element, "path"),
            Element = Attr(element, "element"),
            Kind = Attr(element, "kind"),
            Label = Attr(element, "label"),
            Heading = Attr(element, "heading"),
            Level = int.TryParse(Attr(element, "level"), out int level) ? level : 0,
            Editable = Attr(element, "editable") switch
            {
                "text" => EditMode.Text,
                "attr" => EditMode.Attr,
                _ => EditMode.None,
            },
            Placeholder = Attr(element, "placeholder"),
            AttrName = Attr(element, "attrName"),
            Value = Attr(element, "value"),
            Options = SplitOptions(Attr(element, "options")),
            CanDelete = Attr(element, "canDelete") == "1",
            CanMove = Attr(element, "canMove") == "1",
            Runs = runs == null ? [] : [.. Elements(runs, "run").Select(ParseRun)],
            Attributes = attrs == null ? [] : [.. Elements(attrs, "attr").Select(ParseAttribute)],
            Blocks = ParseBlocks(element),
        };
    }

    private static EditRun ParseRun(XmlElement element) => new()
    {
        Text = Attr(element, "text"),
        Style = Attr(element, "style"),
        Atomic = Attr(element, "atomic") == "1",
        Element = Attr(element, "element"),
        RefKind = Attr(element, "refKind"),
        Target = Attr(element, "target"),
        Src = int.TryParse(Attr(element, "src"), out int src) ? src : 0,
    };

    private static EditAttribute ParseAttribute(XmlElement element) => new()
    {
        Name = Attr(element, "name"),
        Value = Attr(element, "value"),
        Label = Attr(element, "label"),
        Type = Attr(element, "type") is { Length: > 0 } type ? type : "text",
        Options = SplitOptions(Attr(element, "options")),
    };

    private static IReadOnlyList<string> SplitOptions(string value) =>
        value.Length == 0 ? [] : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                                      StringSplitOptions.TrimEntries)];

    private static string Attr(XmlElement element, string name) =>
        element.GetAttribute(name) ?? "";

    private static IEnumerable<XmlElement> Elements(XmlElement parent, string name)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is XmlElement e && e.Name == name)
            {
                yield return e;
            }
        }
    }
}
