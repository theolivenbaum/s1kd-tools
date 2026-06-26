using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-aspp</c> (applicability statement preprocessor).
///
/// The tool has two main functions:
/// <list type="bullet">
///   <item>Generates display text (the "human-readable" form) for applicability
///   annotations from the logic in their <c>assert</c>/<c>evaluate</c> elements
///   (<c>-g</c>).</item>
///   <item>Preprocesses "semantic" applicability into "presentation"
///   applicability that is simpler for a stylesheet to render (<c>-p</c>): every
///   element keeps an explicit <c>applicRefId</c> only where applicability
///   changes, plus an inline <c>applic</c> for the whole DM.</item>
/// </list>
///
/// The C tool implements display-text generation with a two-stage EXSLT
/// meta-stylesheet (<c>disptext.xsl</c> turns a <c>.disptext</c> config into a
/// second stylesheet that uses <c>str:replace</c> / <c>node-set</c>).
/// <see cref="System.Xml.Xsl.XslCompiledTransform"/> does not support those
/// EXSLT functions, so display-text generation here is implemented directly in
/// the DOM, driven by the same <c>.disptext</c> configuration. The presentation
/// processing, delete, and tag-insertion paths are pure DOM and mirror the C
/// directly. See the report / todo for constructs not fully ported (custom
/// <c>-x</c> XSLT, <c>--dump-xsl</c>).
/// </summary>
public sealed class AsppTool : ITool
{
    public string Name => "aspp";
    public string Description => "Applicability statement preprocessor.";
    public string Version => "5.1.0";

    /// <summary>Default ID for the inline whole-DM applicability element.</summary>
    private const string DefaultDmApplicId = "app-0000";

    // ---- options (instance state for a single Run) ----
    private string _dmApplicId = DefaultDmApplicId;
    private bool _overwriteDispText = true;
    private bool _noIssue;
    private bool _recursive;
    private string _searchDir = ".";
    private int _verbosity = 1; // 0 = quiet, 1 = normal, 2 = verbose

