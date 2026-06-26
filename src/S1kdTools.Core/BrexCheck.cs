using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

namespace S1kdTools;

/// <summary>
/// BREX check options, mirroring the bit flags in
/// <c>reference/tools/libs1kd/include/s1kd/brexcheck.h</c>
/// (<c>s1kdBREXCheckOption</c>).
/// </summary>
[Flags]
public enum BrexCheckOptions
{
    None = 0,

    /// <summary>Check object values (<c>S1KD_BREXCHECK_VALUES</c>).</summary>
    Values = 1,

    /// <summary>Check SNS rules (<c>S1KD_BREXCHECK_SNS</c>).</summary>
    Sns = 2,

    /// <summary>Use strict SNS checking (<c>S1KD_BREXCHECK_STRICT_SNS</c>).</summary>
    StrictSns = 4,

    /// <summary>Use unstrict SNS checking (<c>S1KD_BREXCHECK_UNSTRICT_SNS</c>).</summary>
    UnstrictSns = 8,

    /// <summary>Check notation rules (<c>S1KD_BREXCHECK_NOTATIONS</c>).</summary>
    Notations = 16,

    /// <summary>Output errors to console (<c>S1KD_BREXCHECK_NORMAL_LOG</c>).</summary>
    NormalLog = 32,

    /// <summary>Output errors and informative messages (<c>S1KD_BREXCHECK_VERBOSE_LOG</c>).</summary>
    VerboseLog = 64,
}

/// <summary>
/// A parsed BREX severity-levels (<c>brsl</c>) configuration, mirroring the
/// <c>.brseveritylevels</c> file consumed by the C tool's <c>-w</c> option.
///
/// <para>
/// The file lists the severity levels referenced by a BREX rule's
/// <c>brSeverityLevel</c> attribute, giving each a user-defined <c>type</c> name
/// (the element text) and, optionally, a <c>fail</c> flag. A violated rule whose
/// severity level has <c>fail="no"</c> is reported but is <em>not</em> counted as
/// an error in the exit status; any other value (or an absent flag, or an
/// unknown severity) counts as a failure. Mirrors the C <c>is_failure</c> /
/// <c>brsl_type</c> helpers.
/// </para>
/// </summary>
public sealed class BrexSeverityLevels
{
    private readonly XmlDocument _doc;

    public BrexSeverityLevels(XmlDocument doc) => _doc = doc;

    /// <summary>The underlying <c>brSeverityLevels</c> document.</summary>
    public XmlDocument Document => _doc;

    /// <summary>
    /// Whether a violated rule with the given severity level counts as a failure.
    /// Mirrors <c>is_failure</c>: true unless a matching <c>brSeverityLevel</c>
    /// has <c>fail="no"</c>. An unknown severity defaults to a failure.
    /// </summary>
    public bool IsFailure(string severity)
    {
        XmlNodeList? levels = _doc.SelectNodes("//brSeverityLevel");
        if (levels != null)
        {
            foreach (XmlNode n in levels)
            {
                if (n is XmlElement el &&
                    string.Equals(el.GetAttribute("value"), severity, StringComparison.Ordinal))
                {
                    // xmlGetProp returns NULL when @fail is absent; xmlStrcmp(NULL,"no")
                    // is non-zero, so an absent @fail counts as a failure.
                    return !string.Equals(el.GetAttribute("fail"), "no", StringComparison.Ordinal);
                }
            }
        }
        return true;
    }

    /// <summary>
    /// The user-defined type name (element text) for the given severity level, or
    /// null when no <c>brSeverityLevel</c> with that <c>value</c> exists. Mirrors
    /// <c>brsl_type</c> (which returns the node content, i.e. "" for an empty
    /// element).
    /// </summary>
    public string? Type(string severity)
    {
        XmlNodeList? levels = _doc.SelectNodes("//brSeverityLevel");
        if (levels != null)
        {
            foreach (XmlNode n in levels)
            {
                if (n is XmlElement el &&
                    string.Equals(el.GetAttribute("value"), severity, StringComparison.Ordinal))
                {
                    return el.InnerText;
                }
            }
        }
        return null;
    }
}

/// <summary>
/// A single BREX data module in a (possibly layered) BREX chain, paired with the
/// path/identifier recorded for it in the report's <c>brex/@path</c>.
/// </summary>
public readonly struct BrexModule
{
    public BrexModule(XmlDocument document, string path)
    {
        Document = document;
        Path = path;
    }

    /// <summary>The parsed BREX data module.</summary>
    public XmlDocument Document { get; }

    /// <summary>The path/identifier to record for this BREX in the report.</summary>
    public string Path { get; }
}

