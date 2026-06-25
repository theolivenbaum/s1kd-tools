using System.Xml;

namespace S1kdTools;

/// <summary>
/// Filtering mode for <see cref="Instance.Filter"/>. Mirrors
/// <c>s1kdFilterMode</c> from <c>libs1kd</c>'s <c>instance.h</c>.
/// </summary>
public enum FilterMode
{
    /// <summary>No extra processing beyond removing non-applicable content.</summary>
    Default = 0,
    /// <summary>Remove wholly resolved (unambiguously true/false) annotations.</summary>
    Reduce = 1,
    /// <summary>Remove resolved parts of annotations.</summary>
    Simplify = 2,
    /// <summary>Only remove false parts of annotations.</summary>
    Prune = 3,
}

/// <summary>
/// Programmatic applicability-filtering API for S1000D CSDB objects, mirroring
/// the public <c>libs1kd</c> filter functions (<c>s1kdDocFilter</c>) from
/// <c>s1kd-instance</c>. This is a direct DOM port of the applicability core of
/// <c>reference/tools/s1kd-instance/s1kd-instance.c</c>.
/// </summary>
public static class Instance
{
    /// <summary>
    /// Create a filtered instance of <paramref name="doc"/> based on the
    /// user-defined applicability in <paramref name="app"/>. The input document
    /// is not modified — a clone is filtered and returned. Mirrors
    /// <c>s1kdDocFilter</c>.
    /// </summary>
    /// <param name="doc">The source CSDB object.</param>
    /// <param name="app">The user-supplied applicability definitions.</param>
    /// <param name="mode">The filtering mode.</param>
    /// <returns>A new document containing the filtered instance.</returns>
    public static XmlDocument Filter(XmlDocument doc, Applicability app, FilterMode mode)
    {
        var outDoc = (XmlDocument)doc.CloneNode(true);

        XmlElement defs = app.Definitions;
        if (CountElementChildren(defs) == 0)
        {
            return outDoc;
        }

        XmlElement? root = outDoc.DocumentElement;
        if (root == null)
        {
            return outDoc;
        }

        XmlNode? rag = outDoc.SelectSingleNode("//referencedApplicGroup");
        if (rag == null || CountElementChildren(rag) == 0)
        {
            return outDoc;
        }

        StripApplic(defs, rag, root);

        if (mode >= FilterMode.Reduce)
        {
            CleanApplicStmts(defs, rag, mode < FilterMode.Prune);

            if (CountElementChildren(rag) == 0)
            {
                rag.ParentNode?.RemoveChild(rag);
                rag = null;
            }

            // Unconditional, matching s1kdDocFilter: drops dangling applicRefId
            // references even after the group is removed.
            CleanApplic(rag, root);

            if (mode >= FilterMode.Simplify && rag != null)
            {
                rag = SimplApplicClean(defs, rag, mode == FilterMode.Prune);
            }

            if (mode != FilterMode.Prune && rag != null)
            {
                rag = RemSupersets(defs, rag, root, mode < FilterMode.Simplify);
            }
        }

        return outDoc;
    }

    // ---- applicability filtering core (ported from s1kd-instance.c) ----

