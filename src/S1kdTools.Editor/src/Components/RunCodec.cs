using System.Collections.Generic;
using Transpose.Core;
using static Transpose.Core.dom;

namespace S1kdTools.Editor
{
    /// <summary>
    /// The two directions of one translation: a block's inline content as the
    /// server describes it, and the same content as a <c>contenteditable</c>
    /// element the author types into.
    ///
    /// The whole of the editor's fidelity is in here. A run comes back from the
    /// server carrying <see cref="IEditRun.src"/> - the position of the element it
    /// was made from - and the only way the server can put that element back
    /// instead of rebuilding it is if the number survives the trip through the DOM
    /// and the author's editing of it. So every element written here carries its
    /// <c>data-src</c>, and <see cref="Read"/> reads it back off whatever survived.
    ///
    /// What that buys: an author can rewrite the sentence around a <c>dmRef</c>,
    /// split it, drag the chip to the other end - and the reference still lands in
    /// the file with its address items, its applicability and every attribute this
    /// editor has never heard of, because it was never taken apart.
    ///
    /// What is deliberately lost: markup nested inside markup. A bold run holding
    /// an italic one comes back as one bold run - <see cref="Read"/> takes an
    /// element's <c>textContent</c> rather than recursing. S1000D allows the
    /// nesting; an editor that has to explain to an author why their bold-inside-
    /// italic became something else is worse than one that never made the promise.
    /// </summary>
    internal static class RunCodec
    {
        /// <summary>Marks the elements this codec wrote, so pasted markup is told apart from ours.</summary>
        private const string SourceAttribute = "data-src";

        /// <summary>The class on an atomic run's chip.</summary>
        internal const string ChipClass = "s1kd-chip";

        /// <summary>Fill <paramref name="host"/> with the DOM for <paramref name="runs"/>.</summary>
        internal static void Write(HTMLElement host, IEditRun[] runs)
        {
            host.innerHTML = "";

            if (runs is null)
            {
                return;
            }

            for (var i = 0; i < runs.Length; i++)
            {
                IEditRun run = runs[i];

                if (run.atomic)
                {
                    host.appendChild(Chip(run));
                }
                else if (string.IsNullOrEmpty(run.style))
                {
                    host.appendChild(document.createTextNode(run.text));
                }
                else
                {
                    HTMLElement styled = document.createElement(TagFor(run.style));
                    styled.setAttribute(SourceAttribute, run.src.ToString());
                    styled.appendChild(document.createTextNode(run.text));
                    host.appendChild(styled);
                }
            }
        }

        /// <summary>
        /// Read <paramref name="host"/> back as the runs to send.
        ///
        /// Everything the author can produce in a <c>contenteditable</c> has to land
        /// somewhere: text nodes, the elements this codec wrote, the elements
        /// <c>execCommand</c> writes, the <c>&lt;br&gt;</c> a stray Enter leaves, and
        /// whatever a paste from a word processor brings. Anything unrecognised is
        /// kept as its text rather than dropped, because losing an author's words is
        /// the one failure they will not forgive.
        /// </summary>
        internal static EditRunValue[] Read(HTMLElement host)
        {
            var runs = new List<EditRunValue>();

            foreach (Node node in Children(host))
            {
                if (node.nodeType == 3)
                {
                    // A text node. Empty ones are dropped: the browser leaves them
                    // behind after a deletion and they would each become a text node
                    // in the file.
                    string text = node.nodeValue;
                    if (!string.IsNullOrEmpty(text))
                    {
                        Append(runs, EditRunValue.Plain(text));
                    }
                    continue;
                }

                var element = node as HTMLElement;
                if (element is null)
                {
                    continue;
                }

                if (IsChip(element))
                {
                    Append(runs, EditRunValue.Chip(SourceOf(element)));
                    continue;
                }

                string tag = element.tagName.ToLower();

                if (tag == "br")
                {
                    // A paragraph has no line breaks in S1000D. The author pressed
                    // Enter somewhere the editor did not turn into a new block, so it
                    // becomes the space they were most likely reaching for.
                    Append(runs, EditRunValue.Plain(" "));
                    continue;
                }

                string style = StyleFor(tag);
                string content = element.textContent;

                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                Append(runs, string.IsNullOrEmpty(style)
                    ? EditRunValue.Plain(content)
                    : EditRunValue.Styled(content, style, SourceOf(element)));
            }

            return runs.ToArray();
        }