/// <summary>
/// Programmatic API for checking CSDB objects against BREX (Business Rules
/// EXchange) data modules.
///
/// <para>
/// SNS checks (<see cref="BrexCheckOptions.Sns"/>) are implemented for data
/// modules in normal/strict/unstrict modes and are merged across a layered BREX
/// chain (the SNS rules from every BREX in the chain are combined, mirroring the
/// C <c>check_brex_sns</c>). Notation checks (<see cref="BrexCheckOptions.Notations"/>)
/// enumerate the NOTATIONs actually referenced by the document's internal-DTD
/// unparsed-entity (<c>NDATA</c>) declarations and validate each against the
/// combined <c>notationRuleList</c> of the BREX chain, mirroring
/// <c>check_brex_notations</c>. Layered BREX is supported by passing the resolved
/// chain to <see cref="Check(XmlDocument, IReadOnlyList{BrexModule}, BrexCheckOptions, string, bool, out XmlDocument)"/>.
/// </para>
/// </summary>
public static class BrexCheck
{
    private const string XsiUri = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Check <paramref name="doc"/> against the BREX module <paramref name="brex"/>.
    /// Returns the number of BREX errors found (0 = conforms). The XML report is
    /// returned in <paramref name="report"/>.
    /// </summary>
    public static int Check(XmlDocument doc, XmlDocument brex, BrexCheckOptions opts, out XmlDocument report)
    {
        return Check(doc, brex, opts, "-", "-", out report);
    }

    /// <summary>
    /// Check <paramref name="doc"/> against <paramref name="brex"/>, recording the
    /// given document and BREX paths in the report (mirrors the C tool's
    /// <c>document/@path</c> and <c>brex/@path</c>).
    /// </summary>
    public static int Check(XmlDocument doc, XmlDocument brex, BrexCheckOptions opts,
        string docPath, string brexPath, out XmlDocument report)
    {
        return Check(doc, new[] { new BrexModule(brex, brexPath) }, opts, docPath, layered: false, out report);
    }

    /// <summary>
    /// Check <paramref name="doc"/> against a chain of BREX modules. The
    /// structure object rules of every BREX are evaluated in turn (each producing
    /// its own <c>brex</c> element), while the SNS rules and notation rules are
    /// merged across the whole chain before checking (mirroring the C
    /// <c>check_brex</c> / <c>check_brex_sns</c> / <c>check_brex_notations</c>).
    /// Returns the total number of errors found.
    /// </summary>
    public static int Check(XmlDocument doc, IReadOnlyList<BrexModule> brexChain, BrexCheckOptions opts,
        string docPath, bool layered, out XmlDocument report)
    {
        return Check(doc, brexChain, opts, docPath, layered, null, out report);
    }

    /// <summary>
    /// Check <paramref name="doc"/> against a chain of BREX modules, applying a
    /// BREX severity-levels (<c>brsl</c>) configuration. Violated rules are tagged
    /// with their <c>brSeverityLevel</c> and a <c>type</c> element; rules whose
    /// severity has <c>fail="no"</c> are reported but not counted towards the
    /// returned error total. Mirrors the C <c>check_brex_rules</c> when a
    /// <c>.brseveritylevels</c> file is in effect.
    /// </summary>
    public static int Check(XmlDocument doc, IReadOnlyList<BrexModule> brexChain, BrexCheckOptions opts,
        string docPath, bool layered, BrexSeverityLevels? brsl, out XmlDocument report)
    {
        report = XmlUtils.NewDocument();
        XmlElement brexCheck = report.CreateElement("brexCheck");
        report.AppendChild(brexCheck);
        AddConfigToReport(brexCheck, opts, layered);

        XmlElement documentNode = (XmlElement)brexCheck.AppendChild(report.CreateElement("document"))!;
        documentNode.SetAttribute("path", docPath);

        int total = 0;

        // SNS and notation rules are checked once, against the combined rules of
        // all BREX in the chain.
        if (opts.HasFlag(BrexCheckOptions.Sns))
        {
            if (!CheckSns(brexChain, doc, documentNode, opts))
            {
                ++total;
            }
        }

        if (opts.HasFlag(BrexCheckOptions.Notations))
        {
            total += CheckNotations(brexChain, doc, documentNode);
        }

        // Structure object rules are checked per-BREX (one <brex> per layer).
        foreach (BrexModule layer in brexChain)
        {
            total += CheckStructureRules(layer.Document, doc, layer.Path, documentNode, opts, brsl);
        }

        return total;
    }

    /// <summary>
    /// Check <paramref name="doc"/> against the appropriate S1000D default BREX,
    /// chosen from the document's schema (mirrors <c>s1kdDocCheckDefaultBREX</c>).
    /// </summary>
    public static int CheckDefault(XmlDocument doc, BrexCheckOptions opts, out XmlDocument report)
    {
        string code = DefaultBrexDmc(doc);
        XmlDocument brex = LoadDefaultBrex(code)
            ?? throw new InvalidOperationException($"No default BREX found for {code}.");
        return Check(doc, brex, opts, doc.BaseURI, code, out report);
    }