    /// <summary>The XPath selecting all elements which may carry applicability annotations.</summary>
    private static readonly string ApplicElemsXPath = BuildApplicElemsXPath();

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool overwrite = false;
        bool genDispText = false;
        bool process = false;
        bool delDispText = false;
        bool findcts = false;
        bool islist = false;
        string? format = null;
        string? tags = null;
        string? customGenDispTextFile = null;
        string? disptextFile = null;
        var acts = new List<string>();
        var ccts = new List<string>();
        var files = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return 0;
                case "--version":
                    stdout.WriteLine($"{Name} (s1kd-tools) {Version}");
                    return 0;
                case "-." or "--dump-disptext":
                    stdout.Write(EmbeddedResources.ReadText("aspp/disptext.xml"));
                    return 0;
                case "-," or "--dump-xsl":
                    stderr.WriteLine($"{Name}: ERROR: --dump-xsl is not supported in this port.");
                    return 0;
                case "-A" or "--act":
                    if (++i >= args.Count) return ArgErr(stderr, a);
                    acts.Add(args[i]);
                    findcts = false;
                    break;
                case "-a" or "--id":
                    if (++i >= args.Count) return ArgErr(stderr, a);
                    _dmApplicId = args[i];
                    break;
                case "-C" or "--cct":
                    if (++i >= args.Count) return ArgErr(stderr, a);
                    ccts.Add(args[i]);
                    findcts = false;
                    break;
                case "-c" or "--search":
                    findcts = true;
                    break;
                case "-D" or "--delete":
                    delDispText = true;
                    break;
                case "-d" or "--dir":
                    if (++i >= args.Count) return ArgErr(stderr, a);
                    _searchDir = args[i];
                    break;
                case "-F" or "--format":
                    if (++i >= args.Count) return ArgErr(stderr, a);
                    genDispText = true;
                    format = args[i];
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-G" or "--disptext":
                    if (++i >= args.Count) return ArgErr(stderr, a);
                    genDispText = true;
                    disptextFile = args[i];
                    break;
                case "-g" or "--generate":
                    genDispText = true;
                    break;
                case "-k" or "--keep":
                    _overwriteDispText = false;
                    break;
                case "-l" or "--list":
                    islist = true;
                    break;
                case "-N" or "--omit-issue":
                    _noIssue = true;
                    break;
                case "-p" or "--presentation":
                    process = true;
                    break;
                case "-q" or "--quiet":
                    --_verbosity;
                    break;
                case "-r" or "--recursive":
                    _recursive = true;
                    break;
                case "-t" or "--tags":
                    if (++i >= args.Count) return ArgErr(stderr, a);
                    tags = args[i];
                    break;
                case "-v" or "--verbose":
                    ++_verbosity;
                    break;
                case "-x" or "--xsl":
                    if (++i >= args.Count) return ArgErr(stderr, a);
                    genDispText = true;
                    customGenDispTextFile = args[i];
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return 2;
                    }
                    files.Add(a);
                    break;
            }
        }

        if (customGenDispTextFile != null)
        {
            stderr.WriteLine($"{Name}: ERROR: Custom display-text XSLT (-x) is not supported in this port.");
            return 2;
        }

        // Load the .disptext configuration: explicit -G, else a discovered
        // .disptext in the directory tree, else the built-in default.
        XmlDocument disptext;
        if (disptextFile != null)
        {
            disptext = XmlUtils.ReadDoc(disptextFile);
        }
        else if (Csdb.FindConfig(".disptext", out string cfgPath))
        {
            disptext = XmlUtils.ReadDoc(cfgPath);
        }
        else
        {
            disptext = EmbeddedResources.LoadXml("aspp/disptext.xml");
        }

        var dispCfg = new DispTextConfig(disptext, format);

        Action<string, string> handle = (input, output) =>
            ProcessFile(input, output, process, genDispText, delDispText, acts, ccts, findcts, dispCfg, tags, stdout, stderr);

        if (files.Count == 0)
        {
            if (islist)
            {
                ProcessList(null, overwrite, handle, stderr);
            }
            else
            {
                handle("-", "-");
            }
        }
        else
        {
            foreach (string file in files)
            {
                if (islist)
                {
                    ProcessList(file, overwrite, handle, stderr);
                }
                else
                {
                    handle(file, overwrite ? file : "-");
                }
            }
        }

        return 0;
    }

    private int ArgErr(TextWriter stderr, string opt)
    {
        stderr.WriteLine($"{Name}: ERROR: {opt} requires an argument");
        return 2;
    }

    private void ProcessList(string? path, bool overwrite, Action<string, string> handle, TextWriter stderr)
    {
        IEnumerable<string> lines;
        if (path == null)
        {
            using var reader = new StreamReader(Console.OpenStandardInput());
            lines = reader.ReadToEnd().Split('\n');
        }
        else if (File.Exists(path))
        {
            lines = File.ReadAllLines(path);
        }
        else
        {
            if (_verbosity >= 1)
            {
                stderr.WriteLine($"{Name}: ERROR: Could not read list: {path}");
            }
            return;
        }

        foreach (string raw in lines)
        {
            string line = raw.Trim('\t', '\r', '\n', ' ');
            if (line.Length == 0)
            {
                continue;
            }
            handle(line, overwrite ? line : "-");
        }
    }

    private void ProcessFile(string input, string output, bool process, bool genDispText, bool delDispText,
        List<string> acts, List<string> ccts, bool findcts, DispTextConfig dispCfg, string? tags,
        TextWriter stdout, TextWriter stderr)
    {
        if (_verbosity >= 2)
        {
            stderr.WriteLine($"{Name}: INFO: Processing {input}...");
        }

        XmlDocument doc;
        try
        {
            doc = input == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(input);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            if (_verbosity >= 1)
            {
                stderr.WriteLine($"{Name}: ERROR: Could not read {input}: {ex.Message}");
            }
            return;
        }

        // Build the per-document ACT/CCT list (user-supplied plus, with -c, the
        // ones referenced by this DM).
        var allActs = new List<string>(acts);
        var allCcts = new List<string>(ccts);
        if (findcts)
        {
            FindCrossRefTables(doc, allActs, allCcts, stderr);
        }

        if (process)
        {
            XmlNodeList? dmodules = doc.SelectNodes("//dmodule");
            if (dmodules != null)
            {
                foreach (XmlNode dmodule in dmodules)
                {
                    ProcessDmodule(dmodule);
                }
            }
        }

        if (delDispText)
        {
            DeleteDisplayText(doc);
        }
        else if (genDispText)
        {
            GenerateDisplayText(doc, allActs, allCcts, dispCfg, stderr);
        }

        if (tags != null)
        {
            AddTags(doc, tags);
        }

        if (output == "-")
        {
            stdout.Write(XmlUtils.ToXmlString(doc));
            stdout.Write('\n');
        }
        else
        {
            XmlUtils.SaveDoc(doc, output);
        }
    }

    // ----------------------------------------------------------------------
    // Presentation applicability processing (-p)
    // ----------------------------------------------------------------------

    private void ProcessDmodule(XmlNode dmodule)
    {
        var nodes = SelectApplicElems(dmodule);
        ProcessNodeSet(nodes);
        RemoveDuplicates(nodes);
        AddDmApplic(dmodule);
    }

    private static List<XmlNode> SelectApplicElems(XmlNode dmodule)
    {
        var list = new List<XmlNode>();
        var seen = new HashSet<XmlNode>();
        var sel = dmodule.SelectNodes(ApplicElemsXPath);
        if (sel != null)
        {
            foreach (XmlNode n in sel)
            {
                if (seen.Add(n))
                {
                    list.Add(n);
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Make an element's applicability explicit. Mirrors <c>processNode</c>:
    /// elements without <c>@applicRefId</c>/<c>@refapplic</c> inherit it from the
    /// nearest annotated ancestor, or from the whole-DM applic ID.
    /// </summary>
    private void ProcessNode(XmlNode node)
    {
        XmlNode? attr = node.SelectSingleNode("@applicRefId|@refapplic");
        if (attr != null)
        {
            return;
        }

        XmlNodeList? ancestors = node.SelectNodes("ancestor::*[@applicRefId]|ancestor::*[@refapplic]");
        XmlNode? ancestor = (ancestors != null && ancestors.Count > 0) ? ancestors[ancestors.Count - 1] : null;

        // 3.0- documents use refapplic; 4.0+ use applicRefId. The presence of an
        // idstatus element distinguishes the legacy schema.
        string name = node.SelectSingleNode("//idstatus") != null ? "refapplic" : "applicRefId";

        if (node is not XmlElement el)
        {
            return;
        }

        if (ancestor is XmlElement ancestorEl)
        {
            string ancestorApplic = ancestorEl.GetAttribute("applicRefId");
            if (string.IsNullOrEmpty(ancestorApplic))
            {
                ancestorApplic = ancestorEl.GetAttribute("refapplic");
            }
            el.SetAttribute(name, ancestorApplic);
        }
        else
        {
            el.SetAttribute(name, _dmApplicId);
        }
    }

    private void ProcessNodeSet(List<XmlNode> nodes)
    {
        foreach (XmlNode n in nodes)
        {
            ProcessNode(n);
        }
    }

    /// <summary>
    /// Remove redundant annotations so an annotation only remains where the
    /// applicability changes in document order. Mirrors <c>removeDuplicates</c>.
    /// </summary>
    private void RemoveDuplicates(List<XmlNode> nodes)
    {
        string applic = _dmApplicId;

        foreach (XmlNode node in nodes)
        {
            if (node is not XmlElement el)
            {
                continue;
            }

            XmlAttribute? attr = el.GetAttributeNode("applicRefId") ?? el.GetAttributeNode("refapplic");
            if (attr == null)
            {
                continue;
            }

            string applicRefId = attr.Value;
            if (applicRefId == applic)
            {
                el.RemoveAttributeNode(attr);
            }
            else
            {
                applic = applicRefId;
            }
        }
    }

    /// <summary>
    /// Insert an inline <c>applic</c> for the whole DM's applicability into the
    /// referenced applic group. Mirrors <c>addDmApplic</c>.
    /// </summary>
    private void AddDmApplic(XmlNode dmodule)
    {
        XmlNode? group = dmodule.SelectSingleNode(".//referencedApplicGroup|.//inlineapplics");
        if (group == null)
        {
            return;
        }

        XmlNode? wholeDmApplic = dmodule.SelectSingleNode(".//dmStatus/applic|.//status/applic");
        if (wholeDmApplic == null)
        {
            return;
        }

        XmlDocument owner = group.OwnerDocument!;
        XmlNode applic = owner.ImportNode(wholeDmApplic, true);
        group.AppendChild(applic);

        if (applic is XmlElement applicEl)
        {
            applicEl.SetAttribute("id", _dmApplicId);
        }
    }

    // ----------------------------------------------------------------------
    // Delete display text (-D)
    // ----------------------------------------------------------------------

    private static void DeleteDisplayText(XmlDocument doc)
    {
        if (doc.DocumentElement != null)
        {
            DeleteDisplayTextNode(doc.DocumentElement);
        }
    }

    private static void DeleteDisplayTextNode(XmlNode node)
    {
        // displayText (4.0+) / displaytext (3.0-). Only remove if the parent has
        // more than one child element (i.e. there is a computer-processing part);
        // a display-text-only annotation is left intact.
        if ((node.LocalName == "displayText" || node.LocalName == "displaytext")
            && ChildElementCount(node.ParentNode) > 1)
        {
            node.ParentNode?.RemoveChild(node);
            return;
        }

        XmlNode? cur = node.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            DeleteDisplayTextNode(cur);
            cur = next;
        }
    }

    private static int ChildElementCount(XmlNode? node)
    {
        if (node == null)
        {
            return 0;
        }
        int n = 0;
        for (XmlNode? c = node.FirstChild; c != null; c = c.NextSibling)
        {
            if (c.NodeType == XmlNodeType.Element)
            {
                n++;
            }
        }
        return n;
    }

    // ----------------------------------------------------------------------
    // Tag insertion (-t)
    // ----------------------------------------------------------------------

    /// <summary>
    /// Insert (or remove) display-text tags before elements that reference an
    /// applicability annotation. Mirrors <c>addTags</c> / <c>addTags.xsl</c>.
    /// </summary>
    private static void AddTags(XmlDocument doc, string mode)
    {
        // Always remove existing s1kd-aspp processing instructions first.
        RemovePis(doc);

        if (mode == "remove")
        {
            return;
        }

        if (mode != "pi" && mode != "comment")
        {
            return;
        }

        if (doc.DocumentElement != null)
        {
            InsertTags(doc.DocumentElement, mode);
        }
    }

    private static void RemovePis(XmlNode node)
    {
        XmlNode? cur = node.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            if (cur.NodeType == XmlNodeType.ProcessingInstruction && cur.Name == "s1kd-aspp")
            {
                node.RemoveChild(cur);
            }
            else
            {
                RemovePis(cur);
            }
            cur = next;
        }
    }

    private static void InsertTags(XmlNode node, string mode)
    {
        // Snapshot the children first since we insert siblings while iterating.
        var children = new List<XmlNode>();
        for (XmlNode? c = node.FirstChild; c != null; c = c.NextSibling)
        {
            children.Add(c);
        }

        foreach (XmlNode child in children)
        {
            if (child is XmlElement el && el.HasAttribute("applicRefId"))
            {
                string refId = el.GetAttribute("applicRefId");
                XmlNode? applic = node.OwnerDocument?.SelectSingleNode($"//applic[@id='{refId}']")
                                  ?? node.SelectSingleNode($"//applic[@id='{refId}']");
                string text = applic == null ? string.Empty : DisplayParaText(applic);
                string content = "Applicable to: " + text;

                XmlDocument owner = node.OwnerDocument!;
                XmlNode tag = mode == "pi"
                    ? owner.CreateProcessingInstruction("s1kd-aspp", content)
                    : owner.CreateComment(content);
                node.InsertBefore(tag, child);
            }

            InsertTags(child, mode);
        }
    }

    private static string DisplayParaText(XmlNode applic)
    {
        XmlNode? para = applic.SelectSingleNode("displayText/simplePara|displaytext/p");
        return para?.InnerText ?? string.Empty;
    }

    // ----------------------------------------------------------------------
    // Display text generation (-g / -F / -A / -C / -G)
    // ----------------------------------------------------------------------

    /// <summary>
    /// Generate display text for every applicability annotation containing a
    /// computer-processing part. Mirrors the effect of the generated
    /// <c>disptext.xsl</c> stylesheet, but implemented in DOM.
    /// </summary>
    private void GenerateDisplayText(XmlDocument doc, List<string> acts, List<string> ccts, DispTextConfig cfg, TextWriter stderr)
    {
        // Load ACT/CCT docs once for property name/value lookups.
        var actDocs = LoadDocs(acts, stderr);
        var cctDocs = LoadDocs(ccts, stderr);

        var applics = new List<XmlElement>();
        CollectApplics(doc.DocumentElement, applics);

        foreach (XmlElement applic in applics)
        {
            ProcessApplic(applic, actDocs, cctDocs, cfg);
        }
    }

    private List<XmlDocument> LoadDocs(List<string> paths, TextWriter stderr)
    {
        var docs = new List<XmlDocument>();
        foreach (string p in paths)
        {
            try
            {
                docs.Add(XmlUtils.ReadDoc(p));
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                if (_verbosity >= 1)
                {
                    stderr.WriteLine($"{Name}: WARNING: Could not read referenced object: {p}");
                }
            }
        }
        return docs;
    }

    private static void CollectApplics(XmlNode? node, List<XmlElement> applics)
    {
        if (node == null)
        {
            return;
        }
        for (XmlNode? c = node.FirstChild; c != null; c = c.NextSibling)
        {
            if (c is XmlElement el && el.LocalName == "applic" &&
                el.SelectSingleNode("assert|evaluate|expression") != null)
            {
                applics.Add(el);
            }
            CollectApplics(c, applics);
        }
    }

    /// <summary>
    /// Add (or replace) the display text of a single <c>applic</c> element.
    /// Mirrors the <c>applic[assert|evaluate|expression]</c> template.
    /// </summary>
    private void ProcessApplic(XmlElement applic, List<XmlDocument> acts, List<XmlDocument> ccts, DispTextConfig cfg)
    {
        XmlNode? parent = applic.ParentNode;
        bool legacy = parent != null && (parent.LocalName == "status" || parent.LocalName == "inlineapplics");
        string dispName = legacy ? "displaytext" : "displayText";
        string paraName = legacy ? "p" : "simplePara";

        XmlNode? existing = applic.SelectSingleNode("displayText|displaytext");

        if (existing != null && !_overwriteDispText)
        {
            // Keep existing display text; nothing to do.
            return;
        }

        if (existing != null)
        {
            applic.RemoveChild(existing);
        }

        XmlDocument owner = applic.OwnerDocument!;
        XmlElement disp = owner.CreateElement(dispName);
        XmlElement para = owner.CreateElement(paraName);
        disp.AppendChild(para);

        string text = StatementText(applic, acts, ccts, cfg);
        para.AppendChild(owner.CreateTextNode(text));

        // The display text precedes the computer-processing part.
        XmlNode? cp = applic.SelectSingleNode("assert|evaluate|expression");
        applic.InsertBefore(disp, cp);
    }

    /// <summary>Build the display text for the logic part of an applic element.</summary>
    private string StatementText(XmlNode applic, List<XmlDocument> acts, List<XmlDocument> ccts, DispTextConfig cfg)
    {
        var sb = new StringBuilder();
        foreach (XmlNode child in applic.ChildNodes)
        {
            if (child.LocalName is "assert" or "evaluate")
            {
                EmitNode(child, acts, ccts, cfg, sb);
            }
            else if (child.LocalName == "expression")
            {
                // Pass expression text through verbatim (rare; mirrors copy).
                sb.Append(child.InnerText);
            }
        }
        return sb.ToString();
    }

    private void EmitNode(XmlNode node, List<XmlDocument> acts, List<XmlDocument> ccts, DispTextConfig cfg, StringBuilder sb)
    {
        if (node.LocalName == "assert")
        {
            EmitAssert(node, acts, ccts, cfg, sb);
        }
        else if (node.LocalName == "evaluate")
        {
            EmitEvaluate(node, acts, ccts, cfg, sb);
        }
    }

    /// <summary>Mirrors the <c>evaluate</c> mode="text" template.</summary>
    private void EmitEvaluate(XmlNode evaluate, List<XmlDocument> acts, List<XmlDocument> ccts, DispTextConfig cfg, StringBuilder sb)
    {
        string op = FirstAttr(evaluate, "andOr", "operator") ?? string.Empty;

        var children = new List<XmlNode>();
        foreach (XmlNode c in evaluate.ChildNodes)
        {
            if (c.LocalName is "assert" or "evaluate")
            {
                children.Add(c);
            }
        }

        for (int i = 0; i < children.Count; i++)
        {
            XmlNode child = children[i];
            bool nestedGroup = child.LocalName == "evaluate" &&
                               (FirstAttr(child, "andOr", "operator") ?? string.Empty) != op;

            if (nestedGroup)
            {
                sb.Append(cfg.OpenGroup);
            }

            EmitNode(child, acts, ccts, cfg, sb);

            if (nestedGroup)
            {
                sb.Append(cfg.CloseGroup);
            }

            if (i != children.Count - 1)
            {
                if (op == "and")
                {
                    sb.Append(cfg.And);
                }
                else if (op == "or")
                {
                    sb.Append(cfg.Or);
                }
            }
        }
    }

    /// <summary>
    /// Mirrors the <c>assert</c> mode="text" template generated from the
    /// <c>.disptext</c> rules: pick the matching property rule and emit its
    /// name/text/values sequence.
    /// </summary>
    private void EmitAssert(XmlNode assert, List<XmlDocument> acts, List<XmlDocument> ccts, DispTextConfig cfg, StringBuilder sb)
    {
        string? ident = FirstAttr(assert, "applicPropertyIdent", "actidref");
        string? type = FirstAttr(assert, "applicPropertyType", "actreftype");
        string? values = FirstAttr(assert, "applicPropertyValues", "actvalues");

        DispTextConfig.PropertyRule rule = cfg.SelectRule(ident, type, assert);

        foreach (DispTextConfig.FormatPart part in rule.Parts)
        {
            switch (part.Kind)
            {
                case DispTextConfig.PartKind.Name:
                    sb.Append(PropertyName(ident, type, acts, ccts));
                    break;
                case DispTextConfig.PartKind.Text:
                    sb.Append(part.Text);
                    break;
                case DispTextConfig.PartKind.Values:
                    sb.Append(PropertyValues(ident, type, values, acts, ccts, part.ValueLabels, cfg));
                    break;
            }
        }
    }

    /// <summary>
    /// Resolve the display name for a property. Mirrors the
    /// <c>applicPropertyName</c> template: prefer displayName, then name, then
    /// the raw ident.
    /// </summary>
    private static string PropertyName(string? ident, string? type, List<XmlDocument> acts, List<XmlDocument> ccts)
    {
        if (ident == null)
        {
            return string.Empty;
        }

        XmlNode? prop = FindProperty(ident, type, acts, ccts);
        if (prop != null)
        {
            string? disp = prop.SelectSingleNode("displayName|displayname")?.InnerText;
            if (!string.IsNullOrEmpty(disp))
            {
                return disp;
            }
            string? name = prop.SelectSingleNode("name")?.InnerText;
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return ident;
    }

    /// <summary>
    /// Resolve the displayed values for a property. Mirrors the
    /// <c>applicPropertyVal</c> template, including enumeration labels and the
    /// set/range operator substitution.
    /// </summary>
    private static string PropertyValues(string? ident, string? type, string? values,
        List<XmlDocument> acts, List<XmlDocument> ccts,
        IReadOnlyDictionary<string, string>? customLabels, DispTextConfig cfg)
    {
        if (values == null)
        {
            return string.Empty;
        }

        // Custom labels supplied directly in the .disptext <values> rule.
        if (customLabels != null && customLabels.TryGetValue(values, out string? custom))
        {
            return custom;
        }

        // Enumeration label from the ACT/CCT.
        XmlNode? valProp = FindValueProperty(ident, type, acts, ccts);
        if (valProp != null)
        {
            XmlNode? enumEl = valProp.SelectSingleNode($"enumeration[@applicPropertyValues='{values}']");
            string? label = (enumEl as XmlElement)?.GetAttribute("enumerationLabel");
            if (!string.IsNullOrEmpty(label))
            {
                return label;
            }
        }

        // Default: substitute set/range operators.
        return values.Replace("|", cfg.SetOp).Replace("~", cfg.RangeOp);
    }

    private static XmlNode? FindProperty(string? ident, string? type, List<XmlDocument> acts, List<XmlDocument> ccts)
    {
        if (ident == null)
        {
            return null;
        }

        foreach (XmlDocument act in acts)
        {
            if (type == "prodattr")
            {
                XmlNode? n = act.SelectSingleNode($"//productAttribute[@id='{ident}']|//prodattr[@id='{ident}']");
                if (n != null) return n;
            }
        }
        foreach (XmlDocument cct in ccts)
        {
            if (type == "condition")
            {
                XmlNode? n = cct.SelectSingleNode($"//cond[@id='{ident}']|//condition[@id='{ident}']");
                if (n != null) return n;
            }
        }
        return null;
    }

    /// <summary>
    /// Locate the element that carries the enumeration list for a property's
    /// values. For conditions this is the condition type referenced by the cond,
    /// mirroring the more elaborate <c>applicPropertyVal</c> XPath.
    /// </summary>
    private static XmlNode? FindValueProperty(string? ident, string? type, List<XmlDocument> acts, List<XmlDocument> ccts)
    {
        if (ident == null)
        {
            return null;
        }

        if (type == "prodattr")
        {
            foreach (XmlDocument act in acts)
            {
                XmlNode? n = act.SelectSingleNode($"//productAttribute[@id='{ident}']|//prodattr[@id='{ident}']");
                if (n != null) return n;
            }
        }
        else if (type == "condition")
        {
            foreach (XmlDocument cct in ccts)
            {
                // 4.0+: cond -> condType via condTypeRefId; 3.0-: condition via condtyperef.
                XmlElement? cond = cct.SelectSingleNode($"//cond[@id='{ident}']") as XmlElement;
                if (cond != null)
                {
                    string typeRef = cond.GetAttribute("condTypeRefId");
                    if (!string.IsNullOrEmpty(typeRef))
                    {
                        XmlNode? ct = cct.SelectSingleNode($"//condType[@id='{typeRef}']");
                        if (ct != null) return ct;
                    }
                }
                XmlElement? condition = cct.SelectSingleNode($"//condition[@id='{ident}']") as XmlElement;
                if (condition != null)
                {
                    string typeRef = condition.GetAttribute("condtyperef");
                    if (!string.IsNullOrEmpty(typeRef))
                    {
                        XmlNode? ct = cct.SelectSingleNode($"//condition[@id='{typeRef}']");
                        if (ct != null) return ct;
                    }
                }
            }
        }
        return null;
    }

    private static string? FirstAttr(XmlNode node, string a, string b)
    {
        if (node.Attributes == null)
        {
            return null;
        }
        return node.Attributes[a]?.Value ?? node.Attributes[b]?.Value;
    }

    // ----------------------------------------------------------------------
    // ACT/CCT discovery (-c)
    // ----------------------------------------------------------------------

    /// <summary>
    /// Find the ACT referenced by the DM, and via the ACT the CCT, and add their
    /// filenames to the lists. Mirrors <c>find_cross_ref_tables</c>.
    /// </summary>
    private void FindCrossRefTables(XmlDocument doc, List<string> acts, List<string> ccts, TextWriter stderr)
    {
        XmlNode? actRef = doc.SelectSingleNode("//applicCrossRefTableRef/dmRef/dmRefIdent|//actref/refdm");
        if (actRef == null)
        {
            return;
        }

        if (!FindDmodFname(actRef, out string actFname, stderr))
        {
            return;
        }

        XmlDocument act;
        try
        {
            act = XmlUtils.ReadDoc(actFname);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return;
        }

        acts.Add(actFname);

        XmlNode? cctRef = act.SelectSingleNode("//condCrossRefTableRef/dmRef/dmRefIdent|//cctref/refdm");
        if (cctRef != null && FindDmodFname(cctRef, out string cctFname, stderr))
        {
            ccts.Add(cctFname);
        }
    }

    /// <summary>
    /// Reconstruct a DM code from a dmRefIdent element and locate the matching
    /// file under the search directory. Mirrors <c>find_dmod_fname</c>.
    /// </summary>
    private bool FindDmodFname(XmlNode dmRefIdent, out string result, TextWriter stderr)
    {
        result = string.Empty;

        XmlNode? dmCode = dmRefIdent.SelectSingleNode("dmCode|avee");
        XmlNode? issueInfo = dmRefIdent.SelectSingleNode("issueInfo|issno");
        XmlNode? language = dmRefIdent.SelectSingleNode("language");
        if (dmCode == null)
        {
            return false;
        }

        string V(string xpath) => XmlUtils.XPathFirstValue(null, dmCode, xpath) ?? string.Empty;

        string mic = V("modelic|@modelIdentCode");
        string sdc = V("sdc|@systemDiffCode");
        string sc = V("chapnum|@systemCode");
        string ssc = V("section|@subSystemCode");
        string sssc = V("subsect|@subSubSystemCode");
        string ac = V("subject|@assyCode");
        string dc = V("discode|@disassyCode");
        string dcv = V("discodev|@disassyCodeVariant");
        string ic = V("incode|@infoCode");
        string icv = V("incodev|@infoCodeVariant");
        string ilc = V("itemloc|@itemLocationCode");
        string lc = V("@learnCode");
        string lec = V("@learnEventCode");

        var code = new StringBuilder();
        code.Append($"DMC-{mic}-{sdc}-{sc}-{ssc}{sssc}-{ac}-{dc}{dcv}-{ic}{icv}-{ilc}");

        if (!string.IsNullOrEmpty(lc))
        {
            code.Append($"-{lc}{lec}");
        }

        if (!_noIssue)
        {
            if (issueInfo != null)
            {
                string issno = XmlUtils.XPathFirstValue(null, issueInfo, "@issno|@issueNumber") ?? string.Empty;
                string inwork = XmlUtils.XPathFirstValue(null, issueInfo, "@inwork|@inWork") ?? "00";
                code.Append($"_{issno}-{inwork}");
            }
            else if (language != null)
            {
                code.Append("_???-??");
            }
        }

        if (language != null)
        {
            string lang = XmlUtils.XPathFirstValue(null, language, "@language|@languageIsoCode") ?? string.Empty;
            string country = XmlUtils.XPathFirstValue(null, language, "@country|@countryIsoCode") ?? string.Empty;
            code.Append($"_{lang}-{country}");
        }

        if (FindCsdbObject(code.ToString(), out result))
        {
            return true;
        }

        if (_verbosity >= 1)
        {
            stderr.WriteLine($"{Name}: WARNING: Could not read referenced object: {code}");
        }
        return false;
    }

    /// <summary>
    /// Locate a CSDB object file whose name begins with <paramref name="code"/>
    /// and looks like a data module (DMC-*.XML). Private helper standing in for
    /// the common <c>find_csdb_object</c>/<c>is_dm</c>.
    /// </summary>
    private bool FindCsdbObject(string code, out string result)
    {
        result = string.Empty;
        string baseDir;
        try
        {
            baseDir = Path.GetFullPath(_searchDir);
        }
        catch
        {
            return false;
        }

        if (!Directory.Exists(baseDir))
        {
            return false;
        }

        var option = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(baseDir, code + "*", option);
        }
        catch
        {
            return false;
        }

        foreach (string path in candidates)
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith("DMC-", StringComparison.Ordinal) &&
                name.EndsWith(".XML", StringComparison.OrdinalIgnoreCase))
            {
                result = path;
                return true;
            }
        }
        return false;
    }

    // ----------------------------------------------------------------------
    // Static helpers
    // ----------------------------------------------------------------------

    private static string BuildApplicElemsXPath()
    {
        string list = EmbeddedResources.Exists("aspp/elements.list")
            ? EmbeddedResources.ReadText("aspp/elements.list")
            : FallbackElementsList;
        // The list file is one XPath fragment per line, joined with '|'.
        var parts = list.Split('\n')
            .Select(l => l.Trim().TrimEnd('|').Trim())
            .Where(l => l.Length > 0);
        return string.Join("|", parts);
    }

    // A pragmatic subset covering the most common applicable elements. Used only
    // if the full elements.list resource is not embedded.
    private const string FallbackElementsList =
        ".//para|.//proceduralStep|.//step|.//note|.//warning|.//caution|" +
        ".//figure|.//table|.//listItem|.//levelledPara|.//dmRef|.//pmRef|" +
        ".//reasonForUpdate|.//randomList|.//sequentialList|.//definitionList";

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage:");
        stdout.WriteLine($"  s1kd-{Name} [options] [<object> ...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -., --dump-disptext   Dump the built-in .disptext file.");
        stdout.WriteLine("  -,, --dump-xsl        Dump the built-in XSLT for generating display text.");
        stdout.WriteLine("  -A, --act <ACT>       Use <ACT> when generating display text.");
        stdout.WriteLine("  -a, --id <ID>         Use <ID> for DM-level applic.");
        stdout.WriteLine("  -C, --cct <CCT>       Use <CCT> when generating display text.");
        stdout.WriteLine("  -c, --search          Search for ACT/CCT data modules.");
        stdout.WriteLine("  -D, --delete          Remove all display text.");
        stdout.WriteLine("  -d, --dir <dir>       Directory to start search for ACT/CCT in.");
        stdout.WriteLine("  -F, --format <fmt>    Use a custom format string for generating display text.");
        stdout.WriteLine("  -f, --overwrite       Overwrite input file(s).");
        stdout.WriteLine("  -G, --disptext <file> Specify .disptext file.");
        stdout.WriteLine("  -g, --generate        Generate display text for applicability annotations.");
        stdout.WriteLine("  -k, --keep            Do not overwrite existing display text.");
        stdout.WriteLine("  -l, --list            Treat input as list of modules.");
        stdout.WriteLine("  -N, --omit-issue      Assume issue/inwork number are omitted.");
        stdout.WriteLine("  -p, --presentation    Convert semantic applicability to presentation applicability.");
        stdout.WriteLine("  -q, --quiet           Quiet mode.");
        stdout.WriteLine("  -r, --recursive       Search for ACT/CCT recursively.");
        stdout.WriteLine("  -t, --tags <mode>     Add display text tags before elements with applicability.");
        stdout.WriteLine("  -v, --verbose         Verbose output.");
        stdout.WriteLine("  -x, --xsl <XSL>       Use custom XSLT script to generate display text.");
        stdout.WriteLine("  -h, -?, --help        Show help/usage message.");
        stdout.WriteLine("      --version         Show version information.");
        stdout.WriteLine("  <object> ...          CSDB objects to process.");
    }
}

