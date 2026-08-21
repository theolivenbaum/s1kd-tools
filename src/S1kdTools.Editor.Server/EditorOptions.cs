using S1kdTools.Editing;

namespace S1kdTools.Editor.Server;

/// <summary>
/// What an editor back-end needs to know: where the objects are, where the page
/// layouts are, and which S1000D dialect to speak.
///
/// Deliberately four settings and no more. Everything else an editor does — what
/// is editable, what may be inserted, how a page is laid out — is a stylesheet or
/// a catalogue, and belongs in <see cref="Profile"/> or in
/// <see cref="PresentationDirectory"/> rather than as another knob here.
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
    /// Null means no page preview: <c>GET …/pdf</c> answers 404 and the check says
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

    /// <summary>The route the endpoints are mapped under.</summary>
    public string RoutePrefix { get; init; } = "/api";
}
