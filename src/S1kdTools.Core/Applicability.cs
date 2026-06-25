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
}