        /// <summary>
        /// Whether what the author has left in <paramref name="host"/> differs from
        /// <paramref name="original"/>.
        ///
        /// Asked before every commit, because a blur is not an edit: an author who
        /// clicks into a paragraph, reads it and clicks away has changed nothing, and
        /// a command for that would put a step on the undo stack that undoes nothing
        /// and mark a clean document dirty.
        /// </summary>
        internal static bool Differs(HTMLElement host, IEditRun[] original)
        {
            EditRunValue[] current = Read(host);
            int originalLength = original is null ? 0 : original.Length;

            if (current.Length != originalLength)
            {
                return true;
            }

            for (var i = 0; i < current.Length; i++)
            {
                EditRunValue a = current[i];
                IEditRun b = original[i];

                if (a.atomic != b.atomic || a.src != b.src)
                {
                    return true;
                }

                if (a.atomic)
                {
                    // A chip's text is derived, not typed; only which element it is
                    // matters, and that is its src.
                    continue;
                }

                if (a.text != b.text || Normalize(a.style) != Normalize(b.style))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The plain text of a block, for a placeholder decision or a test.</summary>
        internal static string TextOf(IEditRun[] runs)
        {
            if (runs is null)
            {
                return "";
            }

            var text = "";
            for (var i = 0; i < runs.Length; i++)
            {
                text += runs[i].text;
            }
            return text;
        }

        private static HTMLElement Chip(IEditRun run)
        {
            HTMLElement chip = document.createElement("span");
            chip.className = ChipClass + " " + ChipClass + "-" + run.refKind;
            chip.setAttribute("contenteditable", "false");
            chip.setAttribute(SourceAttribute, run.src.ToString());
            chip.setAttribute("data-kind", run.refKind ?? "");

            if (!string.IsNullOrEmpty(run.target))
            {
                // The code or the id the chip stands for. A title tells the author
                // what they are looking at without the surface having to find room
                // for a data module code in the middle of a sentence.
                chip.setAttribute("title", run.target);
            }

            chip.appendChild(document.createTextNode(run.text));
            return chip;
        }

        /// <summary>
        /// Fold a run into the one before it when both are plain text.
        ///
        /// The browser splits a text node on almost every edit, so a sentence the
        /// author typed in one go arrives as a handful of adjacent nodes. Left
        /// alone they would become a handful of adjacent text nodes in the file and
        /// make every save look like a change.
        /// </summary>
        private static void Append(List<EditRunValue> runs, EditRunValue run)
        {
            if (runs.Count > 0 && !run.atomic && string.IsNullOrEmpty(run.style) && run.src == 0)
            {
                EditRunValue last = runs[runs.Count - 1];
                if (!last.atomic && string.IsNullOrEmpty(last.style) && last.src == 0)
                {
                    last.text += run.text;
                    return;
                }
            }

            runs.Add(run);
        }

        private static bool IsChip(HTMLElement element)
        {
            string className = element.className ?? "";
            return className.Contains(ChipClass);
        }

        private static int SourceOf(HTMLElement element)
        {
            string value = element.getAttribute(SourceAttribute);
            if (string.IsNullOrEmpty(value))
            {
                // Written by the browser rather than by this codec - execCommand's
                // <b>, or a paste. There is no original element to put back, so the
                // server makes a new one.
                return 0;
            }

            int parsed;
            return int.TryParse(value, out parsed) ? parsed : 0;
        }

        /// <summary>
        /// The tag a style is written as. These are also the tags
        /// <c>document.execCommand</c> produces with <c>styleWithCSS</c> off, which
        /// is what lets the browser's own bold and italic be read straight back.
        /// </summary>
        private static string TagFor(string style)
        {
            switch (style)
            {
                case RunStyles.Bold: return "b";
                case RunStyles.Italic: return "i";
                case RunStyles.Underline: return "u";
                case RunStyles.Subscript: return "sub";
                case RunStyles.Superscript: return "sup";
                case RunStyles.Code: return "code";
                default: return "span";
            }
        }

        private static string StyleFor(string tag)
        {
            switch (tag)
            {
                case "b":
                case "strong": return RunStyles.Bold;
                case "i":
                case "em": return RunStyles.Italic;
                case "u": return RunStyles.Underline;
                case "sub": return RunStyles.Subscript;
                case "sup": return RunStyles.Superscript;
                case "code":
                case "kbd":
                case "samp": return RunStyles.Code;
                default: return "";
            }
        }

        private static string Normalize(string style)
        {
            return string.IsNullOrEmpty(style) ? "" : style;
        }

        /// <summary>
        /// A snapshot of the child nodes, because the caller iterates while the
        /// browser may still be rearranging a live <c>NodeList</c> underneath it.
        /// </summary>
        private static List<Node> Children(HTMLElement host)
        {
            var nodes = new List<Node>();
            NodeList children = host.childNodes;

            for (uint i = 0; i < children.length; i++)
            {
                nodes.Add(children[i]);
            }

            return nodes;
        }
    }
}
