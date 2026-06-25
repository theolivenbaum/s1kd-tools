using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-newupf</c>: create a new data update file (UPF) by comparing
/// two issues of a common information repository (CIR) object. The tool compares
/// a SOURCE CIR (the older issue) against a TARGET CIR (the newer issue) and
/// emits an update file describing the objects that were deleted, inserted and
/// replaced between them.
///
/// Mirrors the C tool's option set, exit codes, generated filename and the
/// metadata/structure of the produced update file. Object diffing is done with
/// the DOM (no XSLT); issue down-conversion (4.1/4.2/5.0) reuses the shared
/// <c>to4x</c>/<c>to50</c> stylesheets via <see cref="XslCompiledTransform"/>.
/// </summary>
public sealed class NewupfTool : ITool
{
    public string Name => "newupf";
    public string Description => "Create a new data update file.";
    public string Version => "3.0.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitUpfExists = 1;
    private const int ExitMissingArgs = 2;
    private const int ExitInvalidArgs = 3;
    private const int ExitBadIssue = 4;
    private const int ExitBadTemplate = 5;
    private const int ExitBadTemplDir = 6;
    private const int ExitOsError = 7;

    private enum Issue { NoIss, Iss41, Iss42, Iss50, Iss6 }

    private const Issue DefaultS1000DIssue = Issue.Iss6;

    /* XPath matching every supported CIR object spec (mirror CIR_OBJECT_XPATH). */
    private const string CirObjectXPath =
        "//accessPointSpec|" +
        "//applicSpec|" +
        "//cautionSpec|" +
        "//circuitBreakerSpec|" +
        "//controlIndicatorSpec|" +
        "//enterpriseSpec|" +
        "//functionalItemSpec|" +
        "//partSpec|" +
        "//supplySpec|" +
        "//toolSpec|" +
        "//warningSpec";

    /* Per-run option state. */
    private Issue _issue = Issue.NoIss;
    private string? _templateDir;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool overwrite = false;
        bool noOverwriteError = false;
        bool verbose = false;
        string? @out = null;
        string? defaultsFname = null;
        bool customDefaults = false;
        var positional = new List<string>();