/// <summary>
/// Parsed <c>.disptext</c> configuration: operator strings plus the ordered
/// property-format rules. Replaces the role of the generated <c>disptext.xsl</c>
/// stylesheet (which is EXSLT-based and not runnable under
/// <see cref="System.Xml.Xsl.XslCompiledTransform"/>).
/// </summary>
internal sealed class DispTextConfig
{
    public string And { get; } = " and ";
    public string Or { get; } = " or ";
    public string OpenGroup { get; } = "(";
    public string CloseGroup { get; } = ")";
    public string SetOp { get; } = ", ";
    public string RangeOp { get; } = "-";

    private readonly List<MatchRule> _rules = new();
    private readonly PropertyRule _defaultRule;

    public enum PartKind { Name, Text, Values }

    /// <summary>One piece of a property's display-text format.</summary>
    public sealed class FormatPart
    {
        public PartKind Kind { get; init; }
        public string Text { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string>? ValueLabels { get; init; }
    }

    /// <summary>The ordered format parts for a matched property.</summary>
    public sealed class PropertyRule
    {
        public List<FormatPart> Parts { get; } = new();
    }

    /// <summary>A rule plus the predicate that selects which asserts it applies to.</summary>
    private sealed class MatchRule
    {
        public required Func<string?, string?, XmlNode, bool> Matches { get; init; }
        public required PropertyRule Rule { get; init; }
    }

