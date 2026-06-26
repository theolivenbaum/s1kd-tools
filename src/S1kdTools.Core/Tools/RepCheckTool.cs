using System.Text;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-repcheck</c>: validate CIR (Common Information Repository)
/// references in S1000D CSDB objects.
///
/// <para>The C tool decorates each CIR reference element with three attributes in
/// the <c>urn:s1kd-tools:s1kd-repcheck</c> namespace using an XSLT identity
/// transform: <c>type</c> (a short type code), <c>name</c> (a human-readable
/// identifier) and <c>test</c> (an XPath that locates the matching CIR
/// specification). It then evaluates each <c>test</c> XPath against the supplied
/// CIR data modules.</para>
///
/// <para>By default this port reproduces that logic directly over the
/// <see cref="XmlDocument"/> DOM (the built-in XSLT is a plain XSLT 1.0 identity
/// transform with no EXSLT use, so porting the per-element template matches is
/// exact and avoids the namespaced attribute quirks of
/// <see cref="System.Xml.Xsl.XslCompiledTransform"/>). The original stylesheets
/// are embedded under <c>Resources/repcheck/</c> so the <c>-D</c>/<c>--dump-xsl</c>
/// option still emits the authoritative source.</para>
///
/// <para>When a custom extraction stylesheet is supplied with <c>-X</c>/<c>--xsl</c>,
/// the DOM rules are bypassed: the stylesheet is applied with
/// <see cref="System.Xml.Xsl.XslCompiledTransform"/> and CIR references are read
/// from the resulting <c>urn:s1kd-tools:s1kd-repcheck</c> <c>type</c>/<c>name</c>/
/// <c>test</c> attributes, mirroring the C tool's behaviour.</para>
/// </summary>
public sealed class RepCheckTool : ITool
{
    public string Name => "repcheck";
    public string Description => "Validate CIR references in S1000D CSDB objects.";
    public string Version => "1.10.0";

    /// <summary>Exit status: the number of objects exceeded available memory.</summary>
    private const int ExitMaxObjects = 2;

    private enum Verbosity { Quiet, Normal, Verbose, Debug }

    private enum ShowFilenames { None, Invalid, Valid }

    /// <summary>A discovered CIR reference, paired with its generated metadata.</summary>
    private sealed record CirRef(XmlElement Element, string Type, string Ident, string Test);

    private sealed class Options
    {
        public Verbosity Verbosity = Verbosity.Normal;
        public ShowFilenames ShowFilenames = ShowFilenames.None;
        public string SearchDir = ".";
        public bool Recursive;
        public bool NoIssue;
        public bool SearchAllObjs;
        public bool OutputValid;
        public bool ListRefs;
        public bool RemDelete;
        public bool AllRefs;
        public string? Type;

        /// <summary>
        /// Path to a user-supplied extraction stylesheet (<c>-X</c>/<c>--xsl</c>).
        /// When set, CIR references are extracted by applying this stylesheet
        /// (which must decorate ref elements with the
        /// <c>urn:s1kd-tools:s1kd-repcheck</c> <c>type</c>/<c>name</c>/<c>test</c>
        /// attributes) instead of using the built-in DOM rules.
        /// </summary>
        public string? CustomXsl;

        public readonly List<string> Objects = new();
        public readonly List<string> Cirs = new();
        public XmlElement? Report;
    }

