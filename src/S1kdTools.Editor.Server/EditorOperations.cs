using S1kdTools.Editing;

namespace S1kdTools.Editor.Server;

/// <summary>
/// Everything the editor's endpoints do, as calls rather than as routes.
///
/// <b>This is the half of this package you hook.</b> <see cref="EditorEndpoints"/>
/// maps the endpoints this library happens to define; every one of them is a
/// single call on this class and nothing else. An application that wants its
/// editor reached some other way - its own paths, a controller, an authorization
/// filter per operation, a gRPC service, a queue - takes this and maps it however
/// it likes, without re-deriving anything:
///
/// <code>
/// app.MapGet("/csdb/{id}/edit", (string id, EditorOperations editor) =&gt; editor.Read(id));
/// </code>
///
/// Most of it forwards straight to <see cref="CsdbLibrary"/>, which is the session
/// store. What is here and not there is the handful of operations that are a
/// composition rather than a call - the palette for one object, the check, the page
/// - because those were spelled inline in the endpoints, which meant anyone mapping
/// their own had to know how to spell them too.
/// </summary>
public sealed class EditorOperations(
    CsdbLibrary library,
    DocumentCheck check,
    EditorPresentation? presentation = null)
{
    /// <summary>The CSDB and its open sessions.</summary>
    public CsdbLibrary Library => library;

    /// <summary>Whether this server can lay a document out at all.</summary>
    public bool CanRenderPdf => presentation is not null;

    /// <summary>Every CSDB object on offer.</summary>
    public IReadOnlyList<DocumentSummary> List() => library.List();

    /// <summary>Read an object without changing it.</summary>
    public EditorState Read(string id) => library.Read(id);

    /// <summary>Apply a batch of edits as one undoable step.</summary>
    public EditorState Apply(string id, IReadOnlyList<EditCommand> commands) =>
        library.Apply(id, commands);

    /// <summary>Replace the whole source.</summary>
    public EditorState SetXml(string id, string xml) => library.SetXml(id, xml);

    /// <summary>Reverse the last edit.</summary>
    public EditorState Undo(string id) => library.Undo(id);

    /// <summary>Reapply the last undone edit.</summary>
    public EditorState Redo(string id) => library.Redo(id);

    /// <summary>Throw the session away and read the object from the CSDB again.</summary>
    public EditorState Revert(string id) => library.Revert(id);

    /// <summary>Write the object out.</summary>
    public EditorState Save(string id) => library.Save(id);

    /// <summary>
    /// The whole component catalogue: everything this editor's vocabulary knows how
    /// to build, with no object in mind.
    /// </summary>
    public IReadOnlyList<PaletteEntry> Palette() => EditPalette.Build(library.Profile);

    /// <summary>
    /// The catalogue narrowed to what one object can actually take, which is what a
    /// component rail should show.
    ///
    /// A procedure takes most of the catalogue and a publication module almost none
    /// of it, so a rail built from <see cref="Palette()"/> offers a publication
    /// module components that refuse every drop without saying why.
    /// </summary>
    public IReadOnlyList<PaletteEntry> Palette(string id) =>
        EditPalette.Build(library.Read(id).Model, library.Profile);

    /// <summary>
    /// Check an object: well-formedness, business rules, and whether it can be laid
    /// out.
    ///
    /// Against what the session holds rather than what is on disk, because an
    /// author wants to know about the module they are writing.
    /// </summary>
    public CheckReport Check(string id)
    {
        EditorState state = library.Read(id);
        return check.Check(state.Xml, state.Schema, state.Title);
    }

    /// <summary>
    /// Lay an object out as the page it will be published as.
    ///
    /// From what the editor holds rather than from what is on disk: an author who
    /// has just moved a warning wants to see it move.
    /// </summary>
    /// <returns>
    /// The PDF and what to call it, or null when this server was started without
    /// presentation stylesheets - which is a supported way to run, not a failure.
    /// </returns>
    public EditorPdf? RenderPdf(string id)
    {
        if (presentation is null)
        {
            return null;
        }

        EditorState state = library.Read(id);
        return new EditorPdf(
            presentation.RenderPdf(state.Xml, state.Schema, state.Title),
            $"{state.Code}.pdf");
    }
}

/// <summary>A laid-out page, and the name a browser should save it under.</summary>
/// <param name="Content">The PDF.</param>
/// <param name="FileName">What to call it.</param>
public sealed record EditorPdf(byte[] Content, string FileName);