    public DispTextConfig(XmlDocument disptext, string? formatString)
    {
        XmlNode? root = disptext.SelectSingleNode("disptext");

        XmlNode? ops = root?.SelectSingleNode("operators");
        if (ops != null)
        {
            And = Text(ops, "and", And);
            Or = Text(ops, "or", Or);
            OpenGroup = Text(ops, "openGroup", OpenGroup);
            CloseGroup = Text(ops, "closeGroup", CloseGroup);
            SetOp = Text(ops, "set", SetOp);
            RangeOp = Text(ops, "range", RangeOp);
        }

        // Specific <property> rules (matched on ident + type).
        if (root != null)
        {
            foreach (XmlNode prop in root.SelectNodes("property") ?? EmptyList())
            {
                string id = (prop as XmlElement)?.GetAttribute("ident") ?? string.Empty;
                string ty = (prop as XmlElement)?.GetAttribute("type") ?? string.Empty;
                PropertyRule r = ParseRule(prop);
                _rules.Add(new MatchRule
                {
                    Matches = (ident, type, _) => ident == id && type == ty,
                    Rule = r,
                });
            }

            // <conditionType> rules: match conditions whose cond's type ref equals ident.
            foreach (XmlNode ct in root.SelectNodes("conditionType") ?? EmptyList())
            {
                string id = (ct as XmlElement)?.GetAttribute("ident") ?? string.Empty;
                PropertyRule r = ParseRule(ct);
                _rules.Add(new MatchRule
                {
                    Matches = (ident, type, assert) =>
                        type == "condition" && ident is not null && CondTypeRef(assert.OwnerDocument, ident) == id,
                    Rule = r,
                });
            }

            // <productAttributes> / <conditions> default-by-type rules.
            XmlNode? pa = root.SelectSingleNode("productAttributes");
            if (pa != null)
            {
                PropertyRule r = ParseRule(pa);
                _rules.Add(new MatchRule { Matches = (_, type, _) => type == "prodattr", Rule = r });
            }
            XmlNode? co = root.SelectSingleNode("conditions");
            if (co != null)
            {
                PropertyRule r = ParseRule(co);
                _rules.Add(new MatchRule { Matches = (_, type, _) => type == "condition", Rule = r });
            }
        }

        XmlNode? def = root?.SelectSingleNode("default");
        _defaultRule = def != null ? ParseRule(def) : DefaultRule();

        // A -F format string overrides the assert-level format entirely
        // (mirrors apply_format_str, which rewrites the assert text template).
        if (formatString != null)
        {
            PropertyRule fmt = ParseFormatString(formatString);
            _rules.Clear();
            _defaultRule = fmt;
        }
    }