    /// <summary>
    /// Remove non-applicable elements from content. Elements carrying
    /// <c>@applicRefId</c> / <c>@refapplic</c> that reference an applic statement
    /// which evaluates false (under <paramref name="defs"/> with assume=true) are
    /// removed. Mirrors <c>strip_applic</c>.
    /// </summary>
    public static void StripApplic(XmlNode defs, XmlNode? referencedApplicGroup, XmlNode node, bool tagNonApplic = false)
    {
        if (node is XmlElement el)
        {
            string? applicRefId = el.GetAttribute("applicRefId");
            if (string.IsNullOrEmpty(applicRefId))
            {
                applicRefId = el.GetAttribute("refapplic");
            }

            if (!string.IsNullOrEmpty(applicRefId) && referencedApplicGroup != null)
            {
                XmlNode? applic = GetElementById(referencedApplicGroup, applicRefId);
                if (applic != null && !EvalApplicStmt(defs, applic, true))
                {
                    if (tagNonApplic)
                    {
                        var pi = node.OwnerDocument!.CreateProcessingInstruction("notApplicable", string.Empty);
                        node.InsertBefore(pi, node.FirstChild);
                    }
                    else
                    {
                        node.ParentNode?.RemoveChild(node);
                    }
                    return;
                }
            }
        }

        XmlNode? cur = node.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            StripApplic(defs, referencedApplicGroup, cur, tagNonApplic);
            cur = next;
        }
    }

    /// <summary>
    /// Remove unambiguously true or false applic statements from the group.
    /// Mirrors <c>clean_applic_stmts</c>.
    /// </summary>
    public static void CleanApplicStmts(XmlNode defs, XmlNode referencedApplicGroup, bool remTrue)
    {
        XmlNode? cur = referencedApplicGroup.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            if (cur.NodeType == XmlNodeType.Element &&
                ((remTrue && EvalApplicStmt(defs, cur, false)) || !EvalApplicStmt(defs, cur, true)))
            {
                cur.ParentNode?.RemoveChild(cur);
            }
            cur = next;
        }
    }

    /// <summary>
    /// Remove <c>@applicRefId</c> references on content where the applic
    /// statement was removed by <see cref="CleanApplicStmts"/>. Mirrors
    /// <c>clean_applic</c>.
    /// </summary>
    public static void CleanApplic(XmlNode? referencedApplicGroup, XmlNode node)
    {
        if (node is XmlElement el && el.HasAttribute("applicRefId"))
        {
            string applicRefId = el.GetAttribute("applicRefId");
            XmlNode? applic = referencedApplicGroup == null ? null : GetElementById(referencedApplicGroup, applicRefId);
            if (applic == null)
            {
                el.RemoveAttribute("applicRefId");
            }
        }

        for (XmlNode? cur = node.FirstChild; cur != null; cur = cur.NextSibling)
        {
            CleanApplic(referencedApplicGroup, cur);
        }
    }

    /// <summary>
    /// Remove applic statements or parts of statements where all assertions are
    /// unambiguously true or false. Returns true if the whole annotation is
    /// removed, false if only parts are removed. Mirrors <c>simpl_applic</c>.
    /// </summary>
    public static bool SimplApplic(XmlNode defs, XmlNode node, bool remTrue, bool cleanDispText = false)
    {
        if (node.LocalName == "applic")
        {
            if ((remTrue && EvalApplicStmt(defs, node, false)) || !EvalApplicStmt(defs, node, true))
            {
                node.ParentNode?.RemoveChild(node);
                return true;
            }
        }
        else if (node.LocalName == "evaluate")
        {
            if ((remTrue && Applicability.EvalApplic(defs, node, false)) || !Applicability.EvalApplic(defs, node, true))
            {
                if (cleanDispText)
                {
                    RemDispText(node);
                }
                node.ParentNode?.RemoveChild(node);
                return false;
            }
        }
        else if (node.LocalName == "assert")
        {
            if ((remTrue && Applicability.EvalAssert(defs, node, false)) || !Applicability.EvalAssert(defs, node, true))
            {
                if (cleanDispText)
                {
                    RemDispText(node);
                }
                node.ParentNode?.RemoveChild(node);
                return false;
            }
        }

        XmlNode? cur = node.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            SimplApplic(defs, cur, remTrue, cleanDispText);
            cur = next;
        }

        return false;
    }

    /// <summary>Remove display text from the containing annotation. Mirrors <c>rem_disp_text</c>.</summary>
    private static void RemDispText(XmlNode node)
    {
        XmlNode? disptext = node.SelectSingleNode("ancestor::applic/*[self::displayText or self::displaytext]");
        disptext?.ParentNode?.RemoveChild(disptext);
    }

    /// <summary>If an &lt;evaluate&gt; contains one or no child elements, remove it. Mirrors <c>simpl_evaluate</c>.</summary>
    private static void SimplEvaluate(XmlNode evaluate)
    {
        int nchild = 0;
        for (XmlNode? cur = evaluate.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType == XmlNodeType.Element)
            {
                nchild++;
            }
        }

        if (nchild < 2)
        {
            XmlNode? child = FindChild(evaluate, "assert") ?? FindChild(evaluate, "evaluate");
            XmlNode? parent = evaluate.ParentNode;
            if (parent != null)
            {
                if (child != null)
                {
                    parent.InsertAfter(child, evaluate);
                }
                parent.RemoveChild(evaluate);
            }
        }
    }

    /// <summary>Simplify &lt;evaluate&gt; elements recursively. Mirrors <c>simpl_applic_evals</c>.</summary>
    public static void SimplApplicEvals(XmlNode? node)
    {
        if (node == null)
        {
            return;
        }

        XmlNode? cur = node.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            if (cur.NodeType == XmlNodeType.Element)
            {
                SimplApplicEvals(cur);
            }
            cur = next;
        }

        if (node.LocalName == "evaluate")
        {
            SimplEvaluate(node);
        }
    }

    /// <summary>
    /// Run <see cref="SimplApplic"/> + <see cref="SimplApplicEvals"/> over a
    /// referencedApplicGroup, removing it if empty. Mirrors <c>simpl_applic_clean</c>.
    /// </summary>
    public static XmlNode? SimplApplicClean(XmlNode defs, XmlNode? referencedApplicGroup, bool remTrue, bool cleanDispText = false)
    {
        if (referencedApplicGroup == null)
        {
            return null;
        }

        SimplApplic(defs, referencedApplicGroup, remTrue, cleanDispText);
        SimplApplicEvals(referencedApplicGroup);

        bool hasApplic = false;
        for (XmlNode? cur = referencedApplicGroup.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.LocalName == "applic")
            {
                hasApplic = true;
            }
        }

        if (!hasApplic)
        {
            referencedApplicGroup.ParentNode?.RemoveChild(referencedApplicGroup);
            return null;
        }

        return referencedApplicGroup;
    }

    /// <summary>
    /// Remove annotations which are supersets of the user-defined applicability.
    /// Mirrors <c>rem_supersets</c>.
    /// </summary>
    public static XmlNode? RemSupersets(XmlNode defs, XmlNode? referencedApplicGroup, XmlNode root, bool simpl)
    {
        if (referencedApplicGroup == null)
        {
            return null;
        }

        var applics = new List<XmlNode>();
        foreach (XmlNode n in referencedApplicGroup.SelectNodes("applic")!)
        {
            applics.Add(n);
        }

        foreach (XmlNode applic in applics)
        {
            if (AnnotationIsSuperset(defs, applic, simpl))
            {
                applic.ParentNode?.RemoveChild(applic);
            }
        }

        CleanApplic(referencedApplicGroup, root);

        if (CountElementChildren(referencedApplicGroup) == 0)
        {
            referencedApplicGroup.ParentNode?.RemoveChild(referencedApplicGroup);
            return null;
        }

        return referencedApplicGroup;
    }

    /// <summary>Find an applicability definition in a set. Mirrors <c>get_applic_def</c>.</summary>
    private static XmlElement? GetApplicDef(XmlNode defs, string? id, string? type)
    {
        for (XmlNode? cur = defs.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur is not XmlElement el)
            {
                continue;
            }
            if (el.GetAttribute("applicPropertyIdent") == id && el.GetAttribute("applicPropertyType") == type)
            {
                return el;
            }
        }
        return null;
    }

    /// <summary>Remove values from a definition. Mirrors <c>undefine_applic</c>.</summary>
    private static void UndefineApplic(XmlElement def, string vals)
    {
        // Single-value definition stored as an attribute.
        if (def.HasAttribute("applicPropertyValues"))
        {
            string cur = def.GetAttribute("applicPropertyValues");
            if (Csdb.IsInSet(vals, cur))
            {
                def.RemoveAttribute("applicPropertyValues");
            }
            if (!def.HasAttribute("applicPropertyValues") && CountElementChildren(def) == 0)
            {
                def.ParentNode?.RemoveChild(def);
            }
            return;
        }

        // Multi-value definition stored as <value> children.
        XmlNode? child = def.FirstChild;
        while (child != null)
        {
            XmlNode? next = child.NextSibling;
            if (child.NodeType == XmlNodeType.Element)
            {
                string v = child.InnerText;
                if (Csdb.IsInSet(vals, v))
                {
                    def.RemoveChild(child);
                }
            }
            child = next;
        }

        if (CountElementChildren(def) == 0 && !def.HasAttribute("applicPropertyValues"))
        {
            def.ParentNode?.RemoveChild(def);
        }
    }

    /// <summary>
    /// Determine whether an annotation is a superset of the user-defined
    /// applicability. Mirrors <c>annotation_is_superset</c>.
    /// </summary>
    private static bool AnnotationIsSuperset(XmlNode defs, XmlNode applic, bool simpl)
    {
        XmlNode defscopy = defs.CloneNode(true);
        XmlNode app = applic.CloneNode(true);

        if (simpl)
        {
            SimplApplic(defscopy, app, true);
            SimplApplicEvals(app);
        }

        var asserts = new List<XmlNode>();
        foreach (XmlNode n in app.SelectNodes(".//assert")!)
        {
            asserts.Add(n);
        }

        foreach (XmlNode assertNode in asserts)
        {
            string? id = FirstValue(assertNode, "@applicPropertyIdent", "@actidref");
            string? type = FirstValue(assertNode, "@applicPropertyType", "@actreftype");

            XmlElement? a = GetApplicDef(defscopy, id, type);
            if (a != null && Applicability.EvalAssert(defscopy, assertNode, true))
            {
                string? vals = FirstValue(assertNode, "@applicPropertyValues", "@actvalues");
                string? op = FirstValue(assertNode, "parent::evaluate/@andOr", "parent::evaluate/@operator");

                if (vals != null && op != null)
                {
                    // Do not remove assertions from AND evaluations unless
                    // they are unambiguously true.
                    if (op != "and" || Applicability.EvalAssert(defscopy, assertNode, false))
                    {
                        assertNode.ParentNode?.RemoveChild(assertNode);
                        UndefineApplic(a, vals);
                    }
                }
            }
        }

        bool result;
        if (app.SelectNodes(".//assert")!.Count == 0)
        {
            result = EvalApplicStmt(defscopy, applic, true);
        }
        else
        {
            result = EvalApplicStmt(defscopy, app, false);
        }

        return result;
    }

    /// <summary>Replace all references from one annotation to another. Mirrors <c>replace_annotation</c>.</summary>
    private static void ReplaceAnnotation(XmlDocument doc, XmlNode app1, XmlNode app2)
    {
        if (app1.ParentNode is not XmlElement p1 || app2.ParentNode is not XmlElement p2)
        {
            return;
        }
        string app1Id = p1.GetAttribute("id");
        string app2Id = p2.GetAttribute("id");
        if (string.IsNullOrEmpty(app1Id))
        {
            return;
        }

        foreach (XmlNode n in doc.SelectNodes($"//*[@applicRefId='{app1Id}']")!)
        {
            ((XmlElement)n).SetAttribute("applicRefId", app2Id);
        }
    }

    /// <summary>Remove duplicate annotations. Mirrors <c>rem_dupl_annotations</c>.</summary>
    public static XmlNode? RemDuplAnnotations(XmlDocument doc, XmlNode? referencedApplicGroup)
    {
        if (referencedApplicGroup == null)
        {
            return null;
        }

        var stmts = new List<XmlNode>();
        foreach (XmlNode n in doc.SelectNodes(
            "//referencedApplicGroup/applic/assert|//referencedApplicGroup/applic/evaluate")!)
        {
            stmts.Add(n);
        }

        for (int i = 0; i < stmts.Count; i++)
        {
            for (int j = i + 1; j < stmts.Count; j++)
            {
                if (Applicability.SameAnnotation(stmts[i], stmts[j]))
                {
                    ReplaceAnnotation(doc, stmts[j], stmts[i]);
                }
            }
        }

        if (CountElementChildren(referencedApplicGroup) == 0)
        {
            referencedApplicGroup.ParentNode?.RemoveChild(referencedApplicGroup);
            return null;
        }

        // Remove redundant uses of applicability annotations.
        foreach (XmlNode n in doc.SelectNodes("//*[@applicRefId = ancestor::*/@applicRefId]")!)
        {
            ((XmlElement)n).RemoveAttribute("applicRefId");
        }

        return referencedApplicGroup;
    }

    /// <summary>Remove unused applicability annotations. Mirrors <c>rem_unused_annotations</c>.</summary>
    public static XmlNode? RemUnusedAnnotations(XmlDocument doc, XmlNode? referencedApplicGroup)
    {
        if (referencedApplicGroup == null)
        {
            return null;
        }

        string xpath = referencedApplicGroup.LocalName == "referencedApplicGroup"
            ? "applic[not(@id=//@applicRefId)]"
            : "applic[not(@id=//@refapplic)]";

        var unused = new List<XmlNode>();
        foreach (XmlNode n in referencedApplicGroup.SelectNodes(xpath)!)
        {
            unused.Add(n);
        }
        foreach (XmlNode n in unused)
        {
            n.ParentNode?.RemoveChild(n);
        }

        if (CountElementChildren(referencedApplicGroup) == 0)
        {
            referencedApplicGroup.ParentNode?.RemoveChild(referencedApplicGroup);
            return null;
        }

        return referencedApplicGroup;
    }

    // ---- helpers ----

    /// <summary>Tests whether an &lt;applic&gt; element is true. Mirrors <c>eval_applic_stmt</c>.</summary>
    internal static bool EvalApplicStmt(XmlNode defs, XmlNode applic, bool assume)
    {
        XmlNode? stmt = FindChild(applic, "assert") ?? FindChild(applic, "evaluate");
        if (stmt == null)
        {
            return assume;
        }
        return Applicability.EvalApplic(defs, stmt, assume);
    }

    /// <summary>Find the first child element with a given name. Mirrors <c>find_child</c>.</summary>
    private static XmlNode? FindChild(XmlNode parent, string name)
    {
        for (XmlNode? cur = parent.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.LocalName == name)
            {
                return cur;
            }
        }
        return null;
    }

    /// <summary>Search recursively for a descendant element with the given id. Mirrors <c>get_element_by_id</c>.</summary>
    private static XmlNode? GetElementById(XmlNode root, string id)
    {
        for (XmlNode? cur = root.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur is XmlElement el && el.GetAttribute("id") == id)
            {
                return cur;
            }
            XmlNode? ch = GetElementById(cur, id);
            if (ch != null)
            {
                return ch;
            }
        }
        return null;
    }

    private static string? FirstValue(XmlNode node, string a, string b)
    {
        XmlNode? n = node.SelectSingleNode(a) ?? node.SelectSingleNode(b);
        return n?.Value ?? n?.InnerText;
    }

    private static int CountElementChildren(XmlNode node)
    {
        int n = 0;
        for (XmlNode? cur = node.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType == XmlNodeType.Element)
            {
                n++;
            }
        }
        return n;
    }
}