    /// <summary>
    /// Check <paramref name="doc"/> against a single explicit BREX module across a
    /// chain of one (compatibility wrapper for layered callers).
    /// </summary>
    public static int Check(XmlDocument doc, IReadOnlyList<BrexModule> brexChain, BrexCheckOptions opts,
        string docPath, out XmlDocument report)
    {
        return Check(doc, brexChain, opts, docPath, brexChain.Count > 1, out report);
    }

    // ---- Structure object rules -------------------------------------------------

    private static int CheckStructureRules(XmlDocument brex, XmlDocument doc, string brexPath,
        XmlElement documentNode, BrexCheckOptions opts, BrexSeverityLevels? brsl = null)
    {
        XmlDocument report = documentNode.OwnerDocument!;
        XmlElement brexNode = (XmlElement)documentNode.AppendChild(report.CreateElement("brex"))!;
        brexNode.SetAttribute("path", brexPath);

        string? defaultSeverity = (XmlUtils.XPathFirstNode(brex, null, "//brex") as XmlElement)?
            .GetAttribute("defaultBrSeverityLevel");
        if (string.IsNullOrEmpty(defaultSeverity))
        {
            defaultSeverity = null;
        }

        int nerr = 0;
        foreach (XmlElement rule in SelectStructureObjectRules(brex, doc))
        {
            XmlNode? brDecisionRef = rule.SelectSingleNode("brDecisionRef");
            XmlNode? objectPathNode = rule.SelectSingleNode("objectPath|objpath");
            XmlNode? objectUseNode = rule.SelectSingleNode("objectUse|objuse");

            string path = objectPathNode?.InnerText ?? string.Empty;
            string use = objectUseNode?.InnerText ?? string.Empty;
            string? allowedObjectFlag =
                (objectPathNode as XmlElement)?.GetAttribute("allowedObjectFlag")
                is { Length: > 0 } a ? a
                : ((objectPathNode as XmlElement)?.GetAttribute("objappl") is { Length: > 0 } b ? b : null);

            XmlNodeList? matched;
            try
            {
                matched = doc.SelectNodes(path);
            }
            catch (XPathException)
            {
                // Invalid object path (e.g. requires XPath 2.0 / unsupported EXSLT).
                XmlElement xpErr = (XmlElement)brexNode.AppendChild(report.CreateElement("xpathError"))!;
                xpErr.InnerText = path;
                if (objectPathNode != null)
                {
                    xpErr.SetAttribute("xpath", XmlUtils.XPathOf(objectPathNode));
                }
                continue;
            }

            if (!IsInvalid(rule, allowedObjectFlag, matched, opts))
            {
                continue;
            }

            string? severity = (rule.GetAttribute("brSeverityLevel") is { Length: > 0 } s) ? s : defaultSeverity;

            XmlElement brexError = (XmlElement)brexNode.AppendChild(report.CreateElement("error"))!;
            if (severity != null)
            {
                brexError.SetAttribute("brSeverityLevel", severity);

                // With a brsl configuration, record the user-defined type name of
                // the severity level (mirrors the brsl_fname branch in the C).
                if (brsl != null)
                {
                    XmlElement typeNode = (XmlElement)brexError.AppendChild(report.CreateElement("type"))!;
                    typeNode.InnerText = brsl.Type(severity) ?? string.Empty;
                }
            }
            else
            {
                brexError.SetAttribute("fail", "yes");
            }

            if (brDecisionRef != null)
            {
                brexError.AppendChild(report.ImportNode(brDecisionRef, true));
            }

            XmlElement errPath = (XmlElement)brexError.AppendChild(report.CreateElement("objectPath"))!;
            errPath.InnerText = path;
            if (allowedObjectFlag != null)
            {
                errPath.SetAttribute("allowedObjectFlag", allowedObjectFlag);
            }

            XmlElement errUse = (XmlElement)brexError.AppendChild(report.CreateElement("objectUse"))!;
            errUse.InnerText = use;

            AddObjectValues(brexError, rule);

            if (matched != null && matched.Count > 0)
            {
                DumpNodesXml(matched, brexError, rule, opts);
            }

            // Decide whether this violation counts as a true failure. Mirrors the
            // tail of the C is_invalid block: a rule with a severity level whose
            // brsl entry has fail="no" is reported (with fail="no") but not
            // counted; everything else (including any severity when no brsl is in
            // effect) counts as an error.
            if (severity != null && brsl != null)
            {
                if (brsl.IsFailure(severity))
                {
                    ++nerr;
                }
                else
                {
                    brexError.SetAttribute("fail", "no");
                }
            }
            else
            {
                ++nerr;
            }
        }

        if (!brexNode.HasChildNodes)
        {
            brexNode.AppendChild(report.CreateElement("noErrors"));
        }

        return nerr;
    }

