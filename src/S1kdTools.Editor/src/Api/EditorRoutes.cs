using static Transpose.Core.es5;

namespace S1kdTools.Editor
{
    /// <summary>
    /// Where the editor's operations live, as URLs.
    ///
    /// <b>The front end used to spell <c>/api</c> into eight of its own methods</b>,
    /// while the back end had a <see cref="!:EditorOptions.RoutePrefix"/> anyone
    /// could set. The two could not disagree out loud: move the prefix on the
    /// server and every call from the browser answered 404, with nothing in either
    /// half admitting it had an opinion about the other's naming. Naming belongs in
    /// one place, and this is it.
    ///
    /// The ordinary case is a different prefix, which is a constructor argument:
    ///
    /// <code>
    /// var client = new EditorClient(routes: new EditorRoutes("/editor-api"));
    /// </code>
    ///
    /// A back end whose paths are shaped differently altogether - <c>/csdb/{id}/edit</c>
    /// rather than <c>/documents/{id}/commands</c> - subclasses and overrides the
    /// members it spells differently. Replacing how a call is *made*, rather than
    /// where it goes, is <see cref="IEditorApi"/> instead.
    /// </summary>
    public class EditorRoutes
    {
        private readonly string _baseUrl;
        private readonly string _prefix;

        /// <summary>The routes this library assumes, and what every overload defaults to.</summary>
        /// <param name="prefix">
        /// What the back end mapped its editor group under. Must match
        /// <c>EditorOptions.RoutePrefix</c>, whose default is the same.
        /// </param>
        /// <param name="baseUrl">
        /// Where the back end is. Empty - the default - means the origin this page
        /// was served from, which is the case when one server hosts both.
        /// </param>
        public EditorRoutes(string prefix = "/api", string baseUrl = "")
        {
            _prefix = Trim(prefix);
            _baseUrl = Trim(baseUrl);
        }

        /// <summary>Every CSDB object the back end offers.</summary>
        public virtual string Documents()
        {
            return Url("/documents");
        }

        /// <summary>One object: its source, its projection and its history.</summary>
        public virtual string Document(string id)
        {
            return Url("/documents/" + Escape(id));
        }

        /// <summary>The whole component catalogue, with no object in mind.</summary>
        public virtual string Palette()
        {
            return Url("/palette");
        }

        /// <summary>The catalogue narrowed to what one object can take.</summary>
        public virtual string Palette(string id)
        {
            return Url("/documents/" + Escape(id) + "/palette");
        }

        /// <summary>Where a batch of edits is posted.</summary>
        public virtual string Commands(string id)
        {
            return Url("/documents/" + Escape(id) + "/commands");
        }

        /// <summary>Where the whole source is put.</summary>
        public virtual string Xml(string id)
        {
            return Url("/documents/" + Escape(id) + "/xml");
        }

        /// <summary>One of undo, redo, revert or save.</summary>
        public virtual string Session(string id, string action)
        {
            return Url("/documents/" + Escape(id) + "/" + action);
        }

        /// <summary>Well-formedness, business rules, and whether it can be laid out.</summary>
        public virtual string Check(string id)
        {
            return Url("/documents/" + Escape(id) + "/check");
        }

        /// <summary>
        /// The laid-out page.
        ///
        /// <paramref name="revision"/> is stamped into the query string because the
        /// preview is of a document being typed into, and the one thing a cached
        /// page must never do is look like the current one.
        /// </summary>
        public virtual string Pdf(string id, int revision)
        {
            return Url("/documents/" + Escape(id) + "/pdf?r=" + revision);
        }

        /// <summary>A path under the prefix, as an absolute URL.</summary>
        protected string Url(string path)
        {
            return _baseUrl + _prefix + path;
        }

        /// <summary>A value safe to put in a path segment.</summary>
        protected static string Escape(string value)
        {
            return encodeURIComponent(value ?? "");
        }

        private static string Trim(string value)
        {
            string trimmed = (value ?? "").TrimEnd('/');
            if (trimmed.Length > 0 && trimmed[0] != '/' && trimmed.IndexOf("://") < 0)
            {
                trimmed = "/" + trimmed;
            }

            return trimmed;
        }
    }
}