    /// <summary>Namespace for the attributes that drive CIR-reference extraction.</summary>
    private const string RepCheckNs = "urn:s1kd-tools:s1kd-repcheck";

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var opts = new Options();
        bool isList = false;
        bool findCir = false;
        bool showStats = false;
        bool xmlReport = false;
        bool dumpXsl = false;
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
                    stdout.WriteLine($"{ProgName} (s1kd-tools) {Version}");
                    return 0;
                case "-A" or "--all-refs":
                    opts.AllRefs = true;
                    break;
                case "-a" or "--all":
                    opts.SearchAllObjs = true;
                    break;
                case "-D" or "--dump-xsl":
                    dumpXsl = true;
                    break;
                case "-d" or "--dir":
                    if (++i >= args.Count) return ArgError(stderr, "-d");
                    opts.SearchDir = args[i];
                    break;
                case "-F" or "--valid-filenames":
                    opts.ShowFilenames = ShowFilenames.Valid;
                    break;
                case "-f" or "--filenames":
                    opts.ShowFilenames = ShowFilenames.Invalid;
                    break;
                case "-L" or "--list-refs":
                    opts.ListRefs = true;
                    break;
                case "-l" or "--list":
                    isList = true;
                    break;
                case "-N" or "--omit-issue":
                    opts.NoIssue = true;
                    break;
                case "-o" or "--output-valid":
                    opts.OutputValid = true;
                    break;
                case "-p" or "--progress" or "--zenity-progress":
                    // Progress display is a no-op in the in-process port.
                    break;
                case "-q" or "--quiet":
                    if (opts.Verbosity > Verbosity.Quiet) opts.Verbosity--;
                    break;
                case "-R" or "--cir":
                    if (++i >= args.Count) return ArgError(stderr, "-R");
                    if (args[i] == "*") findCir = true;
                    else opts.Cirs.Add(args[i]);
                    break;
                case "-r" or "--recursive":
                    opts.Recursive = true;
                    break;
                case "-T" or "--summary":
                    showStats = true;
                    break;
                case "-t" or "--type":
                    if (++i >= args.Count) return ArgError(stderr, "-t");
                    opts.Type = args[i];
                    break;
                case "-v" or "--verbose":
                    if (opts.Verbosity < Verbosity.Debug) opts.Verbosity++;
                    break;
                case "-X" or "--xsl":
                    if (++i >= args.Count) return ArgError(stderr, "-X");
                    opts.CustomXsl = args[i];
                    break;
                case "-x" or "--xml":
                    xmlReport = true;
                    break;
                case "-^" or "--remove-deleted":
                    opts.RemDelete = true;
                    break;
                default:
                    if (a.Length > 1 && a[0] == '-' && a != "-")
                    {
                        stderr.WriteLine($"{ErrPrefix}Unknown option: {a}");
                        return 0;
                    }
                    files.Add(a);
                    break;
            }
        }

        if (dumpXsl)
        {
            // The C tool dumps whichever extraction stylesheet is in effect: the
            // custom one given with -X, otherwise the relevant built-in.
            stdout.Write(opts.CustomXsl != null
                ? File.ReadAllText(opts.CustomXsl)
                : EmbeddedResources.ReadText(opts.AllRefs
                    ? "repcheck/cirrefsall.xsl"
                    : "repcheck/cirrefs.xsl"));
            return 0;
        }

        XmlDocument? reportDoc = null;
        if (xmlReport || showStats)
        {
            reportDoc = XmlUtils.NewDocument();
            opts.Report = reportDoc.CreateElement("repCheck");
            reportDoc.AppendChild(opts.Report);
        }

        if (findCir)
        {
            FindCirs(opts.SearchDir, opts);
            opts.Cirs.Sort(Csdb.CompareBaseName);
            var latest = Csdb.ExtractLatestObjects(opts.Cirs);
            opts.Cirs.Clear();
            opts.Cirs.AddRange(latest);
        }

        // Build the list of objects to check.
        if (files.Count > 0)
        {
            foreach (string f in files)
            {
                if (isList) AddObjectList(opts.Objects, f, opts, stderr);
                else opts.Objects.Add(f);
            }
        }
        else if (isList)
        {
            AddObjectList(opts.Objects, null, opts, stderr);
        }
        else
        {
            opts.Objects.Add("-");
        }

        int err = 0;
        foreach (string obj in opts.Objects)
        {
            if (CheckCirRefsInFile(obj, opts, stdout, stderr) != 0)
            {
                err = 1;
            }
        }

        if (xmlReport && reportDoc != null)
        {
            stdout.Write(XmlUtils.ToXmlString(reportDoc));
            stdout.Write('\n');
        }

        if (showStats && reportDoc != null)
        {
            PrintStats(reportDoc, stderr);
        }

        return err;
    }

    private static int ArgError(TextWriter stderr, string opt)
    {
        stderr.WriteLine($"{ErrPrefix}{opt} requires an argument");
        return 0;
    }

    /// <summary>Check all CIR references in a single CSDB object file.</summary>
    private int CheckCirRefsInFile(string path, Options opts, TextWriter stdout, TextWriter stderr)
    {
        XmlDocument doc;
        string? source = null;
        try
        {
            if (path == "-")
            {
                using var stdin = new StreamReader(Console.OpenStandardInput());
                source = stdin.ReadToEnd();
            }
            else
            {
                source = File.ReadAllText(path);
            }
            doc = XmlUtils.ReadMem(source);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            if (opts.Verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Could not read {path}: {ex.Message}");
            }
            return 1;
        }

        if (opts.Verbosity >= Verbosity.Debug)
        {
            stderr.WriteLine($"{InfPrefix}Checking CIR references in {path}...");
        }

        // Build the source line map from the original parse, before any
        // structural modification (remove-deleted only removes nodes, so
        // surviving CIR-reference elements keep their mapped line). For the
        // built-in extraction path the ref elements are the original DOM nodes,
        // so this maps them directly; the custom -X stylesheet path reports
        // against transform-result nodes that are not in this map (see
        // ExtractCirRefsWithXsl) and therefore yields line 0.
        LineInfo lineInfo = source != null
            ? LineInfo.Build(doc, source)
            : LineInfo.BuildFromFile(doc, path);

        XmlDocument? validTree = null;
        if (opts.OutputValid)
        {
            validTree = (XmlDocument)doc.CloneNode(true);
        }

        if (opts.RemDelete)
        {
            XmlUtils.RemoveDeleteElements(doc);
        }

        int err = CheckCirRefs(doc, path, opts, lineInfo, stdout, stderr);

        if (opts.Verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine(err != 0
                ? $"{FldPrefix}Could not resolve some CIR references in {path}."
                : $"{SucPrefix}All CIR references were resolved in {path}.");
        }

        if ((err != 0 && opts.ShowFilenames == ShowFilenames.Invalid) ||
            (err == 0 && opts.ShowFilenames == ShowFilenames.Valid))
        {
            stdout.WriteLine(path);
        }

        if (opts.OutputValid && err == 0 && validTree != null)
        {
            stdout.Write(XmlUtils.ToXmlString(validTree));
            stdout.Write('\n');
        }

        return err;
    }

    /// <summary>Check all CIR references in a document tree.</summary>
    private int CheckCirRefs(XmlDocument doc, string path, Options opts, LineInfo lineInfo, TextWriter stdout, TextWriter stderr)
    {
        int err = 0;

        XmlElement? rpt = null;
        if (opts.Report != null)
        {
            rpt = opts.Report.OwnerDocument.CreateElement("object");
            rpt.SetAttribute("path", path);
            opts.Report.AppendChild(rpt);
        }

        List<CirRef> refs = ExtractCirRefs(doc, opts);

        foreach (CirRef r in refs)
        {
            if (opts.Type != null && r.Type != opts.Type)
            {
                continue;
            }

            if (opts.ListRefs)
            {
                ListCirRef(r, path, rpt, lineInfo, stdout);
            }
            else if (CheckCirRef(r, path, rpt, opts, lineInfo, stderr) != 0)
            {
                err = 1;
            }
        }

        if (!opts.ListRefs && rpt != null)
        {
            rpt.SetAttribute("valid", err != 0 ? "no" : "yes");
        }

        return err;
    }

    /// <summary>List a CIR reference without validating it.</summary>
    private static void ListCirRef(CirRef r, string path, XmlElement? rpt, LineInfo lineInfo, TextWriter stdout)
    {
        int lineno = LineOf(r.Element, lineInfo);
        if (rpt != null)
        {
            AddRefToReport(rpt, r, lineno, null);
        }
        else
        {
            stdout.WriteLine($"{path}:{lineno}:{r.Ident}");
        }
    }

    /// <summary>Validate a single CIR reference against the available CIRs.</summary>
    private int CheckCirRef(CirRef r, string path, XmlElement? rpt, Options opts, LineInfo lineInfo, TextWriter stderr)
    {
        int lineno = LineOf(r.Element, lineInfo);

        // Explicit CIR reference embedded in the ref element?
        var explicitRefs = new List<XmlNode>();
        foreach (XmlNode n in SelectNodes(r.Element, "refs/dmRef/dmRefIdent"))
        {
            explicitRefs.Add(n);
        }
        foreach (XmlNode n in SelectNodes(r.Element, "refs/refdm"))
        {
            explicitRefs.Add(n);
        }

        if (explicitRefs.Count == 0)
        {
            // Search in all specified/found CIRs.
            foreach (string cir in opts.Cirs)
            {
                if (FindRefInCir(r, cir, opts, stderr))
                {
                    AddRefToReport(rpt, r, lineno, cir);
                    return 0;
                }
            }

            // Search in other objects to check, if allowed.
            if (opts.SearchAllObjs)
            {
                foreach (string obj in opts.Objects)
                {
                    if (FindRefInCir(r, obj, opts, stderr))
                    {
                        AddRefToReport(rpt, r, lineno, obj);
                        return 0;
                    }
                }
            }
        }
        else
        {
            // Only check against the explicitly referenced CIR data modules.
            foreach (XmlNode refIdent in explicitRefs)
            {
                if (FindDmodFname(out string fname, (XmlElement)refIdent, opts, stderr) &&
                    FindRefInCir(r, fname, opts, stderr))
                {
                    AddRefToReport(rpt, r, lineno, fname);
                    return 0;
                }
            }
        }

        if (opts.Verbosity >= Verbosity.Normal)
        {
            stderr.WriteLine($"{ErrPrefix}{path} ({lineno}): {r.Ident} not found.");
        }
        AddRefToReport(rpt, r, lineno, null);
        return 1;
    }

    /// <summary>Evaluate a CIR ref's test XPath against a CIR data module file.</summary>
    private bool FindRefInCir(CirRef r, string cirPath, Options opts, TextWriter stderr)
    {
        if (opts.Verbosity >= Verbosity.Debug)
        {
            stderr.WriteLine($"{InfPrefix}Searching for {r.Ident} in CIR {cirPath}...");
        }

        XmlDocument doc;
        try
        {
            doc = XmlUtils.ReadDoc(cirPath);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return false;
        }

        if (opts.RemDelete)
        {
            XmlUtils.RemoveDeleteElements(doc);
        }

        bool found = doc.SelectSingleNode(r.Test) != null;

        if (opts.Verbosity >= Verbosity.Debug)
        {
            stderr.WriteLine(found
                ? $"{InfPrefix}Found {r.Ident} in CIR {cirPath}"
                : $"{InfPrefix}Not found in CIR {cirPath}");
        }

        return found;
    }

    /// <summary>Add a resolved/unresolved reference to the XML report.</summary>
    private static void AddRefToReport(XmlElement? rpt, CirRef r, int lineno, string? cir)
    {
        if (rpt == null)
        {
            return;
        }

        XmlDocument owner = rpt.OwnerDocument;
        XmlElement node = owner.CreateElement("ref");
        node.SetAttribute("type", r.Type);
        node.SetAttribute("name", r.Ident);
        node.SetAttribute("line", lineno.ToString());
        node.SetAttribute("xpath", XmlUtils.XPathOf(r.Element));
        if (cir != null)
        {
            node.SetAttribute("cir", cir);
        }
        node.AppendChild(owner.ImportNode(r.Element, true));
        rpt.AppendChild(node);
    }

    // ----- CIR reference extraction (ports the XSLT templates) ---------------

    /// <summary>
    /// Walk the document and produce a CIR reference for every element that the
    /// extraction XSLT would decorate. The order matches a document-order
    /// traversal, mirroring the XSLT identity transform followed by an
    /// <c>//*[@s1kd-repcheck:test]</c> selection.
    /// </summary>
    private static List<CirRef> ExtractCirRefs(XmlDocument doc, Options opts)
    {
        if (opts.CustomXsl != null)
        {
            return ExtractCirRefsWithXsl(doc, opts);
        }

        var refs = new List<CirRef>();
        if (doc.DocumentElement != null)
        {
            Walk(doc.DocumentElement, opts, refs);
        }
        return refs;
    }

    /// <summary>
    /// Extract CIR references by applying a user-supplied extraction stylesheet
    /// (<c>-X</c>). The stylesheet decorates ref elements with the
    /// <c>s1kd-repcheck:type</c>/<c>name</c>/<c>test</c> attributes; this reads
    /// those attributes back, strips them from the element (so the report copy
    /// matches the C tool, which removes them before reporting) and produces the
    /// corresponding <see cref="CirRef"/> list in document order.
    /// </summary>
    private static List<CirRef> ExtractCirRefsWithXsl(XmlDocument doc, Options opts)
    {
        var refs = new List<CirRef>();

        var xslt = new XslCompiledTransform();
        var settings = new XsltSettings(enableDocumentFunction: false, enableScript: false);
        xslt.Load(opts.CustomXsl!, settings, new XmlUrlResolver());

        var result = XmlUtils.NewDocument();
        using (var writer = result.CreateNavigator()!.AppendChild())
        {
            xslt.Transform(doc, null, writer);
        }

        if (result.DocumentElement == null)
        {
            return refs;
        }

        // Select every element decorated with a test attribute, in document order.
        var nsmgr = new XmlNamespaceManager(result.NameTable);
        nsmgr.AddNamespace("rc", RepCheckNs);
        XmlNodeList? nodes = result.SelectNodes("//*[@rc:test]", nsmgr);
        if (nodes == null)
        {
            return refs;
        }

        foreach (XmlNode node in nodes)
        {
            if (node is not XmlElement el)
            {
                continue;
            }

            string type = el.GetAttribute("type", RepCheckNs);
            string ident = el.GetAttribute("name", RepCheckNs);
            string test = el.GetAttribute("test", RepCheckNs);

            // Remove the tool-added attributes (mirror remove_repcheck_attrs).
            el.RemoveAttribute("type", RepCheckNs);
            el.RemoveAttribute("name", RepCheckNs);
            el.RemoveAttribute("test", RepCheckNs);

            refs.Add(new CirRef(el, type, ident, test));
        }

        return refs;
    }

    private static void Walk(XmlElement el, Options opts, List<CirRef> refs)
    {
        CirRef? r = MatchRef(el, opts);
        if (r != null)
        {
            refs.Add(r);
        }

        foreach (XmlNode child in el.ChildNodes)
        {
            if (child is XmlElement childEl)
            {
                Walk(childEl, opts, refs);
            }
        }
    }

    private static string Attr(XmlElement el, params string[] names)
    {
        foreach (string n in names)
        {
            if (el.HasAttribute(n))
            {
                return el.GetAttribute(n);
            }
        }
        return string.Empty;
    }

    private static string? ChildVal(XmlElement el, string name)
    {
        foreach (XmlNode child in el.ChildNodes)
        {
            if (child is XmlElement c && c.LocalName == name)
            {
                return c.InnerText;
            }
        }
        return null;
    }

    /// <summary>
    /// Determine whether an element is a CIR reference and, if so, build its
    /// type/name/test metadata exactly as the XSLT does.
    /// </summary>
    private static CirRef? MatchRef(XmlElement el, Options opts)
    {
        switch (el.LocalName)
        {
            // Access point
            case "accessPointRef" when el.HasAttribute("accessPointNumber"):
            case "accpnl" when el.HasAttribute("accpnlnbr"):
            {
                string apn = Attr(el, "accessPointNumber", "accpnlnbr");
                return new CirRef(el, "acp", $"Access Point {apn}",
                    $"//accessPointIdent[@accessPointNumber='{apn}']|//accpnlid[@accpnlnbr='{apn}']");
            }

            // Applicability annotation
            case "applicRef":
            {
                string aiv = Attr(el, "applicIdentValue");
                return new CirRef(el, "app", $"Applic {aiv}",
                    $"//applicSpecIdent[@applicIdentValue='{aiv}']");
            }

            // Caution
            case "cautionRef":
            {
                string cin = Attr(el, "cautionIdentNumber");
                return new CirRef(el, "caut", $"Caution {cin}",
                    $"//cautionIdent[@cautionIdentNumber='{cin}']");
            }

            // Circuit breaker
            case "circuitBreakerRef":
            case "cb":
            {
                string cbn = Attr(el, "circuitBreakerNumber", "cbnbr");
                return new CirRef(el, "cbr", $"Circuit breaker {cbn}",
                    $"//circuitBreakerIdent[@circuitBreakerNumber='{cbn}']|//cbid[@cbnbr='{cbn}']");
            }

            // Control/Indicator
            case "controlIndicatorRef":
            {
                string cin = Attr(el, "controlIndicatorNumber");
                return new CirRef(el, "cin", $"Control/Indicator {cin}",
                    $"//controlIndicatorSpec[@controlIndicatorNumber='{cin}']");
            }

            // Enterprise
            case "responsiblePartnerCompany" when el.HasAttribute("enterpriseCode"):
            case "originator" when el.HasAttribute("enterpriseCode"):
            {
                string ent = Attr(el, "enterpriseCode");
                return new CirRef(el, "ent", $"Enterprise {ent}",
                    $"//enterpriseIdent[@manufacturerCodeValue='{ent}']|//organizationid[@mfc='{ent}']");
            }
            case "rpc" when el.InnerText.Length > 0:
            case "orig" when el.InnerText.Length > 0:
            {
                string ent = el.InnerText;
                return new CirRef(el, "ent", $"Enterprise {ent}",
                    $"//enterpriseIdent[@manufacturerCodeValue='{ent}']|//organizationid[@mfc='{ent}']");
            }

            // Functional item
            case "functionalItemRef":
            case "ein":
            {
                string fin = Attr(el, "functionalItemNumber", "einnbr");
                return new CirRef(el, "fin", $"Functional item {fin}",
                    $"//functionalItemIdent[@functionalItemNumber='{fin}']|//einid[@einnbr='{fin}']");
            }

            // Part
            case "partRef":
            {
                string mcv = Attr(el, "manufacturerCodeValue");
                string pnv = Attr(el, "partNumberValue");
                // Note: the XSLT has a known bug referencing pnv via "pnr" (an
                // undefined element) in the second alternative; the first branch
                // is what actually resolves CSDB-2.0 part identifiers.
                return new CirRef(el, "part", $"Part {mcv}/{pnv}",
                    $"//partIdent[@manufacturerCodeValue='{mcv}' and @partNumberValue='{pnv}']" +
                    $"|//partid[@mfc='{mcv}' and @pnr='{pnv}']");
            }

            // Supply
            case "supplyRef":
            case "con":
            {
                string sn = Attr(el, "supplyNumber", "connbr");
                string snt = Attr(el, "supplyNumberType");
                string name = snt.Length > 0 ? $"Supply {sn} ({snt})" : $"Supply {sn}";
                string test = snt.Length > 0
                    ? $"//supplyIdent[@supplyNumber='{sn}' and @supplyNumberType='{snt}']|//conitemid[@itemnbr='{sn}']"
                    : $"//supplyIdent[@supplyNumber='{sn}']|//conitemid[@itemnbr='{sn}']";
                return new CirRef(el, "supply", name, test);
            }

            // Tool
            case "toolRef":
            case "tool" when el.HasAttribute("toolnbr"):
            {
                string mcv = Attr(el, "manufacturerCodeValue", "mfc");
                string tn = Attr(el, "toolNumber", "toolnbr");
                string name = mcv.Length > 0 ? $"Tool {mcv}/{tn}" : $"Tool {tn}";
                var test = new StringBuilder("//toolIdent[");
                if (mcv.Length > 0) test.Append($"@manufacturerCodeValue='{mcv}' and ");
                test.Append($"@toolNumber='{tn}']|//toolid[");
                if (mcv.Length > 0) test.Append($"@mfc='{mcv}' and");
                test.Append($"@toolnbr='{tn}']");
                return new CirRef(el, "tool", name, test.ToString());
            }

            // Warning
            case "warningRef":
            {
                string win = Attr(el, "warningIdentNumber");
                return new CirRef(el, "warn", $"Warning {win}",
                    $"//warningIdent[@warningIdentNumber='{win}']");
            }

            // Zone
            case "zoneRef" when el.HasAttribute("zoneNumber"):
            case "zone" when el.HasAttribute("zonenbr"):
            {
                string zn = Attr(el, "zoneNumber", "zonenbr");
                return new CirRef(el, "zone", $"Zone {zn}",
                    $"//zoneIdent[@zoneNumber='{zn}']|//zoneid[@zonenbr='{zn}']");
            }
        }

        // Indirect references via <identNumber> (only with -A/--all-refs).
        if (opts.AllRefs)
        {
            return MatchIndirectRef(el);
        }

        return null;
    }

    /// <summary>
    /// Ports the additional templates present only in <c>cirrefsall.xsl</c>
    /// (indirect references via the <c>identNumber</c>/<c>identno</c> element).
    /// </summary>
    private static CirRef? MatchIndirectRef(XmlElement el)
    {
        bool isIdentNumber = el.LocalName is "identNumber" or "identno";
        if (!isIdentNumber)
        {
            return null;
        }

        // Requires a part/serial number child (partAndSerialNumber/pnr).
        bool hasPart = ChildVal(el, "partAndSerialNumber") != null
            || HasChild(el, "partAndSerialNumber")
            || ChildVal(el, "pnr") != null;
        if (!hasPart)
        {
            return null;
        }

        string mfc = ChildVal(el, "manufacturerCode") ?? ChildVal(el, "mfc") ?? string.Empty;
        string pnr = PartNumber(el);

        string? parentName = (el.ParentNode as XmlElement)?.LocalName;
        switch (parentName)
        {
            case "spareDescr":
            case "spare":
                return new CirRef(el, "part", $"Part {mfc}/{pnr}",
                    $"//partIdent[@manufacturerCodeValue='{mfc}' and @partNumberValue='{pnr}']" +
                    $"|//partid[@mfc='{mfc}' and @pnr='{pnr}']");

            case "supplyDescr":
            case "supply":
                return new CirRef(el, "supply", $"Supply {pnr}",
                    $"//supplyIdent[@supplyNumber='{pnr}']|//conitemid[@itemnbr='{pnr}']");

            case "supportEquipDescr":
            case "supequi":
                return new CirRef(el, "tool", $"Tool {mfc}/{pnr}",
                    $"//toolIdent[@manufacturerCodeValue='{mfc}' and @toolNumber='{pnr}']" +
                    $"|//toolid[@mfc='{mfc}' and@toolnbr='{pnr}']");

            default:
                // Generic identNumber: resolves against part, supply or tool.
                return new CirRef(el, "identno", $"Ident No. {mfc}/{pnr}",
                    $"//partIdent[@manufacturerCodeValue='{mfc}' and @partNumberValue='{pnr}']" +
                    $"|//partid[@mfc='{mfc}' and @pnr='{pnr}']" +
                    $"|//supplyIdent[@supplyNumber='{pnr}']|//conitemid[@itemnbr='{pnr}']" +
                    $"|//toolIdent[@manufacturerCodeValue='{mfc}' and @toolNumber='{pnr}']" +
                    $"|//toolid[@mfc='{mfc}' and@toolnbr='{pnr}']");
        }
    }

    private static bool HasChild(XmlElement el, string name)
    {
        foreach (XmlNode child in el.ChildNodes)
        {
            if (child is XmlElement c && c.LocalName == name)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Extract partAndSerialNumber/partNumber or pnr from an identNumber.</summary>
    private static string PartNumber(XmlElement el)
    {
        foreach (XmlNode child in el.ChildNodes)
        {
            if (child is XmlElement c)
            {
                if (c.LocalName == "partAndSerialNumber")
                {
                    foreach (XmlNode gc in c.ChildNodes)
                    {
                        if (gc is XmlElement pn && pn.LocalName == "partNumber")
                        {
                            return pn.InnerText;
                        }
                    }
                }
                else if (c.LocalName == "pnr")
                {
                    return c.InnerText;
                }
            }
        }
        return string.Empty;
    }

    // ----- Explicit reference filename resolution ---------------------------

    /// <summary>
    /// Resolve the filename of a referenced CIR data module from its
    /// <c>dmRefIdent</c>/<c>refdm</c> element. Mirrors <c>find_dmod_fname</c>.
    /// </summary>
    private bool FindDmodFname(out string dst, XmlElement dmRefIdent, Options opts, TextWriter stderr)
    {
        dst = string.Empty;

        XmlNode? dmCode = SelectFirst(dmRefIdent, "dmCode") ?? SelectFirst(dmRefIdent, "avee");
        XmlNode? issueInfo = SelectFirst(dmRefIdent, "issueInfo") ?? SelectFirst(dmRefIdent, "issno");
        XmlNode? language = SelectFirst(dmRefIdent, "language");

        if (dmCode is not XmlElement dmCodeEl)
        {
            return false;
        }

        string V(params string[] names) => CodeValue(dmCodeEl, names);

        var sb = new StringBuilder();
        sb.Append("DMC-")
          .Append(V("modelic", "modelIdentCode")).Append('-')
          .Append(V("sdc", "systemDiffCode")).Append('-')
          .Append(V("chapnum", "systemCode")).Append('-')
          .Append(V("section", "subSystemCode"))
          .Append(V("subsect", "subSubSystemCode")).Append('-')
          .Append(V("subject", "assyCode")).Append('-')
          .Append(V("discode", "disassyCode"))
          .Append(V("discodev", "disassyCodeVariant")).Append('-')
          .Append(V("incode", "infoCode"))
          .Append(V("incodev", "infoCodeVariant")).Append('-')
          .Append(V("itemloc", "itemLocationCode"));

        string learnCode = CodeValue(dmCodeEl, new[] { "learnCode" }, attrOnly: true);
        string learnEvent = CodeValue(dmCodeEl, new[] { "learnEventCode" }, attrOnly: true);
        if (learnCode.Length > 0)
        {
            sb.Append('-').Append(learnCode).Append(learnEvent);
        }

        if (!opts.NoIssue)
        {
            if (issueInfo is XmlElement issEl)
            {
                string issueNumber = AttrAny(issEl, "issno", "issueNumber");
                string inWork = AttrAny(issEl, "inwork", "inWork");
                sb.Append('_').Append(issueNumber).Append('-').Append(inWork.Length > 0 ? inWork : "00");
            }
            else if (language != null)
            {
                sb.Append("_???-??");
            }
        }

        if (language is XmlElement langEl)
        {
            string lang = AttrAny(langEl, "language", "languageIsoCode");
            string country = AttrAny(langEl, "country", "countryIsoCode");
            sb.Append('_').Append(lang).Append('-').Append(country);
        }

        string code = sb.ToString();

        // Look for the DM under the search directory.
        if (FindCsdbObject(out dst, opts.SearchDir, code, opts.Recursive))
        {
            return true;
        }
        // Look in the list of CIRs and objects.
        if (FindInList(out dst, opts.Cirs, code) || FindInList(out dst, opts.Objects, code))
        {
            return true;
        }

        if (opts.Verbosity >= Verbosity.Normal)
        {
            stderr.WriteLine($"{WrnPrefix}Could not read referenced object: {code}");
        }
        return false;
    }

    private static string CodeValue(XmlElement dmCode, string[] names, bool attrOnly = false)
    {
        // Try child elements (CSDB 4.x) then attributes (CSDB 2.x form).
        if (!attrOnly)
        {
            foreach (XmlNode child in dmCode.ChildNodes)
            {
                if (child is XmlElement c && Array.IndexOf(names, c.LocalName) >= 0)
                {
                    return c.InnerText;
                }
            }
        }
        foreach (string n in names)
        {
            if (dmCode.HasAttribute(n))
            {
                return dmCode.GetAttribute(n);
            }
        }
        return string.Empty;
    }

    private static string AttrAny(XmlElement el, params string[] names) => Attr(el, names);

    private static bool FindInList(out string dst, IReadOnlyList<string> list, string code)
    {
        foreach (string p in list)
        {
            if (Csdb.StrMatch(code, Path.GetFileName(p)))
            {
                dst = p;
                return true;
            }
        }
        dst = string.Empty;
        return false;
    }

    private static bool FindCsdbObject(out string dst, string dir, string code, bool recursive)
    {
        dst = string.Empty;
        if (!Directory.Exists(dir))
        {
            return false;
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (string file in Directory.EnumerateFiles(dir, "*", option))
        {
            string name = Path.GetFileName(file);
            if (Csdb.IsDataModule(name) && Csdb.StrMatch(code, name))
            {
                dst = file;
                return true;
            }
        }
        return false;
    }

    // ----- CIR discovery (-R *) ---------------------------------------------

    /// <summary>Find CIR data modules under a directory. Mirrors <c>find_cirs</c>.</summary>
    private static void FindCirs(string searchDir, Options opts)
    {
        if (!Directory.Exists(searchDir))
        {
            return;
        }

        var option = opts.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (string file in Directory.EnumerateFiles(searchDir, "*", option))
        {
            string name = Path.GetFileName(file);
            if (Csdb.IsDataModule(name) && IsCir(file, opts.RemDelete))
            {
                opts.Cirs.Add(file);
            }
        }
    }

    /// <summary>
    /// Determine whether a data module is a CIR/TIR. Mirrors <c>is_cir</c> from
    /// the shared C utilities.
    /// </summary>
    private static bool IsCir(string path, bool ignoreDel)
    {
        XmlDocument doc;
        try
        {
            doc = XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return false;
        }

        bool isCir = doc.SelectSingleNode("//commonRepository|//techRepository|//techrep") != null;

        if (isCir && ignoreDel)
        {
            bool deleted = doc.SelectSingleNode(
                "//dmodule[identAndStatusSection/dmStatus/@issueType='deleted' or status/issno/@type='deleted']") != null;
            isCir = !deleted;
        }

        return isCir;
    }

    // ----- Object list reading ----------------------------------------------

    private static void AddObjectList(List<string> objects, string? list, Options opts, TextWriter stderr)
    {
        IEnumerable<string> lines;
        try
        {
            lines = list != null
                ? File.ReadAllLines(list)
                : ReadAllLines(Console.In);
        }
        catch (IOException)
        {
            if (opts.Verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Could not read list: {list}");
            }
            return;
        }

        foreach (string raw in lines)
        {
            string path = raw.Trim('\t', '\r', '\n', ' ');
            if (path.Length > 0)
            {
                objects.Add(path);
            }
        }
    }

    private static IEnumerable<string> ReadAllLines(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            yield return line;
        }
    }

    // ----- Summary ----------------------------------------------------------

    /// <summary>Print a summary of the check (mirrors stats.xsl output).</summary>
    private static void PrintStats(XmlDocument report, TextWriter stderr)
    {
        XmlNodeList? objects = report.SelectNodes("/repCheck/object");
        int total = objects?.Count ?? 0;
        int invalid = 0;
        if (objects != null)
        {
            foreach (XmlNode o in objects)
            {
                if (o is XmlElement e && e.GetAttribute("valid") == "no")
                {
                    invalid++;
                }
            }
        }
        int valid = total - invalid;

        stderr.WriteLine("Checked " + total + " CSDB object" + (total == 1 ? "" : "s"));
        stderr.WriteLine("Passed " + valid + " CSDB object" + (valid == 1 ? "" : "s"));
        stderr.WriteLine("Failed " + invalid + " CSDB object" + (invalid == 1 ? "" : "s"));
    }

    // ----- DOM helpers ------------------------------------------------------

    /// <summary>Select child nodes by a simple relative path of element names.</summary>
    private static IEnumerable<XmlNode> SelectNodes(XmlElement context, string path)
    {
        string[] steps = path.Split('/');
        IEnumerable<XmlNode> current = new[] { (XmlNode)context };
        foreach (string step in steps)
        {
            var next = new List<XmlNode>();
            foreach (XmlNode node in current)
            {
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child is XmlElement c && c.LocalName == step)
                    {
                        next.Add(c);
                    }
                }
            }
            current = next;
        }
        return current;
    }

    private static XmlNode? SelectFirst(XmlElement context, string name)
    {
        foreach (XmlNode child in context.ChildNodes)
        {
            if (child is XmlElement c && c.LocalName == name)
            {
                return c;
            }
        }
        return null;
    }

    private static int LineOf(XmlElement el, LineInfo lineInfo)
    {
        // For the built-in extraction path the ref element is the original DOM
        // node, so the source line map resolves it (mirroring the C's
        // xmlGetLineNo(ref)). For the custom -X stylesheet path the element comes
        // from the transform result and is not in the map, so this yields 0.
        return lineInfo.LineOf(el);
    }

    // ----- Messages ---------------------------------------------------------

    private const string ProgName = "s1kd-repcheck";
    private const string ErrPrefix = ProgName + ": ERROR: ";
    private const string WrnPrefix = ProgName + ": WARNING: ";
    private const string InfPrefix = ProgName + ": INFO: ";
    private const string SucPrefix = ProgName + ": SUCCESS: ";
    private const string FldPrefix = ProgName + ": FAILED: ";

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: {ProgName} [options] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -A, --all-refs         Validate indirect CIR references.");
        stdout.WriteLine("  -a, --all              Resolve against CIRs specified as objects to check.");
        stdout.WriteLine("  -d, --dir <dir>        Search for CIRs in <dir>.");
        stdout.WriteLine("  -F, --valid-filenames  List valid files.");
        stdout.WriteLine("  -f, --filenames        List invalid files.");
        stdout.WriteLine("  -h, -?, --help         Show help/usage message.");
        stdout.WriteLine("  -L, --list-refs        List CIR refs instead of validating them.");
        stdout.WriteLine("  -l, --list             Treat input as list of CSDB objects.");
        stdout.WriteLine("  -N, --omit-issue       Assume issue/inwork numbers are omitted.");
        stdout.WriteLine("  -o, --output-valid     Output valid CSDB objects to stdout.");
        stdout.WriteLine("  -p, --progress         Display a progress bar.");
        stdout.WriteLine("  -q, --quiet            Quiet mode.");
        stdout.WriteLine("  -R, --cir <CIR>        Check references against the given CIR.");
        stdout.WriteLine("  -r, --recursive        Search for CIRs recursively.");
        stdout.WriteLine("  -T, --summary          Print a summary of the check.");
        stdout.WriteLine("  -t, --type <type>      Type of CIR references to check.");
        stdout.WriteLine("  -v, --verbose          Verbose output.");
        stdout.WriteLine("  -X, --xsl <file>       Custom XSLT for extracting CIR references.");
        stdout.WriteLine("  -x, --xml              Output XML report.");
        stdout.WriteLine("  -^, --remove-deleted   Validate with elements marked as \"delete\" removed.");
        stdout.WriteLine("      --version          Show version information.");
        stdout.WriteLine("  <object>               CSDB object(s) to check.");
    }
}
