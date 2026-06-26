using System.Xml;

namespace S1kdTools;

/// <summary>
/// A set of user-supplied applicability definitions plus the evaluation of
/// S1000D <c>&lt;assert&gt;</c> / <c>&lt;evaluate&gt;</c> statements against
/// them. Ported from the applicability functions in
/// <c>tools/common/s1kd_tools.c</c> (and the public <c>s1kdApplicability</c> API
/// from <c>libs1kd</c>).
/// </summary>
public sealed class Applicability
{
    private readonly XmlDocument _doc;
    private readonly XmlElement _defs;

    /// <summary>Create an empty set of applicability definitions.</summary>
    public Applicability()
    {
        _doc = XmlUtils.NewDocument();
        _defs = _doc.CreateElement("assigns");
        _doc.AppendChild(_defs);
    }

    /// <summary>The underlying definitions element (one &lt;assign&gt; per value).</summary>
    public XmlElement Definitions => _defs;

    /// <summary>
    /// Assign a value to an applicability property (mirrors <c>s1kdAssign</c>).
    /// </summary>
    /// <param name="ident">The property identifier.</param>
    /// <param name="type">The property type (<c>prodattr</c> or <c>condition</c>).</param>
    /// <param name="value">The assigned value.</param>
    public void Assign(string ident, string type, string value)
    {
        var assign = _doc.CreateElement("assign");
        assign.SetAttribute("applicPropertyIdent", ident);
        assign.SetAttribute("applicPropertyType", type);
        assign.SetAttribute("applicPropertyValues", value);
        _defs.AppendChild(assign);
    }

    /// <summary>
    /// Evaluate any <c>&lt;assert&gt;</c> or <c>&lt;evaluate&gt;</c> node against
    /// these definitions. Mirrors <c>eval_applic</c>.
    /// </summary>
    /// <param name="node">The assertion or evaluation node.</param>
    /// <param name="assume">
    /// When true, properties not defined by the user are assumed applicable
    /// (used to find content that may be removed); when false, an undefined
    /// property makes the statement evaluate as not applicable (used to find
    /// statements that are unambiguously true).
    /// </param>
    public bool Eval(XmlNode node, bool assume) => EvalApplic(_defs, node, assume);

    /// <summary>
    /// Test whether <paramref name="ident"/>:<paramref name="type"/> =
    /// <paramref name="value"/> is satisfied by the definitions. Mirrors
    /// <c>is_applic</c>.
    /// </summary>
    public static bool IsApplic(XmlNode defs, string? ident, string? type, string? value, bool assume)
    {
        bool result = assume;

        if (ident == null || type == null || value == null)
        {
            return assume;
        }

        for (XmlNode? cur = defs.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur is not XmlElement el)
            {
                continue;
            }

            string curIdent = el.GetAttribute("applicPropertyIdent");
            string curType = el.GetAttribute("applicPropertyType");
            bool hasValue = el.HasAttribute("applicPropertyValues");
            string curValue = el.GetAttribute("applicPropertyValues");

            bool match = curIdent == ident && curType == type;
            if (match)
            {
                if (hasValue)
                {
                    result = Csdb.IsInSet(curValue, value);
                }
                else if (assume)
                {
                    result = EvalMulti(el, value);
                }
                break;
            }
        }

