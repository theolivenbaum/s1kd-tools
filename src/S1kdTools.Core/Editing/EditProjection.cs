using System.Xml;

namespace S1kdTools.Editing;

/// <summary>
/// Projects a CSDB object into an <see cref="EditDocument"/> by running an editing
/// stylesheet over it.
///
/// The projection is XSLT rather than a hand-written walk of the DOM for the same
/// reason the presentation layer is: <b>which parts of an object are editable, and
/// what they look like, is a publishing decision, not a program's</b>. A project
/// with its own house rules — a schema this port has never seen, a rule that a
/// certain step's title is never retyped — writes a stylesheet that imports ours,
/// overrides the templates it disagrees with, and passes it on an
/// <see cref="EditProfile"/>. No C# is rebuilt and nothing is forked; see
/// <see cref="EditStylesheet"/> for what that looks like.
///
/// What the C# keeps is the half XSLT cannot do: resolving a path back to a node
/// and writing to it (<see cref="EditCommands"/>), and holding the document across
/// a sequence of edits (<see cref="EditSession"/>).
/// </summary>
public static class EditProjection
{
    /// <summary>Project <paramref name="doc"/> into an editable model.</summary>
    /// <param name="doc">The CSDB object.</param>
    /// <param name="profile">
    /// Which dialect to project with. <see cref="EditProfile.Default"/> when null.
    /// </param>
    public static EditDocument Project(XmlDocument doc, EditProfile? profile = null)
    {
        return Parse(Transform(doc, profile));
    }

    /// <summary>Run the editing stylesheet and return its raw output, for tests
    /// and for callers writing their own reader.</summary>
    public static XmlDocument Transform(XmlDocument doc, EditProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var output = XmlUtils.NewDocument();
        using (var ms = new MemoryStream())
        {
            using (XmlWriter writer = XmlWriter.Create(ms, new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Auto,
                OmitXmlDeclaration = true,
            }))
            {
                (profile ?? EditProfile.Default).Stylesheet.Compiled.Transform(doc, null, writer);
            }

            ms.Position = 0;
            output.Load(ms);
        }

        return output;
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
