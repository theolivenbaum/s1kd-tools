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
/// Programmatic API for checking CSDB objects against BREX (Business Rules
/// EXchange) data modules. This mirrors the libs1kd functions
/// <c>s1kdDocCheckBREX</c> / <c>s1kdDocCheckDefaultBREX</c> and the core of the
/// <c>s1kd-brexcheck</c> command-line tool.
///
/// <para>
/// What is implemented thoroughly: the structure object rules (the
/// <c>structureObjectRule</c> / <c>objrule</c> elements), namely evaluating each
/// rule's <c>objectPath</c> against the object, honouring the
/// <c>allowedObjectFlag</c> (0 = must NOT exist, 1 = must exist, anything else =
/// only value-checked), and — when <see cref="BrexCheckOptions.Values"/> is set —
/// checking the matched nodes' string values against the allowed
/// <c>objectValue</c>/<c>objval</c> set (single value, <c>pattern</c> regex, or
/// <c>range</c> set). It also builds the XML report (<c>brexCheck</c> →
/// <c>document</c> → <c>brex</c> → <c>error</c>) compatibly with the C tool.
/// </para>
///
/// <para>
/// Partial: SNS checks (<see cref="BrexCheckOptions.Sns"/>) are implemented for
/// data modules. Notation checks (<see cref="BrexCheckOptions.Notations"/>) are a
/// best-effort port — System.Xml does not expose internal-DTD NOTATION-typed
/// unparsed-entity declarations the way libxml2 does, so the implementation
/// records that notation checking was requested but cannot enumerate entities.
/// See the notes in <c>todo.md</c>.
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
        report = XmlUtils.NewDocument();
        XmlElement brexCheck = report.CreateElement("brexCheck");
        report.AppendChild(brexCheck);
        AddConfigToReport(brexCheck, opts);

        XmlElement documentNode = (XmlElement)brexCheck.AppendChild(report.CreateElement("document"))!;
        documentNode.SetAttribute("path", docPath);

        int total = 0;

        if (opts.HasFlag(BrexCheckOptions.Sns))
        {
            if (!CheckSns(brex, doc, documentNode, opts))
            {
                ++total;
            }
        }

        if (opts.HasFlag(BrexCheckOptions.Notations))
        {
            total += CheckNotations(brex, doc, documentNode);
        }

        total += CheckStructureRules(brex, doc, brexPath, documentNode, opts);

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

    // ---- Structure object rules -------------------------------------------------

    private static int CheckStructureRules(XmlDocument brex, XmlDocument doc, string brexPath,
        XmlElement documentNode, BrexCheckOptions opts)
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

            ++nerr;
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

    // ---- SNS rules (partial) ----------------------------------------------------

    /// <summary>
    /// Check the SNS rules of a BREX module against a data module. Mirrors
    /// <c>check_brex_sns_rules</c> for the normal/strict/unstrict modes. Only data
    /// modules (<c>dmodule</c> root) are checked.
    /// </summary>
    private static bool CheckSns(XmlDocument brex, XmlDocument doc, XmlElement documentNode, BrexCheckOptions opts)
    {
        XmlDocument report = documentNode.OwnerDocument!;

        XmlElement snsCheck = (XmlElement)documentNode.AppendChild(report.CreateElement("sns"))!;

        if (doc.DocumentElement?.Name != "dmodule")
        {
            snsCheck.AppendChild(report.CreateElement("noErrors"));
            return true;
        }

        XmlNode? snsRules = XmlUtils.XPathFirstNode(brex, null, "//snsRules");
        XmlElement? dmcode = XmlUtils.XPathFirstNode(doc, null, "//dmIdent/dmCode") as XmlElement;
        if (snsRules == null || dmcode == null)
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
        XmlNode ctx = snsRules;

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

    // ---- Notation rules (partial) ----------------------------------------------

    /// <summary>
    /// Notation-rule checking. The C tool walks the data module's internal DTD
    /// subset and validates each NOTATION-typed unparsed-entity declaration
    /// against the BREX <c>notationRule</c> list. <see cref="XmlDocument"/> does
    /// not surface internal-subset entity declarations, so this records an empty
    /// (no-error) notation check and returns 0. Tracked in todo.md.
    /// </summary>
    private static int CheckNotations(XmlDocument brex, XmlDocument doc, XmlElement documentNode)
    {
        XmlDocument report = documentNode.OwnerDocument!;
        XmlElement notations = (XmlElement)documentNode.AppendChild(report.CreateElement("notations"))!;
        notations.AppendChild(report.CreateElement("noErrors"));
        return 0;
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
    private static void AddConfigToReport(XmlElement brexCheck, BrexCheckOptions opts)
    {
        brexCheck.SetAttribute("layered", "no");
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