    /// <summary>
    /// Select the structure object rules from a BREX document, scoped to the
    /// object's schema. Mirrors <c>STRUCT_OBJ_RULE_PATH</c> from the C tool,
    /// substituting the object's schema for the C <c>$schema</c> XPath variable.
    /// </summary>
    private static IEnumerable<XmlElement> SelectStructureObjectRules(XmlDocument brex, XmlDocument doc)
    {
        string schema = SchemaOf(doc);
        string q = XPathLiteral(schema);

        string expr =
            $"//contextRules[not(@rulesContext) or @rulesContext={q}]//structureObjectRule|" +
            $"//contextrules[not(@context) or @context={q}]//objrule";

        XmlNodeList? rules = brex.SelectNodes(expr);
        if (rules == null)
        {
            yield break;
        }
        foreach (XmlNode n in rules)
        {
            if (n is XmlElement el)
            {
                yield return el;
            }
        }
    }

    /// <summary>
    /// Determine whether a context rule is violated, given its
    /// <c>allowedObjectFlag</c> and the set of nodes matched by the object path.
    /// Mirrors <c>is_invalid</c>.
    /// </summary>
    private static bool IsInvalid(XmlElement rule, string? allowedObjectFlag, XmlNodeList? matched, BrexCheckOptions opts)
    {
        bool invalid = false;
        bool hasNodes = matched != null && matched.Count > 0;

        if (allowedObjectFlag == "0")
        {
            // The object must NOT exist.
            invalid = hasNodes;
        }
        else if (allowedObjectFlag == "1")
        {
            // The object MUST exist.
            invalid = !hasNodes;
        }

        if (!invalid && opts.HasFlag(BrexCheckOptions.Values))
        {
            invalid = !CheckObjectsValues(rule, matched);
        }

        return invalid;
    }

    /// <summary>Copy the allowed object values into the report (mirrors <c>add_object_values</c>).</summary>
    private static void AddObjectValues(XmlElement brexError, XmlElement rule)
    {
        XmlDocument report = brexError.OwnerDocument!;
        XmlNodeList? values = rule.SelectNodes("objectValue|objval");
        if (values == null)
        {
            return;
        }
        foreach (XmlNode v in values)
        {
            brexError.AppendChild(report.ImportNode(v, true));
        }
    }

    /// <summary>Dump the branches that violate a rule into the report (mirrors <c>dump_nodes_xml</c>).</summary>
    private static void DumpNodesXml(XmlNodeList nodes, XmlElement brexError, XmlElement rule, BrexCheckOptions opts)
    {
        XmlDocument report = brexError.OwnerDocument!;
        foreach (XmlNode node in nodes)
        {
            if (opts.HasFlag(BrexCheckOptions.Values) && CheckSingleObjectValues(rule, node))
            {
                continue;
            }

            XmlElement obj = (XmlElement)brexError.AppendChild(report.CreateElement("object"))!;
            obj.SetAttribute("xpath", XmlUtils.XPathOf(node));

            XmlNode toCopy = node.NodeType == XmlNodeType.Attribute
                ? ((XmlAttribute)node).OwnerElement!
                : node;

            // The C copies with extra=2 (this node, no children) by default, or a
            // deep copy when -8 is given. We mirror the shallow-by-default form.
            XmlNode imported = report.ImportNode(toCopy, false);
            obj.AppendChild(imported);
        }
    }

    // ---- Value checking ---------------------------------------------------------

    /// <summary>
    /// Get the allowed-value string from an <c>objectValue</c>/<c>objval</c> node.
    /// Handles both S1000D &lt;= 3.0 (<c>@val1~@val2</c>) and &gt;= 4.0
    /// (<c>@valueAllowed</c>). Mirrors <c>get_value_allowed</c>.
    /// </summary>
    internal static string? GetValueAllowed(XmlNode objval)
    {
        if (objval is not XmlElement el)
        {
            return null;
        }

        string val1 = el.GetAttribute("val1");
        if (!string.IsNullOrEmpty(val1) || el.HasAttribute("val1"))
        {
            string allowed = val1;
            string val2 = el.GetAttribute("val2");
            if (!string.IsNullOrEmpty(val2) || el.HasAttribute("val2"))
            {
                allowed += "~" + val2;
            }
            return allowed;
        }

        return el.HasAttribute("valueAllowed") ? el.GetAttribute("valueAllowed") : null;
    }

