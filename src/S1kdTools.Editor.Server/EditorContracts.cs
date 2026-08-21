using S1kdTools.Editing;

namespace S1kdTools.Editor.Server;

/// <summary>
/// The wire shapes. Every editing endpoint answers with an
/// <see cref="EditorState"/> — the whole of what the editor draws — rather than
/// with a delta.
///
/// A delta protocol would be smaller and would be wrong here. A block's path is
/// only valid against the revision it was projected from (see
/// <see cref="EditDocument"/>), so a client applying a patch to a model it already
/// holds would be reasoning about paths the server has already renumbered.
/// Answering with the whole state makes the client's model and the server's
/// document the same thing by construction, and a data module's projection is a
/// few tens of kilobytes of JSON.
/// </summary>
/// <param name="Id">The document's identifier: its CSDB file name without the extension.</param>
/// <param name="FileName">The file it was read from.</param>
/// <param name="Code">The data module or publication module code.</param>
/// <param name="Title">The object's title.</param>
/// <param name="ObjectType">A human-readable object type, e.g. <c>Procedure</c>.</param>
/// <param name="Schema">The schema it declares, e.g. <c>proced</c>.</param>
/// <param name="Dirty">Whether the session holds edits that are not on disk.</param>
public sealed record DocumentSummary(
    string Id,
    string FileName,
    string Code,
    string Title,
    string ObjectType,
    string Schema,
    bool Dirty);

/// <summary>What can be undone or redone, for the toolbar's two buttons.</summary>
/// <param name="Depth">How many steps are on the stack.</param>
/// <param name="Label">What the next step would do, or null when the stack is empty.</param>
public sealed record HistoryState(int Depth, string? Label);

/// <summary>
/// One open document, in full: the projection the WYSIWYG surface draws, the
/// source the code editor shows, and the history the toolbar reflects — always
/// from the same revision, because they are read from one document in one call.
/// </summary>
public sealed record EditorState(
    string Id,
    string FileName,
    string Code,
    string Title,
    string ObjectType,
    string Schema,
    string Xml,
    EditDocument Model,
    HistoryState Undo,
    HistoryState Redo,
    bool Dirty,
    string? SavedAt);

/// <summary>A batch of edits, applied as one undoable step.</summary>
public sealed class CommandsRequest
{
    /// <summary>The edits, in order.</summary>
    public List<EditCommand> Commands { get; set; } = [];
}

/// <summary>The whole source, as the code editor holds it.</summary>
public sealed class XmlRequest
{
    /// <summary>The document text.</summary>
    public string Xml { get; set; } = "";
}

/// <summary>
/// One thing wrong with the document, as far up the chain as it got.
/// </summary>
/// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
/// <param name="Source">Which check found it: <c>xml</c>, <c>brex</c> or <c>render</c>.</param>
/// <param name="Message">What is wrong, in the words of whatever found it.</param>
/// <param name="Path">
/// The XPath of the offending element, in the same shape a block carries — so the
/// editor can scroll to the block a BREX rule is complaining about rather than
/// leaving the author to find it.
/// </param>
/// <param name="Rule">The BREX rule's own explanation of itself, when there is one.</param>
public sealed record CheckFinding(
    string Severity,
    string Source,
    string Message,
    string? Path,
    string? Rule);

/// <summary>The result of checking a document.</summary>
/// <param name="Ok">Whether nothing of severity <c>error</c> was found.</param>
/// <param name="Brex">The BREX data module the content was checked against.</param>
/// <param name="Findings">What was found, errors first.</param>
public sealed record CheckReport(bool Ok, string? Brex, IReadOnlyList<CheckFinding> Findings);

/// <summary>A refused request, in the words the author needs to act on it.</summary>
/// <param name="Error">What went wrong.</param>
public sealed record ErrorResponse(string Error);
