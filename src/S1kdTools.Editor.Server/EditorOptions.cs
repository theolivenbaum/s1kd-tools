using S1kdTools.Editing;

namespace S1kdTools.Editor.Server;

/// <summary>
/// What an editor back-end needs to know: where the objects are, where the page
/// layouts are, and which S1000D dialect to speak.
///
/// Deliberately a handful of settings and no more. Everything else an editor does
/// — what is editable, what may be inserted, how a page is laid out — is a
/// stylesheet or a catalogue, and belongs in <see cref="Profile"/> or in the
/// presentation stylesheets rather than as another knob here.
///
/// The directory settings are the common case written short. Each has an
/// <see cref="IResourceResolver"/> beside it for a CSDB that is not a folder of
/// files, and the resolver wins when both are given.
/// </summary>
public sealed class EditorOptions
{
    /// <summary>
    /// The CSDB: the folder of objects the editor opens. Every <c>.xml</c> in it is
    /// offered, addressed by its file name without the extension.
    /// </summary>
    public required string CsdbDirectory { get; init; }

    /// <summary>
    /// Where <c>save</c> writes. Defaults to <see cref="CsdbDirectory"/> — a server
    /// that owns its CSDB saves back over it; a demonstration points this somewhere
    /// else so its checked-in objects do not drift.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// The XSL-FO presentation stylesheets the page preview lays out with, one per
    /// CSDB object type, named for the schema they present (<c>proced.xsl</c>).
    ///
    /// Null — and no <see cref="PresentationStylesheets"/> — means no page preview:
    /// <c>GET …/pdf</c> answers 404 and the check says
    /// so rather than failing. That is a supported way to run — an editor without a
    /// page is still an editor — and it is what a project that has not written its
    /// house style yet gets.
    /// </summary>
    public string? PresentationDirectory { get; init; }

    /// <summary>
    /// Where the ICNs are, for the page preview. Defaults to
    /// <see cref="CsdbDirectory"/>, which is where a flat CSDB keeps them.
    /// </summary>
    public string? GraphicsDirectory { get; init; }

    /// <summary>
    /// The stylesheet and vocabulary every session edits in.
    /// <see cref="EditProfile.Default"/> when null.
    /// </summary>
    public EditProfile? Profile { get; init; }

    /// <summary>
    /// Where the presentation stylesheets come from, when they are not a folder of
    /// files: a content management system, an object store, a zip. Takes precedence
    /// over <see cref="PresentationDirectory"/>, and setting either one turns the
    /// page preview on.
    ///
    /// A stylesheet's own <c>xsl:import</c> hrefs are asked of this same resolver
    /// by name, so a house style is still one <c>common.xsl</c> and thirty short
    /// stylesheets over it.
    /// </summary>
    public IResourceResolver? PresentationStylesheets { get; init; }

    /// <summary>
    /// Where an ICN identifier turns into image bytes, when the illustrations are
    /// not a folder of files. Takes precedence over <see cref="GraphicsDirectory"/>.
    ///
    /// An illustration this hands over as a stream is written to a temporary file
    /// for the length of one layout and deleted after: the XSL-FO engine resolves
    /// an <c>external-graphic</c> by file path and treats a <c>data:</c> URI as a
    /// missing image. A resolver that already has the file on disk says so through
    /// <see cref="IResourceResolver.LocalPath"/> and nothing is copied.
    /// </summary>
    public IResourceResolver? Graphics { get; init; }

    /// <summary>The route the endpoints are mapped under.</summary>
    public string RoutePrefix { get; init; } = "/api";
}