        return result;
    }

    /// <summary>Evaluate multiple child values for a property (mirrors <c>eval_multi</c>).</summary>
    private static bool EvalMulti(XmlNode multi, string value)
    {
        for (XmlNode? cur = multi.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (Csdb.IsInSet(cur.InnerText, value))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Evaluate an &lt;assert&gt; element (mirrors <c>eval_assert</c>).</summary>
    public static bool EvalAssert(XmlNode defs, XmlNode assert, bool assume)
    {
        string? ident = FirstAttr(assert, "applicPropertyIdent", "actidref");
        string? type = FirstAttr(assert, "applicPropertyType", "actreftype");
        string? values = FirstAttr(assert, "applicPropertyValues", "actvalues");
        return IsApplic(defs, ident, type, values, assume);
    }

    private static string? FirstAttr(XmlNode node, string a, string b)
    {
        if (node.Attributes == null)
        {
            return null;
        }
        return node.Attributes[a]?.Value ?? node.Attributes[b]?.Value;
    }

    /// <summary>Evaluate an &lt;evaluate&gt; element (mirrors <c>eval_evaluate</c>).</summary>
    public static bool EvalEvaluate(XmlNode defs, XmlNode evaluate, bool assume)
    {
        string? andOr = FirstAttr(evaluate, "andOr", "operator");
        if (andOr == null)
        {
            return false;
        }

        bool isAnd = andOr == "and";
        bool ret = assume;

        for (XmlNode? cur = evaluate.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.LocalName is "assert" or "evaluate")
            {
                ret = EvalApplic(defs, cur, assume);
                if ((isAnd && !ret) || (!isAnd && ret))
                {
                    break;
                }
            }
        }

        return ret;
    }

    /// <summary>Generic dispatch for &lt;assert&gt; or &lt;evaluate&gt; (mirrors <c>eval_applic</c>).</summary>
    public static bool EvalApplic(XmlNode defs, XmlNode node, bool assume)
    {
        return node.LocalName switch
        {
            "assert" => EvalAssert(defs, node, assume),
            "evaluate" => EvalEvaluate(defs, node, assume),
            _ => false,
        };
    }

    /// <summary>
    /// Determine whether two applicability annotations are structurally the
    /// same. Mirrors <c>same_annotation</c> (which compares C14N output);
    /// here we compare canonicalised XML.
    /// </summary>
    public static bool SameAnnotation(XmlNode a, XmlNode b) =>
        Canonicalize(a) == Canonicalize(b);

    private static string Canonicalize(XmlNode node)
    {
        // Normalise by reloading without insignificant whitespace, then emit
        // outer XML. This is sufficient to detect duplicate annotations.
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.LoadXml(node.OuterXml);
        return doc.DocumentElement?.OuterXml ?? string.Empty;
    }

    // ----------------------------------------------------------------------
    // CCT dependency injection (mirrors add_cct_depends and friends from
    // tools/common/s1kd_tools.c).
    // ----------------------------------------------------------------------

    /// <summary>
    /// Inject Conditions Cross-reference Table (CCT) dependency information into
    /// the applicability annotations of <paramref name="doc"/>. For every
    /// condition (<c>cond</c>) in the CCT that declares a dependency, any
    /// assertion in the document that uses one of the dependant condition values
    /// is rewritten so that the dependency test is ANDed onto it. Mirrors
    /// <c>add_cct_depends</c>.
    /// </summary>
    /// <param name="doc">The document whose annotations are mutated in place.</param>
    /// <param name="cct">The Conditions Cross-reference Table document.</param>
    /// <param name="id">
    /// When null, all conditions are processed. When non-null, only the
    /// dependencies of the condition with that <c>id</c> are processed (used
    /// when resolving sub-dependencies).
    /// </param>
    public static void AddCctDepends(XmlDocument doc, XmlDocument cct, string? id)
    {
        if (doc.DocumentElement == null || cct.DocumentElement == null)
        {
            return;
        }

        string xpath = id != null
            ? $"//cond[@id='{id}']/dependency"
            : "//cond/dependency";

        XmlNodeList? deps = cct.SelectNodes(xpath);
        if (deps == null)
        {
            return;
        }

        // Snapshot the dependency list; processing recurses and mutates doc but
        // not the cct node-set.
        var list = new List<XmlNode>();
        foreach (XmlNode dep in deps)
        {
            list.Add(dep);
        }

        foreach (XmlNode dep in list)
        {
            AddCctDepend(doc, dep);
        }
    }

    /// <summary>Add a single dependency from the CCT (mirrors <c>add_cct_depend</c>).</summary>
    private static void AddCctDepend(XmlDocument doc, XmlNode dep)
    {
        if (dep is not XmlElement depEl || depEl.ParentNode is not XmlElement cond)
        {
            return;
        }

        string condId = cond.GetAttribute("id");
        string test = depEl.GetAttribute("dependencyTest");
        string vals = depEl.GetAttribute("forCondValues");

        // Find the annotation in the CCT for the dependency test.
        XmlNode? applic = depEl.OwnerDocument?.SelectSingleNode($"//applic[@id='{test}']");
        if (applic == null)
        {
            return;
        }

        // Add dependency tests to assertions in doc that use the dependant
        // values. The assertion query is re-run for each value because each
        // match may replace the assertion node.
        foreach (string v in vals.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            XmlNodeList? asserts = doc.SelectNodes(
                $"//assert[@applicPropertyIdent='{condId}' and @applicPropertyType='condition']");
            if (asserts == null)
            {
                continue;
            }

            var snapshot = new List<XmlNode>();
            foreach (XmlNode a in asserts)
            {
                snapshot.Add(a);
            }

            foreach (XmlNode assert in snapshot)
            {
                AddCctDependToAssert(assert, condId, v, applic);
            }
        }

        // Handle subdependencies: dependant values which themselves have
        // dependencies. Each condition referenced inside the dependency-test
        // annotation is resolved recursively.
        XmlNodeList? subAsserts = applic.SelectNodes(
            ".//assert[@applicPropertyIdent and @applicPropertyType='condition']");
        if (subAsserts != null)
        {
            var snapshot = new List<XmlNode>();
            foreach (XmlNode a in subAsserts)
            {
                snapshot.Add(a);
            }

            XmlDocument cctDoc = (XmlDocument)applic.OwnerDocument!;
            foreach (XmlNode subAssert in snapshot)
            {
                string ident = ((XmlElement)subAssert).GetAttribute("applicPropertyIdent");
                AddCctDepends(doc, cctDoc, ident);
            }
        }
    }

    /// <summary>
    /// Add a dependency test to an assertion if it contains any of the dependent
    /// values. If the assertion uses a set (<c>|</c>), the values are split so
    /// the dependency is only added to the appropriate values. Mirrors
    /// <c>add_cct_depend_to_assert</c>.
    /// </summary>
    private static void AddCctDependToAssert(XmlNode assert, string id, string forval, XmlNode applic)
    {
        XmlDocument owner = assert.OwnerDocument!;
        string vals = (assert as XmlElement)?.GetAttribute("applicPropertyValues") ?? string.Empty;

        bool match = false;
        XmlElement eval = owner.CreateElement("evaluate");
        eval.SetAttribute("andOr", "or");

        // Split any sets (|); strtok skips empty tokens, so empties are dropped.
        foreach (string v in vals.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            XmlElement a = owner.CreateElement("assert");
            a.SetAttribute("applicPropertyIdent", id);
            a.SetAttribute("applicPropertyType", "condition");
            a.SetAttribute("applicPropertyValues", v);

            if (v == forval)
            {
                // This value has a dependency; AND the dependency test onto it.
                match = true;

                XmlElement e = owner.CreateElement("evaluate");
                e.SetAttribute("andOr", "and");
                eval.AppendChild(e);

                for (XmlNode? cur = applic.FirstChild; cur != null; cur = cur.NextSibling)
                {
                    if (cur.LocalName is "assert" or "evaluate")
                    {
                        e.AppendChild(owner.ImportNode(cur, true));
                    }
                }

                e.AppendChild(a);
            }
            else
            {
                eval.AppendChild(a);
            }
        }

        if (!match)
        {
            return;
        }

        XmlNode? assertParent = assert.ParentNode;
        if (assertParent == null)
        {
            return;
        }

        string? op = (assertParent as XmlElement)?.GetAttribute("andOr");
        // GetAttribute returns "" when absent; treat that as null to match the C
        // (which uses xmlGetProp -> NULL).
        if (string.IsNullOrEmpty(op))
        {
            op = null;
        }

        // If the dependency test is being added to an OR evaluate, or the new
        // evaluate has a single child, simplify by combining the new OR with the
        // existing OR (insert the new OR's contents as siblings of the assert).
        bool singleChild = eval.FirstChild != null && eval.FirstChild.NextSibling == null;
        if (singleChild || op == "or")
        {
            XmlNode? last = assert;
            for (XmlNode? cur = eval.FirstChild; cur != null; cur = cur.NextSibling)
            {
                string? o = (cur as XmlElement)?.GetAttribute("andOr");
                if (string.IsNullOrEmpty(o))
                {
                    o = null;
                }

                if (o != null && o == op)
                {
                    // Combine evaluates with the same operation.
                    for (XmlNode? c = cur.FirstChild; c != null; c = c.NextSibling)
                    {
                        XmlNode imported = owner.ImportNode(c, true);
                        last = assertParent.InsertAfter(imported, last);
                    }
                }
                else
                {
                    XmlNode imported = owner.ImportNode(cur, true);
                    last = assertParent.InsertAfter(imported, last);
                }
            }
        }
        else
        {
            // Otherwise, just add the new OR after the assert.
            assertParent.InsertAfter(eval, assert);
        }

        assertParent.RemoveChild(assert);
    }
}
