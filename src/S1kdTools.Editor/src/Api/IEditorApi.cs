using System.Threading.Tasks;

namespace S1kdTools.Editor
{
    /// <summary>
    /// Every call the editor makes, as a handler a host can supply.
    ///
    /// <b>This is the seam between the components and whatever is behind them.</b>
    /// The default implementation, <see cref="HttpEditorApi"/>, is fetch over the
    /// endpoints <c>S1kdTools.Editor.Server</c> maps, and most applications want
    /// that with at most a different prefix - which is <see cref="EditorRoutes"/>
    /// and needs nothing implemented here.
    ///
    /// Implement this when the calls themselves have to be different: a back end
    /// whose editing API is not these endpoints, an RPC or socket transport, a
    /// host that has the CSDB in the page already, a test double. The components
    /// reach the server only through here, so an application that satisfies it
    /// satisfies all of them.
    ///
    /// <b>Every method that returns a state returns the whole projection.</b> A
    /// block's path is only meaningful against the revision it came from, so an
    /// implementation must answer with the document as it now stands rather than
    /// with a delta - see <see cref="IEditBlock.path"/>. Returning null says the
    /// call failed and has already been reported.
    /// </summary>
    public interface IEditorApi
    {
        /// <summary>Every CSDB object on offer.</summary>
        Task<IDocumentSummary[]> ListAsync();

        /// <summary>
        /// The components an author can add, each with the block it projects as.
        ///
        /// <paramref name="documentId"/> is null before anything is open, which
        /// asks for the whole catalogue; with one open the answer is what that
        /// object can actually take, which is what a component rail should show.
        /// </summary>
        Task<IPaletteEntry[]> PaletteAsync(string documentId);

        /// <summary>Read an object without changing it.</summary>
        Task<IEditorState> ReadAsync(string id);

        /// <summary>Apply a batch of edits as one undoable step.</summary>
        Task<IEditorState> ApplyAsync(string id, EditCommand[] commands);

        /// <summary>Replace the whole source - what a code editor saves.</summary>
        Task<IEditorState> SetXmlAsync(string id, string xml);

        /// <summary>
        /// One of the session commands: <c>undo</c>, <c>redo</c>, <c>revert</c> or
        /// <c>save</c>.
        ///
        /// One method rather than four, because they differ only in the word: an
        /// implementation that routes them somewhere else routes them the same way,
        /// and a host adding a fifth does not need this interface changed.
        /// </summary>
        Task<IEditorState> SessionAsync(string id, string action);

        /// <summary>Well-formedness, business rules, and whether it can be laid out.</summary>
        Task<ICheckReport> CheckAsync(string id);

        /// <summary>
        /// Where the laid-out page can be fetched from, as of
        /// <paramref name="revision"/>.
        ///
        /// A URL rather than bytes because the preview hands it to a viewer that
        /// does its own fetching, and because that is what lets the browser stream
        /// a large page rather than hold it. A host with no such URL - one keeping
        /// the PDF in memory - answers with a blob: URL it made itself.
        /// </summary>
        string PdfUrl(string id, int revision);
    }
}