    /// <summary>
    /// Look up the rule applying to an assertion. Mirrors the choose/when order
    /// in the generated assert template (specific property, condition type,
    /// type-default, then default).
    /// </summary>
    public PropertyRule SelectRule(string? ident, string? type, XmlNode assert)
    {
        foreach (MatchRule mr in _rules)
        {
            if (mr.Matches(ident, type, assert))
            {
                return mr.Rule;
            }
        }
        return _defaultRule;
    }

    private static PropertyRule ParseRule(XmlNode rule)
    {
        var pr = new PropertyRule();
        foreach (XmlNode child in rule.ChildNodes)
        {
            switch (child.LocalName)
            {
                case "name":
                    pr.Parts.Add(new FormatPart { Kind = PartKind.Name });
                    break;
                case "text":
                    pr.Parts.Add(new FormatPart { Kind = PartKind.Text, Text = child.InnerText });
                    break;
                case "values":
                    Dictionary<string, string>? labels = null;
                    foreach (XmlNode v in child.SelectNodes("value") ?? EmptyList())
                    {
                        string match = (v as XmlElement)?.GetAttribute("match") ?? string.Empty;
                        (labels ??= new Dictionary<string, string>())[match] = v.InnerText;
                    }
                    pr.Parts.Add(new FormatPart { Kind = PartKind.Values, ValueLabels = labels });
                    break;
            }
        }
        return pr;
    }