    private static string? ValueForm(XmlNode objval)
    {
        if (objval is not XmlElement el)
        {
            return null;
        }
        if (el.HasAttribute("valueForm"))
        {
            return el.GetAttribute("valueForm");
        }
        return el.HasAttribute("valtype") ? el.GetAttribute("valtype") : null;
    }

    /// <summary>Check a node's value against a set of objectValue nodes (mirrors <c>check_node_values</c>).</summary>
    private static bool CheckNodeValues(XmlNode node, XmlNodeList values)
    {
        if (values.Count == 0)
        {
            return true;
        }

        bool ret = false;
        string value = node.NodeType == XmlNodeType.Attribute ? node.Value ?? string.Empty : node.InnerText;

        foreach (XmlNode v in values)
        {
            string allowed = GetValueAllowed(v) ?? string.Empty;
            string? form = ValueForm(v);

            if (form == "range")
            {
                ret = ret || Csdb.IsInSet(value, allowed);
            }
            else if (form == "pattern")
            {
                ret = ret || MatchPattern(value, allowed);
            }
            else
            {
                ret = ret || string.Equals(value, allowed, StringComparison.Ordinal);
            }
        }

        return ret;
    }

    /// <summary>Check one node's value against a rule (mirrors <c>check_single_object_values</c>).</summary>
    private static bool CheckSingleObjectValues(XmlElement rule, XmlNode node)
    {
        XmlNodeList? values = rule.SelectNodes("objectValue|objval");
        if (values == null || values.Count == 0)
        {
            return false;
        }
        return CheckNodeValues(node, values);
    }

