using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Xsl;

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

    // ---- CIR resolution (ported from undepend_cir / undepend_cir_xsl) ----

    /// <summary>
    /// The set of CIR (Common Information Repository) types for which a built-in
    /// resolution stylesheet exists, mapped to the embedded resource that
    /// resolves references of that type. Mirrors <c>get_cir_xsl</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CirXslByType =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accessPointRepository"] = "instance/accessPointRepository.xsl",
            ["applicRepository"] = "instance/applicRepository.xsl",
            ["cautionRepository"] = "instance/cautionRepository.xsl",
            ["circuitBreakerRepository"] = "instance/circuitBreakerRepository.xsl",
            ["controlIndicatorRepository"] = "instance/controlIndicatorRepository.xsl",
            ["enterpriseRepository"] = "instance/enterpriseRepository.xsl",
            ["functionalItemRepository"] = "instance/functionalItemRepository.xsl",
            ["einlist"] = "instance/einlist.xsl",
            ["partRepository"] = "instance/partRepository.xsl",
            ["illustratedPartsCatalog"] = "instance/illustratedPartsCatalog.xsl",
            ["supplyRepository"] = "instance/supplyRepository.xsl",
            ["toolRepository"] = "instance/toolRepository.xsl",
            ["warningRepository"] = "instance/warningRepository.xsl",
            ["zoneRepository"] = "instance/zoneRepository.xsl",
            ["hazardRepository"] = "instance/hazardRepository.xsl",
            ["terminologyRepository"] = "instance/terminologyRepository.xsl",
        };

    /// <summary>
    /// Returns the embedded resource path for the built-in CIR resolution
    /// stylesheet handling <paramref name="cirType"/>, or null if there is no
    /// built-in XSLT for that type. Mirrors <c>get_cir_xsl</c>.
    /// </summary>
    public static string? GetCirXslResource(string cirType) =>
        CirXslByType.TryGetValue(cirType, out string? res) ? res : null;

    /// <summary>The CIR types that have built-in resolution support.</summary>
    public static IReadOnlyCollection<string> SupportedCirTypes => (IReadOnlyCollection<string>)CirXslByType.Keys;

    /// <summary>
    /// Return the built-in CIR resolution stylesheet for <paramref name="cirType"/>
    /// as XML text, or null if none exists. Mirrors <c>dump_cir_xsl</c>.
    /// </summary>
    public static string? DumpCirXsl(string cirType)
    {
        string? res = GetCirXslResource(cirType);
        return res == null ? null : EmbeddedResources.ReadText(res);
    }

    /// <summary>
    /// Resolve externalized items in <paramref name="dm"/> against the CIR data
    /// module <paramref name="cir"/>, in place. The user-defined applicability in
    /// <paramref name="defs"/> is first applied to the CIR's own content, then the
    /// appropriate resolution stylesheet inlines the referenced entries.
    ///
    /// Mirrors <c>undepend_cir</c>: returns true if a resolution stylesheet was
    /// applied (or a custom one supplied), false if the CIR type was unsupported
    /// (in which case <paramref name="dm"/> is left unchanged). When
    /// <paramref name="addSource"/> is requested and supported, a
    /// <c>repositorySourceDmIdent</c> is inserted before the instance's security
    /// element (mirroring the <c>add_src</c> behaviour).
    /// </summary>
    /// <param name="dm">The instance document; modified in place on success.</param>
    /// <param name="defs">User-defined applicability definitions.</param>
    /// <param name="cir">The CIR data module.</param>
    /// <param name="addSource">Whether to add a repositorySourceDmIdent.</param>
    /// <param name="customXslText">Optional custom XSLT (overrides the built-in).</param>
    /// <returns>True if resolution was attempted, false for an unsupported type.</returns>
    public static bool ResolveCir(XmlDocument dm, XmlNode defs, XmlDocument cir, bool addSource, string? customXslText = null)
    {
        // Apply the user-defined applicability to the CIR content first, and
        // determine the CIR type node. Mirrors the //content handling.
        XmlNode? content = cir.SelectSingleNode("//content");
        XmlNode? cirNode;
        if (content == null)
        {
            cirNode = cir.DocumentElement;
        }
        else
        {
            XmlNode? rag = cir.SelectSingleNode("//referencedApplicGroup");
            if (rag != null)
            {
                StripApplic(defs, rag, content);
            }

            cirNode = cir.SelectSingleNode(
                "//content/commonRepository/*[position()=last()]|" +
                "//content/techRepository/*[position()=last()]|" +
                "//content/techrep/*[position()=last()]|" +
                "//content/illustratedPartsCatalog") ?? cir.DocumentElement;
        }

        if (cirNode == null)
        {
            return false;
        }

        string cirType = cirNode.LocalName;

        bool supported;
        string? xslText;
        if (customXslText != null)
        {
            xslText = customXslText;
            supported = true;
        }
        else
        {
            string? res = GetCirXslResource(cirType);
            if (res == null)
            {
                // No built-in XSLT: nothing to resolve, and add_src is disabled.
                return false;
            }
            xslText = EmbeddedResources.ReadText(res);
            supported = true;
        }

        ApplyCirXsl(dm, cir, xslText);

        // Issue 3.0 objects (idstatus) never get a repository source ident.
        if (addSource && supported && dm.SelectSingleNode("//idstatus") == null)
        {
            AddRepositorySource(dm, cir);
        }

        return supported;
    }

    /// <summary>
    /// Apply a CIR resolution stylesheet to a "mux" document containing the
    /// instance (child 1) and the CIR (child 2), then replace the instance's
    /// root element with the first child of the result. Mirrors
    /// <c>undepend_cir_xsl</c>.
    /// </summary>
    private static void ApplyCirXsl(XmlDocument dm, XmlDocument cir, string xslText)
    {
        // Build <mux><dm/><cir/></mux>.
        var mux = new XmlDocument { PreserveWhitespace = true };
        XmlElement muxRoot = mux.CreateElement("mux");
        mux.AppendChild(muxRoot);
        if (dm.DocumentElement != null)
        {
            muxRoot.AppendChild(mux.ImportNode(dm.DocumentElement, true));
        }
        if (cir.DocumentElement != null)
        {
            muxRoot.AppendChild(mux.ImportNode(cir.DocumentElement, true));
        }

        var xslt = new XslCompiledTransform();
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };

        using (var sr = new StringReader(xslText))
        using (XmlReader styleReader = XmlReader.Create(sr, readerSettings))
        {
            xslt.Load(styleReader);
        }

        var resultDoc = new XmlDocument { PreserveWhitespace = true };
        using (var ms = new MemoryStream())
        {
            var writerSettings = new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Auto,
                OmitXmlDeclaration = true,
            };
            using (XmlWriter writer = XmlWriter.Create(ms, writerSettings))
            {
                xslt.Transform(mux, writer);
            }
            ms.Position = 0;
            using XmlReader resultReader = XmlReader.Create(ms, readerSettings);
            resultDoc.Load(resultReader);
        }

        // The transformed instance is /mux/*[1].
        XmlNode? newRoot = resultDoc.SelectSingleNode("/mux/*[1]");
        if (newRoot == null || dm.DocumentElement == null)
        {
            return;
        }

        XmlNode imported = dm.ImportNode(newRoot, true);
        dm.ReplaceChild(imported, dm.DocumentElement);
    }

    /// <summary>
    /// Insert a <c>repositorySourceDmIdent</c> (copied from the CIR's dmIdent)
    /// before the instance's <c>security</c> element. Mirrors the <c>add_src</c>
    /// block of <c>undepend_cir</c>.
    /// </summary>
    private static void AddRepositorySource(XmlDocument dm, XmlDocument cir)
    {
        XmlNode? dmIdent = cir.SelectSingleNode("//dmIdent");
        if (dmIdent == null)
        {
            return;
        }
        XmlNode? security = dm.SelectSingleNode("//security");
        if (security?.ParentNode == null)
        {
            return;
        }

        XmlElement repo = dm.CreateElement("repositorySourceDmIdent");
        security.ParentNode.InsertBefore(repo, security);
        foreach (XmlNode child in dmIdent.ChildNodes)
        {
            repo.AppendChild(dm.ImportNode(child, true));
        }
    }

    /// <summary>
    /// Whether a file is a CIR/TIR data module (its content contains a
    /// commonRepository, techRepository or techrep). Mirrors <c>is_cir</c>.
    /// </summary>
    public static bool IsCir(string path)
    {
        try
        {
            XmlDocument doc = XmlUtils.ReadDoc(path);
            return doc.SelectSingleNode("//commonRepository|//techRepository|//techrep") != null;
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return false;
        }
    }

    // ---- product (PCT/ACT/CCT) applicability loading ----

    /// <summary>Pattern recognising an <c>ident:type=value</c> primary key.</summary>
    private static readonly Regex ProductKeyPattern =
        new(@"^[^:]+:(prodattr|condition)=[^|~]+", RegexOptions.Compiled);

    /// <summary>
    /// Assign all applicability definitions of the named product from a PCT
    /// (Product Cross-reference Table) into <paramref name="defs"/>. The
    /// <paramref name="product"/> may be the XML id of a <c>product</c> element,
    /// or an <c>ident:type=value</c> primary key matching a <c>product</c> via
    /// its <c>assign</c> children. Mirrors <c>load_applic_from_pct</c>.
    /// </summary>
    /// <returns>The number of values assigned (0 if the product was not found).</returns>
    public static int LoadApplicFromPct(XmlElement defs, XmlDocument pct, string product, bool perDm = false)
    {
        XmlNodeList? assigns;
        if (ProductKeyPattern.IsMatch(product))
        {
            int colon = product.IndexOf(':');
            int eq = product.IndexOf('=', colon + 1);
            string ident = product[..colon];
            string type = product[(colon + 1)..eq];
            string value = product[(eq + 1)..];

            // XPath 1.0 has no variables here; build a literal-safe predicate.
            string xpath =
                $"//product[assign[@applicPropertyIdent={XPathLiteral(ident)} and " +
                $"@applicPropertyType={XPathLiteral(type)} and " +
                $"@applicPropertyValue={XPathLiteral(value)}]]/assign";
            assigns = pct.SelectNodes(xpath);
        }
        else
        {
            assigns = pct.SelectNodes($"//product[@id={XPathLiteral(product)}]/assign");
        }

        if (assigns == null || assigns.Count == 0)
        {
            return 0;
        }

        int count = 0;
        foreach (XmlNode n in assigns)
        {
            if (n is not XmlElement a)
            {
                continue;
            }
            string ident = a.GetAttribute("applicPropertyIdent");
            string type = a.GetAttribute("applicPropertyType");
            string value = a.GetAttribute("applicPropertyValue");
            if (ident.Length == 0 || type.Length == 0 || value.Length == 0)
            {
                continue;
            }
            DefineApplicValue(defs, ident, type, value, perDm, userDefined: true);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Define a value for a product attribute or condition in
    /// <paramref name="defs"/>, merging with any existing definition for the same
    /// ident/type. Mirrors the relevant path of <c>define_applic</c>.
    /// </summary>
    public static void DefineApplicValue(XmlElement defs, string ident, string type, string value, bool perDm, bool userDefined)
    {
        XmlElement? assert = null;
        for (XmlNode? cur = defs.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur is XmlElement el &&
                el.GetAttribute("applicPropertyIdent") == ident &&
                el.GetAttribute("applicPropertyType") == type)
            {
                assert = el;
            }
        }

        if (assert == null)
        {
            assert = defs.OwnerDocument!.CreateElement("assert");
            assert.SetAttribute("applicPropertyIdent", ident);
            assert.SetAttribute("applicPropertyType", type);
            assert.SetAttribute("applicPropertyValues", value);
            assert.SetAttribute("userDefined", userDefined ? "true" : "false");
            if (perDm)
            {
                assert.SetAttribute("perDm", "true");
            }
            defs.AppendChild(assert);
            return;
        }

        // An existing definition may only be modified if the modification is at
        // least as authoritative as the existing one (mirrors allow_def_modify:
        // a user-defined value may not be overwritten by a non-user-defined one).
        bool existingUserDefined = assert.GetAttribute("userDefined") == "true";
        bool allowModify = userDefined || !existingUserDefined;

        if (assert.HasAttribute("applicPropertyValues"))
        {
            string first = assert.GetAttribute("applicPropertyValues");
            if (first != value && allowModify)
            {
                AddValue(assert, first);
                AddValue(assert, value);
                assert.RemoveAttribute("applicPropertyValues");
            }
        }
        else
        {
            bool dup = false;
            for (XmlNode? cur = assert.FirstChild; cur != null && !dup; cur = cur.NextSibling)
            {
                if (cur.InnerText == value)
                {
                    dup = true;
                }
            }
            if (!dup && allowModify)
            {
                AddValue(assert, value);
            }
        }
    }

    private static void AddValue(XmlElement assert, string value)
    {
        XmlElement v = assert.OwnerDocument!.CreateElement("value");
        v.InnerText = value;
        assert.AppendChild(v);
    }

    /// <summary>Remove per-DM applicability assignments. Mirrors <c>clear_perdm_applic</c>.</summary>
    public static void ClearPerDmApplic(XmlElement defs)
    {
        XmlNode? cur = defs.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            if (cur is XmlElement el && el.HasAttribute("perDm"))
            {
                defs.RemoveChild(cur);
            }
            cur = next;
        }
    }

    /// <summary>Quote a string as an XPath 1.0 string literal (handles embedded quotes).</summary>
    internal static string XPathLiteral(string value)
    {
        if (!value.Contains('\''))
        {
            return $"'{value}'";
        }
        if (!value.Contains('"'))
        {
            return $"\"{value}\"";
        }
        // Both quote kinds present: build concat('a',"'",'b',...).
        var parts = value.Split('\'');
        var sb = new System.Text.StringBuilder("concat(");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", \"'\", ");
            }
            sb.Append('\'').Append(parts[i]).Append('\'');
        }
        sb.Append(')');
        return sb.ToString();
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

    // ---- alts flattening (ported from flatten_alts / flatten-alts.xsl) ----

    /// <summary>
    /// The built-in stylesheet that flattens <c>*Alts</c> elements with a single
    /// child by replacing them with that child, and (when
    /// <c>fix-alts-refs</c> is true) rewrites the <c>internalRefTargetType</c> of
    /// cross-references to alts. Ported verbatim from
    /// <c>reference/tools/s1kd-instance/xsl/flatten-alts.xsl</c> so the tool stays
    /// self-contained.
    /// </summary>
    private const string FlattenAltsXsl =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
          <xsl:param name="fix-alts-refs"/>
          <xsl:template match="@*|node()">
            <xsl:copy>
              <xsl:apply-templates select="@*|node()"/>
            </xsl:copy>
          </xsl:template>
          <xsl:template match="assocWarningMalfunctionAlts|
                               bitMessageAlts|
                               commonInfoDescrParaAlts|
                               correlatedFaultAlts|
                               detectedFaultAlts|
                               dialogAlts|
                               dmNodeAlts|
                               dmSeqAlts|
                               electricalEquipAlts|
                               figureAlts|
                               harnessAlts|
                               isolatedFaultAlts|
                               isolationProcedureEndAlts|
                               isolationStepAlts|
                               levelledParaAlts|
                               messageAlts|
                               multimediaAlts|
                               observedFaultAlts|
                               proceduralStepAlts|
                               taskDefinitionAlts|
                               warningMalfunctionAlts|
                               wireAlts">
            <xsl:choose>
              <xsl:when test="count(*) = 1">
                <xsl:for-each select="*">
                  <xsl:copy>
                    <xsl:apply-templates select="parent::*/@id|@*[name() != 'id']|node()"/>
                  </xsl:copy>
                </xsl:for-each>
              </xsl:when>
              <xsl:otherwise>
                <xsl:copy>
                  <xsl:apply-templates select="@*|node()"/>
                </xsl:copy>
              </xsl:otherwise>
            </xsl:choose>
          </xsl:template>
          <xsl:template match="internalRef/@internalRefTargetType">
            <xsl:choose>
              <xsl:when test="$fix-alts-refs">
                <xsl:variable name="id" select="parent::internalRef/@internalRefId"/>
                <xsl:variable name="target" select="//*[@id=$id]"/>
                <xsl:attribute name="internalRefTargetType">
                  <xsl:choose>
                    <xsl:when test="$target/self::figureAlts">irtt01</xsl:when>
                    <xsl:when test="$target/self::multimediaAlts">irtt03</xsl:when>
                    <xsl:when test="$target/self::levelledParaAlts">irtt07</xsl:when>
                    <xsl:when test="$target/self::proceduralStepAlts|$target/self::isolationStepAlts|$target/self::isolationProcedureEndAlts">irtt08</xsl:when>
                  </xsl:choose>
                </xsl:attribute>
              </xsl:when>
              <xsl:otherwise>
                <xsl:copy>
                  <xsl:apply-templates/>
                </xsl:copy>
              </xsl:otherwise>
            </xsl:choose>
          </xsl:template>
        </xsl:stylesheet>
        """;

    /// <summary>
    /// Flatten <c>*Alts</c> elements in place. <c>*Alts</c> elements with exactly
    /// one child element are replaced by that child; when
    /// <paramref name="fixAltsRefs"/> is set, the <c>internalRefTargetType</c> of
    /// cross-references is corrected. Mirrors <c>flatten_alts</c>.
    /// </summary>
    public static void FlattenAlts(XmlDocument doc, bool fixAltsRefs)
    {
        var args = new XsltArgumentList();
        // libxslt passes "true()"/"false()" as XPath boolean params; in
        // XslCompiledTransform we supply a real boolean to the same effect.
        args.AddParam("fix-alts-refs", string.Empty, fixAltsRefs);
        TransformInPlace(doc, FlattenAltsXsl, args);
    }

    /// <summary>
    /// Transform <paramref name="doc"/> with <paramref name="xslText"/> and replace
    /// the document's root element with the transformed result, in place. Mirrors
    /// the C <c>transform_doc</c> helper.
    /// </summary>
    private static void TransformInPlace(XmlDocument doc, string xslText, XsltArgumentList? args)
    {
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };

        var xslt = new XslCompiledTransform();
        using (var sr = new StringReader(xslText))
        using (XmlReader styleReader = XmlReader.Create(sr, readerSettings))
        {
            xslt.Load(styleReader);
        }

        var resultDoc = new XmlDocument { PreserveWhitespace = true };
        using (var ms = new MemoryStream())
        {
            var writerSettings = new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Auto,
                OmitXmlDeclaration = true,
            };
            using (XmlWriter writer = XmlWriter.Create(ms, writerSettings))
            {
                xslt.Transform(doc, args, writer);
            }
            ms.Position = 0;
            using XmlReader resultReader = XmlReader.Create(ms, readerSettings);
            resultDoc.Load(resultReader);
        }

        if (resultDoc.DocumentElement == null || doc.DocumentElement == null)
        {
            return;
        }

        XmlNode imported = doc.ImportNode(resultDoc.DocumentElement, true);
        doc.ReplaceChild(imported, doc.DocumentElement);
    }

    // ---- container resolution (ported from resolve_containers) ----

    /// <summary>
    /// Resolve references to container data modules, replacing each
    /// <c>dmRef</c> to a container with the single applicable <c>dmRef</c> from
    /// within that container for the user-defined applicability. Additionally, if
    /// the object being filtered is itself a container, the applicability of its
    /// referenced DMs is copied in as inline annotations prior to filtering (this
    /// happens inside <see cref="ResolveContainerRef"/>). Mirrors
    /// <c>resolve_containers</c>.
    /// </summary>
    /// <param name="doc">The object whose container references are resolved.</param>
    /// <param name="defs">User-defined applicability definitions.</param>
    /// <param name="resolveRef">
    /// Resolver mapping a <c>dmRefIdent</c> node to a (document, path) pair for the
    /// referenced data module, or null when it cannot be located.
    /// </param>
    /// <param name="warn">Callback for the "could not resolve container" warning.</param>
    public static void ResolveContainers(XmlDocument doc, XmlNode defs,
        Func<XmlNode, (XmlDocument doc, string path)?> resolveRef, Action<string>? warn = null)
    {
        var refIdents = new List<XmlNode>();
        foreach (XmlNode n in doc.SelectNodes("//dmRef/dmRefIdent")!)
        {
            refIdents.Add(n);
        }

        foreach (XmlNode refIdent in refIdents)
        {
            (XmlDocument doc, string path)? resolved = resolveRef(refIdent);
            if (resolved == null)
            {
                continue;
            }
            ResolveContainerRef(refIdent, resolved.Value.doc, resolved.Value.path, defs,
                resolveRef, warn);
        }
    }

    /// <summary>
    /// Replace a reference to a container with the appropriate reference within
    /// the container for the given applicability. Mirrors
    /// <c>resolve_container_ref</c> (with <see cref="AddContainerApplics"/>).
    /// </summary>
    private static XmlNode? ResolveContainerRef(XmlNode refIdent, XmlDocument cdoc, string path, XmlNode defs,
        Func<XmlNode, (XmlDocument doc, string path)?> resolveRef, Action<string>? warn)
    {
        XmlElement? root = cdoc.DocumentElement;
        if (root == null)
        {
            return null;
        }

        XmlNode? content = cdoc.SelectSingleNode("//content");
        if (content == null)
        {
            return null;
        }

        // Referenced DM is not a container.
        XmlNode? container = content.SelectSingleNode("container");
        if (container == null)
        {
            return null;
        }

        // If the container does not contain inline annotations, copy them from the
        // referenced DMs.
        XmlNode? rag = cdoc.SelectSingleNode("//referencedApplicGroup");
        if (rag == null)
        {
            rag = AddContainerApplics(cdoc, content, container, resolveRef);
            if (rag == null)
            {
                return null;
            }
        }

        // Filter the container.
        StripApplic(defs, rag, root);

        // If the container does not have exactly one ref after filtering, it should
        // not be resolved.
        var refs = new List<XmlNode>();
        foreach (XmlNode n in container.SelectNodes("refs/dmRef")!)
        {
            refs.Add(n);
        }
        XmlNode? reference = refs.Count == 1 ? refs[0] : null;

        if (reference != null)
        {
            XmlNode? old = refIdent.ParentNode;
            if (old?.ParentNode != null)
            {
                XmlNode newNode = refIdent.OwnerDocument!.ImportNode(reference, true);
                if (newNode is XmlElement ne)
                {
                    ne.RemoveAttribute("applicRefId");
                }
                old.ParentNode.InsertAfter(newNode, old);
                old.ParentNode.RemoveChild(old);
            }
        }
        else
        {
            warn?.Invoke(path);
        }

        return reference;
    }

    /// <summary>
    /// Copy the applicability of the referenced DMs of a container into the
    /// container itself as inline annotations. Mirrors <c>add_container_applics</c>.
    /// </summary>
    private static XmlNode? AddContainerApplics(XmlDocument doc, XmlNode content, XmlNode container,
        Func<XmlNode, (XmlDocument doc, string path)?> resolveRef)
    {
        XmlElement rag = doc.CreateElement("referencedApplicGroup");

        // Insert the referencedApplicGroup element appropriately.
        XmlNode? refs = content.SelectSingleNode("refs");
        if (refs?.ParentNode != null)
        {
            refs.ParentNode.InsertAfter(rag, refs);
        }
        else
        {
            content.InsertBefore(rag, content.FirstChild);
        }

        var refIdents = new List<XmlNode>();
        foreach (XmlNode n in container.SelectNodes("refs/dmRef/dmRefIdent")!)
        {
            refIdents.Add(n);
        }

        int seq = 1;
        foreach (XmlNode refIdent in refIdents)
        {
            (XmlDocument doc, string path)? resolved = resolveRef(refIdent);
            if (resolved == null)
            {
                continue;
            }

            string id = $"app-{seq:D4}";
            if (AddContainerApplic(rag, resolved.Value.doc, id) != null && refIdent.ParentNode is XmlElement dmRef)
            {
                dmRef.SetAttribute("applicRefId", id);
                seq++;
            }
        }

        return rag;
    }

    /// <summary>Create an applicability annotation in a container. Mirrors <c>add_container_applic</c>.</summary>
    private static XmlNode? AddContainerApplic(XmlNode rag, XmlDocument refDoc, string id)
    {
        XmlNode? app = refDoc.SelectSingleNode("//applic");
        if (app == null)
        {
            return null;
        }
        if (app is XmlElement ae)
        {
            ae.SetAttribute("id", id);
        }
        XmlNode imported = rag.OwnerDocument!.ImportNode(app, true);
        return rag.AppendChild(imported);
    }

    // ---- automatic naming (ported from init_ident / auto_name) ----

    /// <summary>
    /// The minimal set of identity fields needed to construct an automatic
    /// filename for a CSDB object. Mirrors the subset of the C <c>struct ident</c>
    /// used by <c>auto_name</c>.
    /// </summary>
    private sealed class ObjectIdent
    {
        public string Type = string.Empty; // DM, PM, DML, COM, DDN, IMF, UPF
        public bool Extended;
        public string? ExtensionProducer;
        public string? ExtensionCode;
        public string? ModelIdentCode;
        public string? SystemDiffCode;
        public string? SystemCode;
        public string? SubSystemCode;
        public string? SubSubSystemCode;
        public string? AssyCode;
        public string? DisassyCode;
        public string? DisassyCodeVariant;
        public string? InfoCode;
        public string? InfoCodeVariant;
        public string? ItemLocationCode;
        public string? LearnCode;
        public string? LearnEventCode;
        public string? SenderIdent;
        public string? ReceiverIdent;
        public string? PmNumber;
        public string? PmVolume;
        public string? IssueNumber;
        public string? InWork;
        public string? LanguageIsoCode;
        public string? CountryIsoCode;
        public string? DmlCommentType;
        public string? SeqNumber;
        public string? YearOfDataIssue;
        public string? ImfIdentIcn;
    }

    /// <summary>
    /// Compute the automatic filename for a filtered instance, mirroring
    /// <c>auto_name</c>. <paramref name="dir"/> is the output directory ("." means
    /// no directory prefix). When <paramref name="noIss"/> is set the issue/inwork
    /// suffix is omitted. Returns null for unsupported object types (matching the C
    /// tool's S_BAD_TYPE / EXIT_BAD_XML path); when the object has no recognisable
    /// ident the source basename is used (also matching the C tool).
    /// </summary>
    public static string? AutoName(string src, XmlDocument dm, string dir, bool noIss)
    {
        string dname, sep;
        if (dir == ".")
        {
            dname = string.Empty;
            sep = string.Empty;
        }
        else
        {
            dname = dir;
            sep = "/";
        }

        ObjectIdent? ident = InitIdent(dm);
        if (ident == null)
        {
            return dname + sep + Path.GetFileName(src);
        }

        if (ident.Type is "DM" or "PM" or "COM" or "UPF")
        {
            ident.LanguageIsoCode = ident.LanguageIsoCode?.ToUpperInvariant();
        }

        if (ident.Type is "DML" or "COM" && !string.IsNullOrEmpty(ident.DmlCommentType))
        {
            ident.DmlCommentType = char.ToUpperInvariant(ident.DmlCommentType![0]) + ident.DmlCommentType[1..];
        }

        string iss = string.Empty;
        if (!noIss && ident.Type is "DM" or "PM" or "DML" or "IMF" or "UPF")
        {
            iss = $"_{ident.IssueNumber}-{ident.InWork}";
        }

        switch (ident.Type)
        {
            case "PM":
                return ident.Extended
                    ? $"{dname}{sep}PME-{ident.ExtensionProducer}-{ident.ExtensionCode}-{ident.ModelIdentCode}-{ident.SenderIdent}-{ident.PmNumber}-{ident.PmVolume}{iss}_{ident.LanguageIsoCode}-{ident.CountryIsoCode}.XML"
                    : $"{dname}{sep}PMC-{ident.ModelIdentCode}-{ident.SenderIdent}-{ident.PmNumber}-{ident.PmVolume}{iss}_{ident.LanguageIsoCode}-{ident.CountryIsoCode}.XML";
            case "DML":
                return $"{dname}{sep}DML-{ident.ModelIdentCode}-{ident.SenderIdent}-{ident.DmlCommentType}-{ident.YearOfDataIssue}-{ident.SeqNumber}{iss}.XML";
            case "DM":
            case "UPF":
            {
                string learn = string.Empty;
                if (!string.IsNullOrEmpty(ident.LearnCode) && !string.IsNullOrEmpty(ident.LearnEventCode))
                {
                    learn = $"-{ident.LearnCode}{ident.LearnEventCode}";
                }
                string prefixExt = ident.Type == "DM" ? "DME" : "UPE";
                string prefix = ident.Type == "DM" ? "DMC" : "UPF";
                return ident.Extended
                    ? $"{dname}{sep}{prefixExt}-{ident.ExtensionProducer}-{ident.ExtensionCode}-{ident.ModelIdentCode}-{ident.SystemDiffCode}-{ident.SystemCode}-{ident.SubSystemCode}{ident.SubSubSystemCode}-{ident.AssyCode}-{ident.DisassyCode}{ident.DisassyCodeVariant}-{ident.InfoCode}{ident.InfoCodeVariant}-{ident.ItemLocationCode}{learn}{iss}_{ident.LanguageIsoCode}-{ident.CountryIsoCode}.XML"
                    : $"{dname}{sep}{prefix}-{ident.ModelIdentCode}-{ident.SystemDiffCode}-{ident.SystemCode}-{ident.SubSystemCode}{ident.SubSubSystemCode}-{ident.AssyCode}-{ident.DisassyCode}{ident.DisassyCodeVariant}-{ident.InfoCode}{ident.InfoCodeVariant}-{ident.ItemLocationCode}{learn}{iss}_{ident.LanguageIsoCode}-{ident.CountryIsoCode}.XML";
            }
            case "COM":
                return $"{dname}{sep}COM-{ident.ModelIdentCode}-{ident.SenderIdent}-{ident.YearOfDataIssue}-{ident.SeqNumber}-{ident.DmlCommentType}_{ident.LanguageIsoCode}-{ident.CountryIsoCode}.XML";
            case "DDN":
                return $"{dname}{sep}DDN-{ident.ModelIdentCode}-{ident.SenderIdent}-{ident.ReceiverIdent}-{ident.YearOfDataIssue}-{ident.SeqNumber}.XML";
            case "IMF":
                return $"{dname}{sep}IMF-{ident.ImfIdentIcn}{iss}.XML";
            default:
                return null;
        }
    }

    /// <summary>
    /// Extract a CSDB object's identity from its address/status. A focused port of
    /// <c>init_ident</c> covering the fields used for automatic naming. Returns null
    /// if no recognisable ident/code is present.
    /// </summary>
    private static ObjectIdent? InitIdent(XmlDocument doc)
    {
        XmlNode? moduleIdent = doc.SelectSingleNode(
            "//dmIdent|//dmaddres|//pmIdent|//pmaddres|//dmlIdent|//dml[dmlc]|" +
            "//commentIdent|//cstatus|//ddnIdent|//ddn|//imfIdent|//updateIdent");
        if (moduleIdent == null)
        {
            return null;
        }

        var ident = new ObjectIdent();
        bool iss30;
        switch (moduleIdent.LocalName)
        {
            case "pmIdent": ident.Type = "PM"; iss30 = false; break;
            case "pmaddres": ident.Type = "PM"; iss30 = true; break;
            case "dmlIdent": ident.Type = "DML"; iss30 = false; break;
            case "dml": ident.Type = "DML"; iss30 = true; break;
            case "commentIdent": ident.Type = "COM"; iss30 = false; break;
            case "cstatus": ident.Type = "COM"; iss30 = true; break;
            case "dmIdent": ident.Type = "DM"; iss30 = false; break;
            case "dmaddres": ident.Type = "DM"; iss30 = true; break;
            case "ddnIdent": ident.Type = "DDN"; iss30 = false; break;
            case "ddn": ident.Type = "DDN"; iss30 = true; break;
            case "imfIdent": ident.Type = "IMF"; iss30 = false; break;
            case "updateIdent": ident.Type = "UPF"; iss30 = false; break;
            default: return null;
        }

        XmlNode? identExtension = doc.SelectSingleNode(
            "//dmIdent/identExtension|//dmaddres/dmcextension|//pmIdent/identExtension|//updateIdent/identExtension");
        XmlNode? code = doc.SelectSingleNode(
            "//dmIdent/dmCode|//dmaddres/dmc/avee|//pmIdent/pmCode|//pmaddres/pmc|//dmlIdent/dmlCode|//dml/dmlc|" +
            "//commentIdent/commentCode|//cstatus/ccode|//ddnIdent/ddnCode|//ddn/ddnc|//imfIdent/imfCode|//updateIdent/updateCode");
        XmlNode? language = doc.SelectSingleNode(
            "//dmIdent/language|//dmaddres/language|//pmIdent/language|//pmaddres/language|" +
            "//commentIdent/language|//cstatus/language|//updateIdent/language");
        XmlNode? issueInfo = doc.SelectSingleNode(
            "//dmIdent/issueInfo|//dmaddres/issno|//pmIdent/issueInfo|//pmaddres/issno|" +
            "//dmlIdent/issueInfo|//dml/issno|//imfIdent/issueInfo|//updateIdent/issueInfo");

        if (code is not XmlElement c)
        {
            return null;
        }

        string Prop30(string child) => c.SelectSingleNode(child)?.InnerText ?? string.Empty;
        string Attr(string a) => c.GetAttribute(a);

        ident.ModelIdentCode = iss30 ? Prop30("modelic") : Attr("modelIdentCode");

        if (ident.Type == "PM")
        {
            if (iss30)
            {
                ident.SenderIdent = Prop30("pmissuer");
                ident.PmNumber = Prop30("pmnumber");
                ident.PmVolume = Prop30("pmvolume");
            }
            else
            {
                ident.SenderIdent = Attr("pmIssuer");
                ident.PmNumber = Attr("pmNumber");
                ident.PmVolume = Attr("pmVolume");
            }
        }
        else if (ident.Type is "DML" or "COM")
        {
            if (iss30)
            {
                ident.SenderIdent = Prop30("sendid");
                ident.YearOfDataIssue = Prop30("diyear");
                ident.SeqNumber = Prop30("seqnum");
            }
            else
            {
                ident.SenderIdent = Attr("senderIdent");
                ident.YearOfDataIssue = Attr("yearOfDataIssue");
                ident.SeqNumber = Attr("seqNumber");
            }

            if (ident.Type == "DML")
            {
                ident.DmlCommentType = iss30
                    ? (c.SelectSingleNode("dmltype") as XmlElement)?.GetAttribute("type")
                    : Attr("dmlType");
            }
            else
            {
                ident.DmlCommentType = iss30
                    ? (c.SelectSingleNode("ctype") as XmlElement)?.GetAttribute("type")
                    : Attr("commentType");
            }
        }
        else if (ident.Type == "DDN")
        {
            if (iss30)
            {
                ident.SenderIdent = Prop30("sendid");
                ident.ReceiverIdent = Prop30("recvid");
                ident.YearOfDataIssue = Prop30("diyear");
                ident.SeqNumber = Prop30("seqnum");
            }
            else
            {
                ident.SenderIdent = Attr("senderIdent");
                ident.ReceiverIdent = Attr("receiverIdent");
                ident.YearOfDataIssue = Attr("yearOfDataIssue");
                ident.SeqNumber = Attr("seqNumber");
            }
        }
        else if (ident.Type is "DM" or "UPF")
        {
            if (iss30)
            {
                ident.SystemDiffCode = Prop30("sdc");
                ident.SystemCode = Prop30("chapnum");
                ident.SubSystemCode = Prop30("section");
                ident.SubSubSystemCode = Prop30("subsect");
                ident.AssyCode = Prop30("subject");
                ident.DisassyCode = Prop30("discode");
                ident.DisassyCodeVariant = Prop30("discodev");
                ident.InfoCode = Prop30("incode");
                ident.InfoCodeVariant = Prop30("incodev");
                ident.ItemLocationCode = Prop30("itemloc");
            }
            else
            {
                ident.SystemDiffCode = Attr("systemDiffCode");
                ident.SystemCode = Attr("systemCode");
                ident.SubSystemCode = Attr("subSystemCode");
                ident.SubSubSystemCode = Attr("subSubSystemCode");
                ident.AssyCode = Attr("assyCode");
                ident.DisassyCode = Attr("disassyCode");
                ident.DisassyCodeVariant = Attr("disassyCodeVariant");
                ident.InfoCode = Attr("infoCode");
                ident.InfoCodeVariant = Attr("infoCodeVariant");
                ident.ItemLocationCode = Attr("itemLocationCode");
                ident.LearnCode = c.HasAttribute("learnCode") ? Attr("learnCode") : null;
                ident.LearnEventCode = c.HasAttribute("learnEventCode") ? Attr("learnEventCode") : null;
            }
        }
        else if (ident.Type == "IMF")
        {
            ident.ImfIdentIcn = Attr("imfIdentIcn");
        }

        if (ident.Type is "DM" or "PM" or "DML" or "IMF" or "UPF")
        {
            if (issueInfo is not XmlElement ii)
            {
                return null;
            }
            ident.IssueNumber = iss30 ? ii.GetAttribute("issno") : ii.GetAttribute("issueNumber");
            ident.InWork = iss30 ? ii.GetAttribute("inwork") : ii.GetAttribute("inWork");
            if (string.IsNullOrEmpty(ident.InWork))
            {
                ident.InWork = "00";
            }
        }

        if (ident.Type is "DM" or "PM" or "COM" or "UPF")
        {
            if (language is not XmlElement le)
            {
                return null;
            }
            ident.LanguageIsoCode = iss30 ? le.GetAttribute("language") : le.GetAttribute("languageIsoCode");
            ident.CountryIsoCode = iss30 ? le.GetAttribute("country") : le.GetAttribute("countryIsoCode");
        }

        if (identExtension is XmlElement ext)
        {
            ident.Extended = true;
            if (iss30)
            {
                ident.ExtensionProducer = ext.SelectSingleNode("dmeproducer")?.InnerText ?? string.Empty;
                ident.ExtensionCode = ext.SelectSingleNode("dmecode")?.InnerText ?? string.Empty;
            }
            else
            {
                ident.ExtensionProducer = ext.GetAttribute("extensionProducer");
                ident.ExtensionCode = ext.GetAttribute("extensionCode");
            }
        }

        return ident;
    }

    // ---- update-instances (ported from find_source / load_*_from_inst) ----

    /// <summary>
    /// Locate the <c>sourceDmIdent</c>/<c>sourcePmIdent</c>/<c>srcdmaddres</c> of an
    /// instance, so its source master object can be re-derived. Returns the source
    /// ident node, or null if the object has no source ident. Mirrors the
    /// node-finding part of <c>find_source</c> (the file lookup is performed by the
    /// caller, which owns the filesystem search helpers).
    /// </summary>
    public static XmlNode? FindSourceIdent(XmlDocument inst)
    {
        return inst.SelectSingleNode("//sourceDmIdent|//sourcePmIdent|//srcdmaddres");
    }

    /// <summary>
    /// Load applicability definitions from the applic of an instance into
    /// <paramref name="defs"/>. Asserts inside an <c>or</c> evaluation are ignored
    /// (the defs mechanism cannot represent them). The values are added as
    /// non-user-defined so explicit <c>-s</c> assertions take precedence. Mirrors
    /// <c>load_applic_from_inst</c>.
    /// </summary>
    public static void LoadApplicFromInst(XmlElement defs, XmlDocument doc)
    {
        XmlNodeList? asserts = doc.SelectNodes(
            "//identAndStatusSection//applic[1]//assert[not(ancestor::evaluate/@andOr = 'or')]");
        if (asserts == null)
        {
            return;
        }

        foreach (XmlNode n in asserts)
        {
            if (n is not XmlElement a)
            {
                continue;
            }
            string ident = a.GetAttribute("applicPropertyIdent");
            string type = a.GetAttribute("applicPropertyType");
            string value = a.GetAttribute("applicPropertyValues");
            if (ident.Length == 0 || type.Length == 0 || value.Length == 0)
            {
                continue;
            }
            // userDefined=false: user-defined assertions override those in the instance.
            DefineApplicValue(defs, ident, type, value, perDm: true, userDefined: false);
        }
    }

    /// <summary>
    /// Read the skill level code from an instance, if present. Mirrors
    /// <c>load_skill_from_inst</c>.
    /// </summary>
    public static string? LoadSkillFromInst(XmlDocument doc) =>
        FirstAttr(doc, "//skillLevel/@skillLevelCode|//skill/@skill");

    /// <summary>
    /// Read the security classification from an instance, if present. Mirrors
    /// <c>load_sec_from_inst</c>.
    /// </summary>
    public static string? LoadSecFromInst(XmlDocument doc) =>
        FirstAttr(doc, "//security/@securityClassification|//security/@class");

    /// <summary>
    /// Return the <c>repositorySourceDmIdent</c> nodes referenced by an instance
    /// (CIRs that were resolved when it was created), removing them from the
    /// instance. Mirrors <c>add_cirs_from_inst</c>; the caller resolves each ident
    /// to a file. The returned nodes are detached copies.
    /// </summary>
    public static IReadOnlyList<XmlNode> TakeCirsFromInst(XmlDocument doc)
    {
        var result = new List<XmlNode>();
        var nodes = new List<XmlNode>();
        foreach (XmlNode n in doc.SelectNodes("//repositorySourceDmIdent")!)
        {
            nodes.Add(n);
        }
        foreach (XmlNode n in nodes)
        {
            result.Add(n);
            n.ParentNode?.RemoveChild(n);
        }
        return result;
    }

    private static string? FirstAttr(XmlDocument doc, string xpath)
    {
        XmlNode? n = doc.SelectSingleNode(xpath);
        return n?.Value;
    }
}