    /// <summary>Parse a -F format string into a rule (mirrors apply_format_str).</summary>
    private static PropertyRule ParseFormatString(string fmt)
    {
        var pr = new PropertyRule();
        var sb = new StringBuilder();

        void Flush()
        {
            if (sb.Length > 0)
            {
                pr.Parts.Add(new FormatPart { Kind = PartKind.Text, Text = sb.ToString() });
                sb.Clear();
            }
        }

        for (int i = 0; i < fmt.Length; i++)
        {
            char ch = fmt[i];
            if (ch == '%')
            {
                if (i + 1 < fmt.Length && fmt[i + 1] == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }
                int end = fmt.IndexOf('%', i + 1);
                if (end < 0)
                {
                    break;
                }
                string key = fmt.Substring(i + 1, end - i - 1);
                if (key == "name")
                {
                    Flush();
                    pr.Parts.Add(new FormatPart { Kind = PartKind.Name });
                }
                else if (key == "values")
                {
                    Flush();
                    pr.Parts.Add(new FormatPart { Kind = PartKind.Values });
                }
                i = end;
            }
            else if (ch == '\\' && i + 1 < fmt.Length)
            {
                char next = fmt[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    default: sb.Append(ch); break;
                }
            }
            else
            {
                sb.Append(ch);
            }
        }
        Flush();
        return pr;
    }

