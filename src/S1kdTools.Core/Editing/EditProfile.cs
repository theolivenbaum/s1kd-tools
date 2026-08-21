namespace S1kdTools.Editing;

/// <summary>
/// Which S1000D dialect an editor speaks: the stylesheet an object is projected
/// with, and the catalogue of what may be added to it.
///
/// The two are one decision seen from two sides — what this editor can make, and
/// how it draws what it made — and getting them out of step is the way to build an
/// editor that offers an element it cannot then show. So they travel together, and
/// everything that needs either takes a profile:
///
/// <code>
/// var profile = new EditProfile(
///     EditStylesheet.FromFile("editing/house.xsl"),
///     new HouseCatalogue());
///
/// var session = EditSession.Open("DMC-….XML", profile);
/// var palette = EditPalette.Build(profile);
/// </code>
///
/// <see cref="Default"/> is this library's own, and is what every overload falls
/// back to. A profile is immutable and its stylesheet compiles once, so an
/// application builds one at start-up and hands it around; building one per
/// request would recompile a thousand lines of XSLT per request.
/// </summary>
public sealed class EditProfile
{
    /// <summary>The stylesheet and catalogue this library ships.</summary>
    public static EditProfile Default { get; } = new();

    /// <summary>Build a profile, defaulting either half.</summary>
    /// <param name="stylesheet">The projection. <see cref="EditStylesheet.Default"/> when null.</param>
    /// <param name="templates">The vocabulary. <see cref="EditTemplateCatalogue.Default"/> when null.</param>
    public EditProfile(EditStylesheet? stylesheet = null, EditTemplateCatalogue? templates = null)
    {
        Stylesheet = stylesheet ?? EditStylesheet.Default;
        Templates = templates ?? EditTemplateCatalogue.Default;
    }

    /// <summary>How an object is projected into blocks.</summary>
    public EditStylesheet Stylesheet { get; }

    /// <summary>What may be added to an object, and what a new element is made of.</summary>
    public EditTemplateCatalogue Templates { get; }

    /// <summary>This profile with a different stylesheet.</summary>
    public EditProfile With(EditStylesheet stylesheet) => new(stylesheet, Templates);

    /// <summary>This profile with a different catalogue.</summary>
    public EditProfile With(EditTemplateCatalogue templates) => new(Stylesheet, templates);
}