        try
        {
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "-h" or "-?" or "--help":
                        ShowHelp(stdout);
                        return 0;
                    case "--version":
                        ShowVersion(stdout);
                        return 0;
                    case "-@" or "--out":
                        @out = NextArg(args, ref i, a, stderr);
                        break;
                    case "-$" or "--issue":
                        _issue = GetIssue(NextArg(args, ref i, a, stderr), stderr);
                        break;
                    case "-%" or "--templates":
                        _templateDir = NextArg(args, ref i, a, stderr);
                        break;
                    case "-d" or "--defaults":
                        defaultsFname = NextArg(args, ref i, a, stderr);
                        customDefaults = true;
                        break;
                    case "-f" or "--overwrite":
                        overwrite = true;
                        break;
                    case "-q" or "--quiet":
                        noOverwriteError = true;
                        break;
                    case "-v" or "--verbose":
                        verbose = true;
                        break;
                    case "-~" or "--dump-templates":
                        DumpTemplate(NextArg(args, ref i, a, stderr), stderr);
                        return 0;
                    default:
                        if (a.StartsWith('-') && a.Length > 1 && a != "-")
                        {
                            stderr.WriteLine($"s1kd-{Name}: ERROR: Unknown option: {a}");
                            return ExitInvalidArgs;
                        }
                        positional.Add(a);
                        break;
                }
            }

            if (positional.Count < 2)
            {
                return ExitMissingArgs;
            }

            string source = positional[0];
            string target = positional[1];

            XmlDocument sourceDoc = XmlUtils.ReadDoc(source);
            XmlDocument targetDoc = XmlUtils.ReadDoc(target);

            /* Load defaults (.defaults) for issue/templates fallbacks. */
            if (!customDefaults)
            {
                Csdb.FindConfig(Csdb.DefaultsFileName, out string found);
                defaultsFname = found;
            }

            LoadDefaults(defaultsFname!, stderr);

            if (_issue == Issue.NoIss)
            {
                _issue = DefaultS1000DIssue;
            }

            XmlDocument updateFile = XmlSkeleton(_templateDir, stderr);

            if (TypeOfCir(sourceDoc) != TypeOfCir(targetDoc))
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Source CIR and target CIR are of different types.");
                return ExitInvalidArgs;
            }

            SetMetadata(updateFile, sourceDoc, targetDoc);

            var update = (XmlElement)XmlUtils.XPathFirstNode(updateFile, null, "//content/update")!;

            XmlElement? deleteGroup = DeleteObjects(updateFile, sourceDoc, targetDoc);
            if (deleteGroup != null)
            {
                update.AppendChild(deleteGroup);
            }

            XmlElement? insertGroup = InsertObjects(updateFile, sourceDoc, targetDoc);
            if (insertGroup != null)
            {
                update.AppendChild(insertGroup);
            }

            XmlElement? replaceGroup = ReplaceObjects(updateFile, sourceDoc, targetDoc);
            if (replaceGroup != null)
            {
                update.AppendChild(replaceGroup);
            }

            if (_issue < Issue.Iss6)
            {
                updateFile = ToIssue(updateFile, _issue);
            }

            string? outdir = null;
            if (@out != null && Directory.Exists(@out))
            {
                outdir = @out;
                @out = null;
            }

            @out ??= AutoName(updateFile);

            string outPath = outdir != null ? Path.Combine(outdir, @out) : @out;

            if (!overwrite && File.Exists(outPath))
            {
                if (noOverwriteError)
                {
                    return 0;
                }
                stderr.WriteLine($"s1kd-{Name}: ERROR: {outPath} already exists. Use -f to overwrite.");
                return ExitUpfExists;
            }

            XmlUtils.SaveDoc(updateFile, outPath);

            if (verbose)
            {
                stdout.WriteLine(outPath);
            }
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }

        return 0;
    }

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: {opt} requires an argument");
            throw new ExitException(ExitMissingArgs);
        }
        return args[i];
    }

    private Issue GetIssue(string iss, TextWriter stderr)
    {
        return iss switch
        {
            "6" => Issue.Iss6,
            "5.0" => Issue.Iss50,
            "4.2" => Issue.Iss42,
            "4.1" => Issue.Iss41,
            _ => Fail(),
        };

        Issue Fail()
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Unsupported issue: {iss}");
            throw new ExitException(ExitBadIssue);
        }
    }

    /* ----- defaults handling (mirror copyDefaultValue + defaults loading) ----- */

    private void LoadDefaults(string defaultsFname, TextWriter stderr)
    {
        if (!File.Exists(defaultsFname))
        {
            return;
        }

        XmlDocument? defaultsXml = null;
        try
        {
            defaultsXml = XmlUtils.ReadDoc(defaultsFname);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            defaultsXml = null;
        }

        if (defaultsXml?.DocumentElement != null)
        {
            for (XmlNode? cur = defaultsXml.DocumentElement.FirstChild; cur != null; cur = cur.NextSibling)
            {
                if (cur.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                var el = (XmlElement)cur;
                if (!el.HasAttribute("ident") || !el.HasAttribute("value"))
                {
                    continue;
                }
                CopyDefaultValue(el.GetAttribute("ident"), el.GetAttribute("value"));
            }
        }
        else
        {
            // Plain text .defaults: "key value" per line.
            foreach (string line in File.ReadLines(defaultsFname))
            {
                string trimmed = line.TrimStart();
                int sp = trimmed.IndexOf(' ');
                if (sp < 0)
                {
                    sp = trimmed.IndexOf('\t');
                }
                if (sp <= 0)
                {
                    continue;
                }
                string key = trimmed[..sp];
                string val = trimmed[(sp + 1)..].TrimEnd('\r', '\n').Trim();
                if (key.Length == 0 || val.Length == 0)
                {
                    continue;
                }
                CopyDefaultValue(key, val);
            }
        }
    }

    private void CopyDefaultValue(string key, string val)
    {
        if (key == "issue" && _issue == Issue.NoIss)
        {
            // Defaults never abort the program on a bad issue value in the C
            // (getIssue does), but a malformed default would; mirror by routing
            // through GetIssue with a throwaway writer so behaviour matches.
            _issue = GetIssue(val, TextWriter.Null);
        }
        else if (key == "templates" && _templateDir == null)
        {
            _templateDir = val;
        }
    }

    /* ----- skeleton / template loading ----- */

    private XmlDocument XmlSkeleton(string? templateDir, TextWriter stderr)
    {
        if (templateDir != null)
        {
            string src = Path.Combine(templateDir, "update.xml");
            if (!File.Exists(src))
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: No schema update in template directory \"{templateDir}\".");
                throw new ExitException(ExitBadTemplate);
            }
            return XmlUtils.ReadDoc(src);
        }

        return EmbeddedResources.LoadXml("newupf/update.xml");
    }

    private void DumpTemplate(string path, TextWriter stderr)
    {
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Cannot dump template in directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }

        try
        {
            string content = EmbeddedResources.ReadText("newupf/update.xml");
            File.WriteAllText(Path.Combine(path, "update.xml"), content, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Cannot dump template in directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }
    }

    /* ----- CIR type detection (mirror typeOfCir) ----- */

    private enum CirType
    {
        NonRepository,
        AccessPointRepository,
        ApplicRepository,
        CautionRepository,
        CircuitBreakerRepository,
        ControlIndicatorRepository,
        EnterpriseRepository,
        FunctionalItemRepository,
        PartRepository,
        SupplyRepository,
        ToolRepository,
        WarningRepository,
    }

    private static CirType TypeOfCir(XmlDocument doc)
    {
        var type = XmlUtils.XPathFirstNode(doc, null, "//content/commonRepository/*");
        return type?.Name switch
        {
            "accessPointRepository" => CirType.AccessPointRepository,
            "applicRepository" => CirType.ApplicRepository,
            "cautionRepository" => CirType.CautionRepository,
            "circuitBreakerRepository" => CirType.CircuitBreakerRepository,
            "controlIndicatorRepository" => CirType.ControlIndicatorRepository,
            "enterpriseRepository" => CirType.EnterpriseRepository,
            "functionalItemRepository" => CirType.FunctionalItemRepository,
            "partRepository" => CirType.PartRepository,
            "supplyRepository" => CirType.SupplyRepository,
            "toolRepository" => CirType.ToolRepository,
            "warningRepository" => CirType.WarningRepository,
            _ => CirType.NonRepository,
        };
    }

    /* ----- per-object identifying XPath (mirror getNodeXPath et al.) ----- */

    private static XmlElement? FirstElementChild(XmlNode node)
    {
        for (XmlNode? c = node.FirstChild; c != null; c = c.NextSibling)
        {
            if (c.NodeType == XmlNodeType.Element)
            {
                return (XmlElement)c;
            }
        }
        return null;
    }

    /// <summary>
    /// Compute the identifying XPath for a CIR object and return the node used as
    /// its "ident" (copied into delete/insert groups). Mirrors getNodeXPath; the
    /// out <paramref name="xpath"/> carries the predicate-based selector.
    /// </summary>
    private static XmlNode? GetNodeXPath(XmlElement obj, out string xpath)
    {
        if (obj.HasAttribute("id"))
        {
            xpath = $"//{obj.Name}[@id='{obj.GetAttribute("id")}']";
            return FirstElementChild(obj);
        }

        switch (obj.Name)
        {
            case "accessPointSpec":
                return XPathForBasicSpec(obj, "accessPointNumber", out xpath);
            case "applicSpec":
                return XPathForBasicSpec(obj, "applicIdentValue", out xpath);
            case "cautionSpec":
                return XPathForBasicSpec(obj, "cautionIdentNumber", out xpath);
            case "circuitBreakerSpec":
                return XPathForBasicSpec(obj, "circuitBreakerNumber", out xpath);
            case "controlIndicatorSpec":
                return XPathForControlIndicatorSpec(obj, out xpath);
            case "enterpriseSpec":
                return XPathForBasicSpec(obj, "manufacturerCodeValue", out xpath);
            case "functionalItemSpec":
                return XPathForBasicSpec(obj, "functionalItemNumber", out xpath);
            case "partSpec":
                return XPathForPartSpec(obj, out xpath);
            case "supplySpec":
                return XPathForBasicSpec(obj, "supplyNumber", out xpath);
            case "toolSpec":
                return XPathForToolSpec(obj, out xpath);
            case "warningSpec":
                return XPathForBasicSpec(obj, "warningIdentNumber", out xpath);
            default:
                xpath = string.Empty;
                return null;
        }
    }

    private static XmlNode? XPathForBasicSpec(XmlElement obj, string attr, out string xpath)
    {
        XmlElement? ident = FirstElementChild(obj);
        string value = ident?.GetAttribute(attr) ?? string.Empty;
        xpath = $"//{obj.Name}[{ident?.Name}/@{attr}='{value}']";
        return ident;
    }

    private static XmlNode? XPathForPartSpec(XmlElement obj, out string xpath)
    {
        XmlElement? ident = FirstElementChild(obj);
        string partNumberValue = ident?.GetAttribute("partNumberValue") ?? string.Empty;
        string manufacturerCodeValue = ident?.GetAttribute("manufacturerCodeValue") ?? string.Empty;
        xpath = $"//partSpec[partIdent/@partNumberValue='{partNumberValue}' and partIdent/@manufacturerCodeValue='{manufacturerCodeValue}']";
        return ident;
    }

    private static XmlNode? XPathForToolSpec(XmlElement obj, out string xpath)
    {
        XmlElement? ident = FirstElementChild(obj);
        string toolNumber = ident?.GetAttribute("toolNumber") ?? string.Empty;
        string manufacturerCodeValue = ident?.GetAttribute("manufacturerCodeValue") ?? string.Empty;
        xpath = $"//toolSpec[toolIdent/@toolNumber='{toolNumber}' and toolIdent/@manufacturerCodeValue='{manufacturerCodeValue}']";
        return ident;
    }

    private static XmlNode? XPathForControlIndicatorSpec(XmlElement obj, out string xpath)
    {
        string controlIndicatorNumber = obj.GetAttribute("controlIndicatorNumber");
        xpath = $"//controlIndicatorSpec[@controlIndicatorNumber='{controlIndicatorNumber}']";

        // Mirror the C: synthesize a controlIndicatorIdent sibling carrying the
        // number, used as the ident copied into delete groups.
        var ident = obj.OwnerDocument.CreateElement("controlIndicatorIdent");
        ident.SetAttribute("controlIndicatorNumber", controlIndicatorNumber);
        obj.ParentNode?.InsertAfter(ident, obj);
        return ident;
    }

    /* ----- object diffing (mirror deleteObjects/insertObjects/replaceObjects) ----- */

    private static XmlNodeList CirObjects(XmlDocument doc) => doc.SelectNodes(CirObjectXPath)!;

    private static bool NodeExists(XmlDocument doc, string xpath) =>
        doc.SelectSingleNode(xpath) != null;

    private static XmlElement? DeleteObjects(XmlDocument updateDoc, XmlDocument src, XmlDocument tgt)
    {
        var group = updateDoc.CreateElement("deleteObjectGroup");

        foreach (XmlNode node in CirObjects(src))
        {
            var obj = (XmlElement)node;
            XmlNode? ident = GetNodeXPath(obj, out string xpath);

            if (!NodeExists(tgt, xpath))
            {
                var deleteObject = updateDoc.CreateElement("deleteObject");
                group.AppendChild(deleteObject);
                if (ident != null)
                {
                    deleteObject.AppendChild(updateDoc.ImportNode(ident, true));
                }
            }
        }

        return group.HasChildNodes ? group : null;
    }

    private static XmlElement? InsertObjects(XmlDocument updateDoc, XmlDocument src, XmlDocument tgt)
    {
        var group = updateDoc.CreateElement("insertObjectGroup");

        foreach (XmlNode node in CirObjects(tgt))
        {
            var obj = (XmlElement)node;
            GetNodeXPath(obj, out string xpath);

            if (!NodeExists(src, xpath))
            {
                var insertObject = updateDoc.CreateElement("insertObject");
                group.AppendChild(insertObject);

                XmlElement? before = PreviousElementSibling(obj);
                XmlElement? after = NextElementSibling(obj);

                if (before != null)
                {
                    GetNodeXPath(before, out string targetPath);
                    insertObject.SetAttribute("targetPath", targetPath);
                    insertObject.SetAttribute("insertionOrder", "after");
                }
                else if (after != null)
                {
                    GetNodeXPath(after, out string targetPath);
                    insertObject.SetAttribute("targetPath", targetPath);
                    insertObject.SetAttribute("insertionOrder", "before");
                }

                insertObject.AppendChild(updateDoc.ImportNode(obj, true));
            }
        }

        return group.HasChildNodes ? group : null;
    }

    private static XmlElement? ReplaceObjects(XmlDocument updateDoc, XmlDocument src, XmlDocument tgt)
    {
        var group = updateDoc.CreateElement("replaceObjectGroup");

        foreach (XmlNode node in CirObjects(src))
        {
            var obj = (XmlElement)node;
            GetNodeXPath(obj, out string xpath);

            XmlNode? tgtNode = tgt.SelectSingleNode(xpath);
            if (tgtNode != null && !SameNodes(obj, tgtNode))
            {
                var replaceObject = updateDoc.CreateElement("replaceObject");
                group.AppendChild(replaceObject);
                replaceObject.AppendChild(updateDoc.ImportNode(tgtNode, true));
            }
        }

        return group.HasChildNodes ? group : null;
    }

    /// <summary>Compare two nodes by their serialized form (mirror sameNodes).</summary>
    private static bool SameNodes(XmlNode a, XmlNode b) =>
        string.Equals(a.OuterXml, b.OuterXml, StringComparison.Ordinal);

    private static XmlElement? PreviousElementSibling(XmlNode node)
    {
        for (XmlNode? n = node.PreviousSibling; n != null; n = n.PreviousSibling)
        {
            if (n.NodeType == XmlNodeType.Element)
            {
                return (XmlElement)n;
            }
        }
        return null;
    }

    private static XmlElement? NextElementSibling(XmlNode node)
    {
        for (XmlNode? n = node.NextSibling; n != null; n = n.NextSibling)
        {
            if (n.NodeType == XmlNodeType.Element)
            {
                return (XmlElement)n;
            }
        }
        return null;
    }

    /* ----- metadata (mirror setMetadata) ----- */

    private static void SetMetadata(XmlDocument update, XmlDocument source, XmlDocument target)
    {
        var updateAddress = (XmlElement)XmlUtils.XPathFirstNode(update, null, "//updateAddress")!;

        // updateIdent <- source//dmIdent (renamed)
        var srcDmIdent = XmlUtils.XPathFirstNode(source, null, "//dmIdent")!;
        var updateIdent = (XmlElement)updateAddress.AppendChild(update.ImportNode(srcDmIdent, true))!;
        Rename(updateIdent, "updateIdent");

        // updateIdent/dmCode -> updateCode with objectIdentCode="UPF".
        // Rename returns the replacement node; the original is detached.
        var dmCode = (XmlElement)XmlUtils.XPathFirstNode(update, null, "//updateIdent/dmCode")!;
        var updateCode = Rename(dmCode, "updateCode");
        updateCode.SetAttribute("objectIdentCode", "UPF");

        var updateStatus = (XmlElement)XmlUtils.XPathFirstNode(update, null, "//updateStatus")!;

        // updateAddress <- target//dmAddressItems/issueDate
        var tgtIssueDate = XmlUtils.XPathFirstNode(target, null, "//dmAddressItems/issueDate")!;
        updateAddress.AppendChild(update.ImportNode(tgtIssueDate, true));

        // updateStatus/sourceDmIdent <- source//dmIdent (renamed)
        var sourceDmIdent = (XmlElement)updateStatus.AppendChild(update.ImportNode(srcDmIdent, true))!;
        Rename(sourceDmIdent, "sourceDmIdent");

        // updateStatus/targetDmIssueInfo <- target//dmIdent/issueInfo (renamed)
        var tgtIssueInfo = XmlUtils.XPathFirstNode(target, null, "//dmIdent/issueInfo")!;
        var targetDmIssueInfo = (XmlElement)updateStatus.AppendChild(update.ImportNode(tgtIssueInfo, true))!;
        Rename(targetDmIssueInfo, "targetDmIssueInfo");

        AppendCopy(updateStatus, update, target, "//dmStatus/responsiblePartnerCompany");
        AppendCopy(updateStatus, update, target, "//dmStatus/originator");
        AppendCopy(updateStatus, update, target, "//dmStatus/brexDmRef");
        AppendCopy(updateStatus, update, target, "//dmStatus/qualityAssurance");

        // updateIdentAndStatusSection/targetDmStatus <- target//dmStatus (renamed)
        var updateIdentAndStatusSection =
            (XmlElement)XmlUtils.XPathFirstNode(update, null, "//updateIdentAndStatusSection")!;
        var tgtDmStatus = XmlUtils.XPathFirstNode(target, null, "//dmStatus")!;
        var targetDmStatus =
            (XmlElement)updateIdentAndStatusSection.AppendChild(update.ImportNode(tgtDmStatus, true))!;
        Rename(targetDmStatus, "targetDmStatus");
    }

    private static void AppendCopy(XmlElement parent, XmlDocument doc, XmlDocument from, string xpath)
    {
        var node = XmlUtils.XPathFirstNode(from, null, xpath);
        if (node != null)
        {
            parent.AppendChild(doc.ImportNode(node, true));
        }
    }

    /// <summary>
    /// Rename an element (mirror xmlNodeSetName). <see cref="XmlElement"/> names
    /// are immutable, so this creates a replacement element with the new name,
    /// moves the attributes and children across, swaps it into the tree and
    /// returns it. Callers must use the returned node, as the original is
    /// detached.
    /// </summary>
    private static XmlElement Rename(XmlElement el, string newName)
    {
        XmlDocument doc = el.OwnerDocument;
        var renamed = doc.CreateElement(newName);

        // Move attributes (preserving any namespaced ones, e.g. xsi:*).
        while (el.Attributes.Count > 0)
        {
            renamed.Attributes.Append(el.Attributes[0]!);
        }
        while (el.FirstChild != null)
        {
            renamed.AppendChild(el.FirstChild);
        }

        el.ParentNode!.ReplaceChild(renamed, el);
        return renamed;
    }

    /* ----- automatic filename (mirror autoName) ----- */

    private static string AutoName(XmlDocument update)
    {
        var updateCode = (XmlElement)XmlUtils.XPathFirstNode(update, null, "//updateIdent/updateCode")!;
        var issueInfo = (XmlElement)XmlUtils.XPathFirstNode(update, null, "//updateIdent/issueInfo")!;
        var language = (XmlElement)XmlUtils.XPathFirstNode(update, null, "//updateIdent/language")!;

        string modelIdentCode = updateCode.GetAttribute("modelIdentCode");
        string systemDiffCode = updateCode.GetAttribute("systemDiffCode");
        string systemCode = updateCode.GetAttribute("systemCode");
        string subSystemCode = updateCode.GetAttribute("subSystemCode");
        string subSubSystemCode = updateCode.GetAttribute("subSubSystemCode");
        string assyCode = updateCode.GetAttribute("assyCode");
        string disassyCode = updateCode.GetAttribute("disassyCode");
        string disassyCodeVariant = updateCode.GetAttribute("disassyCodeVariant");
        string infoCode = updateCode.GetAttribute("infoCode");
        string infoCodeVariant = updateCode.GetAttribute("infoCodeVariant");
        string itemLocationCode = updateCode.GetAttribute("itemLocationCode");

        string issueNumber = issueInfo.GetAttribute("issueNumber");
        string inWork = issueInfo.GetAttribute("inWork");

        string languageIsoCode = language.GetAttribute("languageIsoCode").ToUpperInvariant();
        string countryIsoCode = language.GetAttribute("countryIsoCode");

        return string.Format(
            CultureInfo.InvariantCulture,
            "UPF-{0}-{1}-{2}-{3}{4}-{5}-{6}{7}-{8}{9}-{10}_{11}-{12}_{13}-{14}.XML",
            modelIdentCode,
            systemDiffCode,
            systemCode,
            subSystemCode,
            subSubSystemCode,
            assyCode,
            disassyCode,
            disassyCodeVariant,
            infoCode,
            infoCodeVariant,
            itemLocationCode,
            issueNumber,
            inWork,
            languageIsoCode,
            countryIsoCode);
    }

    /* ----- issue down-conversion (mirror toIssue) ----- */

    private XmlDocument ToIssue(XmlDocument doc, Issue iss)
    {
        string resource = iss switch
        {
            Issue.Iss50 => "newupf/to50.xsl",
            Issue.Iss42 => "newupf/to42.xsl",
            Issue.Iss41 => "newupf/to41.xsl",
            _ => throw new ExitException(ExitBadIssue),
        };

        // Preserve the original document (DOCTYPE etc.) and only swap in the
        // transformed root element, mirroring the C which keeps the original doc
        // and replaces its root with the styled result.
        var orig = (XmlDocument)doc.CloneNode(true);

        var xslt = new XslCompiledTransform();
        var settings = new XsltSettings(enableDocumentFunction: false, enableScript: false);
        using (Stream s = EmbeddedResources.Open(resource)
                          ?? throw new FileNotFoundException(resource))
        using (var styleReader = XmlReader.Create(s))
        {
            // The to4x/to50 stylesheets are self-contained (no xsl:import or
            // document()), so resolution is never needed; use a null resolver.
            xslt.Load(styleReader, settings, XmlResolver.ThrowingResolver);
        }

        var resultDoc = XmlUtils.NewDocument();
        using (var ms = new MemoryStream())
        {
            using (var writer = XmlWriter.Create(ms, xslt.OutputSettings))
            {
                xslt.Transform(doc, writer);
            }
            ms.Position = 0;
            resultDoc.Load(ms);
        }

        XmlNode importedRoot = orig.ImportNode(resultDoc.DocumentElement!, true);
        orig.ReplaceChild(importedRoot, orig.DocumentElement!);
        return orig;
    }

    /* ----- help / version ----- */

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] <SOURCE> <TARGET>");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -$, --issue <issue>         Specify which S1000D issue to use.");
        stdout.WriteLine("  -@, --out <path>            Output to specified file or directory.");
        stdout.WriteLine("  -%, --templates <dir>       Use templates in specified directory.");
        stdout.WriteLine("  -~, --dump-templates <dir>  Dump built-in template to directory.");
        stdout.WriteLine("  -d, --defaults <file>       Specify the .defaults file name.");
        stdout.WriteLine("  -f, --overwrite             Overwrite existing file.");
        stdout.WriteLine("  -h, -?, --help              Show help/usage message.");
        stdout.WriteLine("  -q, --quiet                 Don't report an error if file exists.");
        stdout.WriteLine("  -v, --verbose               Print file name of new update file.");
        stdout.WriteLine("      --version               Show version information.");
    }
}