    private static PropertyRule DefaultRule()
    {
        var pr = new PropertyRule();
        pr.Parts.Add(new FormatPart { Kind = PartKind.Name });
        pr.Parts.Add(new FormatPart { Kind = PartKind.Text, Text = ": " });
        pr.Parts.Add(new FormatPart { Kind = PartKind.Values });
        return pr;
    }

    private static string? CondTypeRef(XmlDocument? doc, string ident)
    {
        if (doc == null)
        {
            return null;
        }
        XmlElement? cond = doc.SelectSingleNode($"//cond[@id='{ident}']") as XmlElement;
        if (cond != null)
        {
            string r = cond.GetAttribute("condTypeRefId");
            if (!string.IsNullOrEmpty(r)) return r;
        }
        XmlElement? condition = doc.SelectSingleNode($"//condition[@id='{ident}']") as XmlElement;
        if (condition != null)
        {
            string r = condition.GetAttribute("condtyperef");
            if (!string.IsNullOrEmpty(r)) return r;
        }
        return null;
    }

    private static string Text(XmlNode parent, string name, string fallback)
    {
        XmlNode? n = parent.SelectSingleNode(name);
        return n != null ? n.InnerText : fallback;
    }

    private static XmlNodeList EmptyList() => EmptyNodeListHolder.Instance;

    private static class EmptyNodeListHolder
    {
        public static readonly XmlNodeList Instance = new XmlDocument().ChildNodes;
    }
}