    /// <summary>Check a set of nodes' values against a rule (mirrors <c>check_objects_values</c>).</summary>
    private static bool CheckObjectsValues(XmlElement rule, XmlNodeList? nodes)
    {
        if (nodes == null || nodes.Count == 0)
        {
            return true;
        }

        XmlNodeList? values = rule.SelectNodes("objectValue|objval");
        if (values == null || values.Count == 0)
        {
            return true;
        }

        foreach (XmlNode node in nodes)
        {
            if (!CheckNodeValues(node, values))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Match a value against an XML-Schema-style regular expression pattern.
    /// libxml2 uses <c>xmlRegexpExec</c> (a full-string match against an XSD
    /// regex); .NET's <see cref="Regex"/> is anchored with <c>^…$</c> to mirror
    /// that. Mirrors <c>match_pattern</c>.
    /// </summary>
    internal static bool MatchPattern(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, "^(?:" + pattern + ")$");
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ---- SNS rules --------------------------------------------------------------

    /// <summary>
    /// Check the SNS rules of a chain of BREX modules against a data module. The
    /// valid SNS is taken as the combination of the <c>snsRules</c> from every
    /// BREX in the chain (mirrors <c>check_brex_sns</c>), then descends
    /// systemCode → subSystem → subSubSystem → assy in normal/strict/unstrict
    /// modes (mirrors <c>check_brex_sns_rules</c>). Only data modules
    /// (<c>dmodule</c> root) are checked.
    /// </summary>
    private static bool CheckSns(IReadOnlyList<BrexModule> brexChain, XmlDocument doc, XmlElement documentNode, BrexCheckOptions opts)
    {
        XmlDocument report = documentNode.OwnerDocument!;

        XmlElement snsCheck = (XmlElement)documentNode.AppendChild(report.CreateElement("sns"))!;

        if (doc.DocumentElement?.Name != "dmodule")
        {
            snsCheck.AppendChild(report.CreateElement("noErrors"));
            return true;
        }

        // Merge the snsRules from every BREX in the chain into one group document.
        XmlDocument snsRulesDoc = XmlUtils.NewDocument();
        XmlElement snsRulesGroup = snsRulesDoc.CreateElement("snsRulesGroup");
        snsRulesDoc.AppendChild(snsRulesGroup);
        foreach (BrexModule layer in brexChain)
        {
            XmlNode? snsRules = XmlUtils.XPathFirstNode(layer.Document, null, "//snsRules");
            if (snsRules != null)
            {
                snsRulesGroup.AppendChild(snsRulesDoc.ImportNode(snsRules, true));
            }
        }

        XmlElement? dmcode = XmlUtils.XPathFirstNode(doc, null, "//dmIdent/dmCode") as XmlElement;
        if (!snsRulesGroup.HasChildNodes || dmcode == null)
        {
            snsCheck.AppendChild(report.CreateElement("noErrors"));
            return true;
        }

        string systemCode = dmcode.GetAttribute("systemCode");
        string subSystemCode = dmcode.GetAttribute("subSystemCode");
        string subSubSystemCode = dmcode.GetAttribute("subSubSystemCode");
        string assyCode = dmcode.GetAttribute("assyCode");

        XmlElement snsError = report.CreateElement("error");
        bool correct = true;
        XmlNode ctx = snsRulesGroup;

        bool strict = opts.HasFlag(BrexCheckOptions.StrictSns);
        bool unstrict = opts.HasFlag(BrexCheckOptions.UnstrictSns);

        // System code.
        if (ShouldCheck(systemCode, "snsSystem", ".//snsSystem", ctx, strict, unstrict))
        {
            XmlNode? next = ctx.SelectSingleNode($".//snsSystem[snsCode = {XPathLiteral(systemCode)}]");
            if (next == null)
            {
                AddSnsError(snsError, "systemCode", systemCode);
                correct = false;
            }
            else
            {
                ctx = next;
            }
        }

        // Subsystem code.
        if (correct && ShouldCheck(subSystemCode, "snsSubSystem", ".//snsSubSystem", ctx, strict, unstrict))
        {
            XmlNode? next = ctx.SelectSingleNode($".//snsSubSystem[snsCode = {XPathLiteral(subSystemCode)}]");
            if (next == null)
            {
                AddSnsError(snsError, "subSystemCode", $"{systemCode}-{subSystemCode}");
                correct = false;
            }
            else
            {
                ctx = next;
            }
        }

        // Subsubsystem code.
        if (correct && ShouldCheck(subSubSystemCode, "snsSubSubSystem", ".//snsSubSubSystem", ctx, strict, unstrict))
        {
            XmlNode? next = ctx.SelectSingleNode($".//snsSubSubSystem[snsCode = {XPathLiteral(subSubSystemCode)}]");
            if (next == null)
            {
                AddSnsError(snsError, "subSubSystemCode", $"{systemCode}-{subSystemCode}{subSubSystemCode}");
                correct = false;
            }
            else
            {
                ctx = next;
            }
        }

        // Assembly code.
        if (correct && ShouldCheck(assyCode, "snsAssy", ".//snsAssy", ctx, strict, unstrict))
        {
            XmlNode? next = ctx.SelectSingleNode($".//snsAssy[snsCode = {XPathLiteral(assyCode)}]");
            if (next == null)
            {
                AddSnsError(snsError, "assyCode", $"{systemCode}-{subSystemCode}{subSubSystemCode}-{assyCode}");
                correct = false;
            }
        }

        if (correct)
        {
            snsCheck.AppendChild(report.CreateElement("noErrors"));
        }
        else
        {
            snsCheck.AppendChild(snsError);
        }

        return correct;
    }

    private static void AddSnsError(XmlElement snsError, string code, string invalidValue)
    {
        XmlDocument report = snsError.OwnerDocument!;
        XmlElement c = (XmlElement)snsError.AppendChild(report.CreateElement("code"))!;
        c.InnerText = code;
        XmlElement v = (XmlElement)snsError.AppendChild(report.CreateElement("invalidValue"))!;
        v.InnerText = invalidValue;
    }

    /// <summary>Determine which parts of the SNS to check. Mirrors <c>should_check</c>.</summary>
    private static bool ShouldCheck(string code, string elemName, string relPath, XmlNode ctx, bool strict, bool unstrict)
    {
        if (strict)
        {
            return true;
        }
        if (unstrict)
        {
            return ctx.SelectSingleNode(relPath) != null;
        }

        bool ret;
        if (elemName is "snsSubSystem" or "snsSubSubSystem")
        {
            ret = code != "0";
        }
        else
        {
            ret = !(code == "00" || code == "0000");
        }

        return ret || ctx.SelectSingleNode(relPath) != null;
    }

    // ---- Notation rules ---------------------------------------------------------

    /// <summary>
    /// Notation-rule checking. Mirrors <c>check_brex_notations</c> /
    /// <c>check_brex_notation_rules</c> / <c>check_entity</c>: the
    /// <c>notationRuleList</c> of every BREX in the chain is combined, then each
    /// NOTATION referenced by the document's internal-DTD unparsed-entity
    /// (<c>NDATA</c>) declarations is validated against it. A notation is allowed
    /// only when a <c>notationRule</c> names it with an
    /// <c>allowedNotationFlag != "0"</c>; otherwise an <c>error</c> is recorded.
    ///
    /// <para>
    /// The C reads each entity's NOTATION name from libxml2's parsed internal
    /// subset (unparsed entity, <c>etype == 3</c>). The .NET DOM exposes the same
    /// information via <see cref="XmlDocumentType"/>: declared notations on
    /// <see cref="XmlDocumentType.Notations"/> and unparsed entities on
    /// <see cref="XmlDocumentType.Entities"/> (each <see cref="XmlEntity.NotationName"/>
    /// gives the NDATA notation). Because the BREX rules are keyed on the notation
    /// name actually *used*, we enumerate the NDATA notations referenced by the
    /// entity declarations (falling back to parsing the internal-subset text when
    /// the runtime does not populate the entity map), which is exactly the set the
    /// C iterates.
    /// </para>
    /// </summary>
    private static int CheckNotations(IReadOnlyList<BrexModule> brexChain, XmlDocument doc, XmlElement documentNode)
    {
        XmlDocument report = documentNode.OwnerDocument!;

        // No internal DTD subset -> nothing to check, and no <notations> element
        // is emitted (mirrors the early return when dmod_doc->intSubset is NULL).
        XmlDocumentType? dtd = doc.DocumentType;
        if (dtd == null)
        {
            return 0;
        }

        // Merge the notationRuleList from every BREX in the chain.
        XmlDocument ruleDoc = XmlUtils.NewDocument();
        XmlElement ruleGroup = ruleDoc.CreateElement("notationRuleGroup");
        ruleDoc.AppendChild(ruleGroup);
        foreach (BrexModule layer in brexChain)
        {
            XmlNode? list = XmlUtils.XPathFirstNode(layer.Document, null, "//notationRuleList");
            if (list != null)
            {
                ruleGroup.AppendChild(ruleDoc.ImportNode(list, true));
            }
        }

        XmlElement notations = (XmlElement)documentNode.AppendChild(report.CreateElement("notations"))!;

        int invalid = 0;
        foreach (string notation in UsedNotations(dtd))
        {
            invalid += CheckEntity(notation, ruleGroup, notations);
        }

        if (!notations.HasChildNodes)
        {
            notations.AppendChild(report.CreateElement("noErrors"));
        }

        return invalid;
    }

    /// <summary>
    /// Enumerate the NOTATION names referenced by unparsed (NDATA) entity
    /// declarations in a document's internal DTD subset, mirroring the C iteration
    /// over <c>XML_ENTITY_DECL</c> nodes with <c>etype == 3</c>.
    /// </summary>
    private static IEnumerable<string> UsedNotations(XmlDocumentType dtd)
    {
        var seen = new List<string>();

        // The parsed entity map: each unparsed entity carries the NDATA notation
        // name in XmlEntity.NotationName. This is the direct DOM analogue of the C
        // iteration over unparsed entity declarations.
        XmlNamedNodeMap? entities = dtd.Entities;
        if (entities != null)
        {
            foreach (XmlNode node in entities)
            {
                if (node is XmlEntity entity &&
                    entity.NotationName is { Length: > 0 } name &&
                    !seen.Contains(name))
                {
                    seen.Add(name);
                }
            }
        }

        // Also parse the internal-subset text for <!ENTITY ... NDATA name> decls.
        // Some runtimes/parse paths do not populate XmlEntity.NotationName on the
        // entity map even though the declarations are present in InternalSubset,
        // so this guarantees the used notations are enumerated either way.
        if (dtd.InternalSubset is { Length: > 0 } subset)
        {
            foreach (string name in ParseNdataNotations(subset))
            {
                if (!seen.Contains(name))
                {
                    seen.Add(name);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// Parse the NDATA notation names from the <c>&lt;!ENTITY ... NDATA name&gt;</c>
    /// declarations in a raw internal-subset string.
    /// </summary>
    private static IEnumerable<string> ParseNdataNotations(string subset)
    {
        foreach (Match m in Regex.Matches(subset,
            @"<!ENTITY\b[^>]*?\bNDATA\s+([\w.\-:]+)", RegexOptions.Singleline))
        {
            yield return m.Groups[1].Value;
        }
    }

    /// <summary>
    /// Check a single used NOTATION against the merged notation rules. Mirrors
    /// <c>check_entity</c>: a notation is allowed when a rule names it with
    /// <c>allowedNotationFlag != "0"</c>; otherwise an error is recorded with the
    /// <c>objectUse</c> of the matching (or first) rule.
    /// </summary>
    private static int CheckEntity(string notation, XmlElement ruleGroup, XmlElement notations)
    {
        XmlDocument report = notations.OwnerDocument!;
        string q = XPathLiteral(notation);

        XmlNode? allowedRule = ruleGroup.SelectSingleNode(
            $".//notationRule[notationName = {q} and notationName/@allowedNotationFlag != '0']");
        if (allowedRule != null)
        {
            return 0;
        }

        XmlNode? rule = ruleGroup.SelectSingleNode(
            $"(.//notationRule[notationName = {q}]|.//notationRule)[1]");

        XmlElement notationError = (XmlElement)notations.AppendChild(report.CreateElement("error"))!;
        XmlElement inv = (XmlElement)notationError.AppendChild(report.CreateElement("invalidNotation"))!;
        inv.InnerText = notation;

        XmlNode? use = rule?.SelectSingleNode("objectUse");
        if (use != null)
        {
            notationError.AppendChild(report.ImportNode(use, true));
        }

        return 1;
    }

    // ---- BREX resolution --------------------------------------------------------

    /// <summary>
    /// Return the default BREX DMC for the issue of the spec a document targets.
    /// Mirrors <c>default_brex_dmc</c>.
    /// </summary>
    public static string DefaultBrexDmc(XmlDocument doc)
    {
        string schema = SchemaOf(doc);

        if (string.IsNullOrEmpty(schema) || schema.Contains("S1000D_6"))
        {
            return "DMC-S1000D-H-04-10-0301-00A-022A-D";
        }
        if (schema.Contains("S1000D_5-0"))
        {
            return "DMC-S1000D-G-04-10-0301-00A-022A-D";
        }
        if (schema.Contains("S1000D_4-2"))
        {
            return "DMC-S1000D-F-04-10-0301-00A-022A-D";
        }
        if (schema.Contains("S1000D_4-1"))
        {
            return "DMC-S1000D-E-04-10-0301-00A-022A-D";
        }
        if (schema.Contains("S1000D_4-0"))
        {
            return "DMC-S1000D-D-04-10-0301-00A-022A-D";
        }
        return "DMC-AE-A-04-10-0301-00A-022A-D";
    }

    /// <summary>
    /// Load a default BREX data module (one of the standard codes) from the
    /// embedded resources. Returns null if the code is not a recognised default.
    /// Mirrors the in-memory <c>load_brex</c> path.
    /// </summary>
    public static XmlDocument? LoadDefaultBrex(string code)
    {
        string? resource = code switch
        {
            "DMC-S1000D-H-04-10-0301-00A-022A-D" => "brexcheck/DMC-S1000D-H-04-10-0301-00A-022A-D_001-00_EN-US.XML",
            "DMC-S1000D-G-04-10-0301-00A-022A-D" => "brexcheck/DMC-S1000D-G-04-10-0301-00A-022A-D_001-00_EN-US.XML",
            "DMC-S1000D-F-04-10-0301-00A-022A-D" => "brexcheck/DMC-S1000D-F-04-10-0301-00A-022A-D_001-00_EN-US.XML",
            "DMC-S1000D-E-04-10-0301-00A-022A-D" => "brexcheck/DMC-S1000D-E-04-10-0301-00A-022A-D_012-00_EN-US.XML",
            "DMC-S1000D-D-04-10-0301-00A-022A-D" => "brexcheck/DMC-S1000D-D-04-10-0301-00A-022A-D_006-00_EN-US.XML",
            "DMC-S1000D-A-04-10-0301-00A-022A-D" => "brexcheck/DMC-S1000D-A-04-10-0301-00A-022A-D_005-00_EN-US.XML",
            "DMC-AE-A-04-10-0301-00A-022A-D" => "brexcheck/DMC-AE-A-04-10-0301-00A-022A-D_003-00.XML",
            _ => null,
        };
        return resource == null ? null : EmbeddedResources.LoadXml(resource);
    }

    // ---- Helpers ----------------------------------------------------------------

    /// <summary>Return the <c>xsi:noNamespaceSchemaLocation</c> of a document, or "".</summary>
    internal static string SchemaOf(XmlDocument doc)
    {
        XmlElement? root = doc.DocumentElement;
        if (root == null)
        {
            return string.Empty;
        }
        return root.GetAttribute("noNamespaceSchemaLocation", XsiUri);
    }

    /// <summary>Add the run configuration as attributes on the report root (mirrors <c>add_config_to_report</c>).</summary>
    private static void AddConfigToReport(XmlElement brexCheck, BrexCheckOptions opts, bool layered = false)
    {
        brexCheck.SetAttribute("layered", layered ? "yes" : "no");
        brexCheck.SetAttribute("checkObjectValues", opts.HasFlag(BrexCheckOptions.Values) ? "yes" : "no");

        if (opts.HasFlag(BrexCheckOptions.Sns))
        {
            string mode = opts.HasFlag(BrexCheckOptions.StrictSns) ? "strict"
                : opts.HasFlag(BrexCheckOptions.UnstrictSns) ? "unstrict"
                : "normal";
            brexCheck.SetAttribute("snsCheck", mode);
        }
        else
        {
            brexCheck.SetAttribute("snsCheck", "no");
        }

        brexCheck.SetAttribute("notationCheck", opts.HasFlag(BrexCheckOptions.Notations) ? "yes" : "no");
    }

    /// <summary>Quote a string as an XPath 1.0 literal, handling embedded quotes via concat().</summary>
    internal static string XPathLiteral(string s)
    {
        if (!s.Contains('\''))
        {
            return "'" + s + "'";
        }
        if (!s.Contains('"'))
        {
            return "\"" + s + "\"";
        }
        // Both quote kinds present: build concat('a', "'", 'b', …).
        var parts = s.Split('\'');
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
}
