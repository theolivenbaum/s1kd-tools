using System.Globalization;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-fmgen</c>: generate front matter contents for a data module
/// from a publication module (PM).
///
/// Front matter data modules (title page, table of contents, highlights, list of
/// effective data modules, etc.) derive their content from the structure of the
/// PM that references them. This tool applies a per-front-matter-type XSLT 1.0
/// stylesheet to the PM and merges the result back into the FM data module,
/// replacing the element whose name matches the transformation's root element
/// (typically <c>&lt;content&gt;</c>).
///
/// The built-in stylesheets are copied verbatim from the C tool into
/// <c>Resources/fmgen/*.xsl</c> and applied with <see cref="XslCompiledTransform"/>.
/// They are plain XSLT 1.0 (using only <c>generate-id</c>, <c>key</c>, <c>sort</c>
/// — all natively supported), so no EXSLT shim is required. The C tool also
/// supports XProc (<c>p:pipeline</c>) stylesheets for the <c>-x</c>/per-type
/// <c>xsl</c> override; that path is not ported (see remarks below).
/// </summary>
/// <remarks>
/// Deviations from the C source:
/// <list type="bullet">
///   <item>XProc pipeline stylesheets (the <c>http://www.w3.org/ns/xproc</c>
///         <c>&lt;p:pipeline&gt;</c> form, used by the multi-pass example) are not
///         supported. Plain XSLT stylesheets supplied via <c>-x</c> or a
///         <c>.fmtypes</c> <c>xsl</c> attribute work.</item>
///   <item>The <c>type</c> XSLT parameter that the C passes to every stylesheet
///         is set, but none of the built-in stylesheets actually reference it.</item>
/// </list>
/// </remarks>
public sealed class FmGenTool : ITool
{
    public string Name => "fmgen";

    public string Description => "Generate front matter content for a data module from a publication module.";

    // Mirrors VERSION in reference/tools/s1kd-fmgen/s1kd-fmgen.c.
    public string Version => "4.0.0";

    // Exit codes (mirror the EXIT_* defines in the C source).
    private const int ExitSuccess = 0;
    private const int ExitBadDate = 1;
    private const int ExitNoType = 2;
    private const int ExitBadType = 3;
    private const int ExitMerge = 4;
    private const int ExitBadStylesheet = 5;
    private const int ExitGenerateErr = 6;
    private const int ExitBadPm = 7;

    private const string ToolPrefix = "s1kd-fmgen";
    private const string ErrPrefix = ToolPrefix + ": ERROR: ";
    private const string InfPrefix = ToolPrefix + ": INFO: ";

    private enum Verbosity { Quiet, Normal, Verbose, Debug }

    /// <summary>The built-in front matter types and the stylesheet that generates each.</summary>
    private static readonly Dictionary<string, string> BuiltinXsl = new(StringComparer.Ordinal)
    {
        ["TITLE"] = "fmgen/tp.xsl",
        ["TOC"] = "fmgen/toc.xsl",
        ["HIGH"] = "fmgen/high.xsl",
        ["LOEDM"] = "fmgen/loedm.xsl",
        ["LOA"] = "fmgen/loa.xsl",
        ["LOASD"] = "fmgen/loasd.xsl",
        ["LOI"] = "fmgen/loi.xsl",
        ["LOS"] = "fmgen/los.xsl",
        ["LOT"] = "fmgen/lot.xsl",
        ["LOTBL"] = "fmgen/lotbl.xsl",
    };

    /// <summary>
    /// Front matter types whose generation should ignore "deleted" objects and
    /// elements by default. Mirrors <c>default_ignore_del</c>.
    /// </summary>
    private static bool DefaultIgnoreDel(string type) => type switch
    {
        "LOA" or "LOASD" or "LOEDM" or "LOI" or "LOS" or "LOT" or "LOTBL" or "TOC" or "TITLE" => true,
        _ => false,
    };

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var verbosity = Verbosity.Normal;
        string? pmpath = null;
        string? fmtype = null;
        string? fmtypesPath = null;
        bool overwrite = false;
        bool islist = false;
        string? xslpath = null;
        string? issdate = null;
        var paramNames = new List<string>();
        var paramValues = new List<string>();
        var files = new List<string>();

        // The C tool returns immediately for the dump options; honour the same
        // first-match-wins ordering.
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];

            string? TakeArg(string flag)
            {
                if (++i >= args.Count)
                {
                    if (verbosity >= Verbosity.Normal)
                    {
                        stderr.WriteLine($"{ErrPrefix}{flag} requires an argument");
                    }
                    return null;
                }
                return args[i];
            }

            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return ExitSuccess;
                case "--version":
                    ShowVersion(stdout);
                    return ExitSuccess;
                case "-," or "--dump-fmtypes-xml":
                    DumpFmtypesXml(stdout);
                    return ExitSuccess;
                case "-." or "--dump-fmtypes":
                    stdout.Write(EmbeddedResources.ReadText("fmgen/fmtypes.txt"));
                    return ExitSuccess;
                case "-D" or "--dump-xsl":
                {
                    string? type = TakeArg(a);
                    if (type == null)
                    {
                        return ExitNoType;
                    }
                    return DumpBuiltinXsl(type, stdout, stderr, verbosity);
                }
                case "-F" or "--fmtypes":
                {
                    string? v = TakeArg(a);
                    if (v == null)
                    {
                        return ExitNoType;
                    }
                    fmtypesPath ??= v;
                    break;
                }
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-I" or "--date":
                {
                    string? v = TakeArg(a);
                    if (v == null)
                    {
                        return ExitNoType;
                    }
                    issdate = v;
                    break;
                }
                case "-l" or "--list":
                    islist = true;
                    break;
                case "-P" or "--pm":
                {
                    string? v = TakeArg(a);
                    if (v == null)
                    {
                        return ExitNoType;
                    }
                    pmpath = v;
                    break;
                }
                case "-p" or "--param":
                {
                    string? v = TakeArg(a);
                    if (v == null)
                    {
                        return ExitNoType;
                    }
                    int eq = v.IndexOf('=');
                    string name = eq < 0 ? v : v[..eq];
                    string val = eq < 0 ? string.Empty : v[(eq + 1)..];
                    paramNames.Add(name);
                    paramValues.Add(val);
                    break;
                }
                case "-q" or "--quiet":
                    if (verbosity > Verbosity.Quiet)
                    {
                        verbosity--;
                    }
                    break;
                case "-t" or "--type":
                {
                    string? v = TakeArg(a);
                    if (v == null)
                    {
                        return ExitNoType;
                    }
                    fmtype = v;
                    break;
                }
                case "-v" or "--verbose":
                    if (verbosity < Verbosity.Debug)
                    {
                        verbosity++;
                    }
                    break;
                case "-x" or "--xsl":
                {
                    string? v = TakeArg(a);
                    if (v == null)
                    {
                        return ExitNoType;
                    }
                    xslpath = v;
                    break;
                }
                default:
                    if (a.Length > 1 && a[0] == '-' && a != "-")
                    {
                        if (verbosity >= Verbosity.Normal)
                        {
                            stderr.WriteLine($"{ErrPrefix}Unknown option: {a}");
                        }
                        return ExitNoType;
                    }
                    files.Add(a);
                    break;
            }
        }

        // Build the parameter list passed to each transformation. The C always
        // seeds a "type" placeholder first; we keep it for parity.
        var xsltParams = new XsltArgumentList();
        var paramSet = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < paramNames.Count; i++)
        {
            if (paramSet.Add(paramNames[i]))
            {
                xsltParams.AddParam(paramNames[i], string.Empty, paramValues[i]);
            }
        }

        // Resolve the .fmtypes table: explicit -F, then a discovered config file,
        // then the built-in default.
        XmlDocument fmtypes;
        if (fmtypesPath != null)
        {
            fmtypes = ReadFmtypes(fmtypesPath);
        }
        else if (Csdb.FindConfig(Csdb.FmTypesFileName, out string cfg))
        {
            fmtypes = ReadFmtypes(cfg);
        }
        else
        {
            fmtypes = EmbeddedResources.LoadXml("fmgen/fmtypes.xml");
        }

        // Read the PM (defaults to stdin).
        pmpath ??= "-";
        XmlDocument pm;
        try
        {
            pm = pmpath == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(pmpath);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{ErrPrefix}Error reading PM {pmpath}");
            return ExitBadPm;
        }

        if (files.Count > 0)
        {
            foreach (string file in files)
            {
                int code = islist
                    ? GenerateForList(pm, file, fmtypes, fmtype, overwrite, xslpath, xsltParams, paramSet, issdate, verbosity, stdout, stderr)
                    : GenerateForDm(pm, file, fmtypes, fmtype, overwrite, xslpath, xsltParams, paramSet, issdate, verbosity, stdout, stderr);
                if (code != ExitSuccess)
                {
                    return code;
                }
            }
        }
        else if (fmtype != null)
        {
            // No DMs: just emit the generated front matter to stdout.
            XmlDocument? res = GenerateForType(pm, fmtype, null, xslpath, xsltParams, paramSet,
                DefaultIgnoreDel(fmtype), out int code, stderr, verbosity);
            if (res == null)
            {
                return code;
            }
            stdout.Write(XmlUtils.ToXmlString(res));
            stdout.Write('\n');
        }
        else if (islist)
        {
            int code = GenerateForList(pm, null, fmtypes, fmtype, overwrite, xslpath, xsltParams, paramSet, issdate, verbosity, stdout, stderr);
            if (code != ExitSuccess)
            {
                return code;
            }
        }
        else
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}No FM type specified.");
            }
            return ExitNoType;
        }

        return ExitSuccess;
    }

    private int GenerateForList(XmlDocument pm, string? path, XmlDocument fmtypes, string? fmtype,
        bool overwrite, string? xslpath, XsltArgumentList xsltParams, HashSet<string> paramSet,
        string? issdate, Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        TextReader reader;
        bool close;
        if (path != null)
        {
            try
            {
                reader = new StreamReader(File.OpenRead(path));
                close = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{ErrPrefix}Could not read list: {path}");
                }
                return ExitSuccess;
            }
        }
        else
        {
            reader = new StreamReader(Console.OpenStandardInput());
            close = false;
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string fname = line.Split('\t', '\r', '\n')[0];
                if (fname.Length == 0)
                {
                    continue;
                }
                int code = GenerateForDm(pm, fname, fmtypes, fmtype, overwrite, xslpath,
                    xsltParams, paramSet, issdate, verbosity, stdout, stderr);
                if (code != ExitSuccess)
                {
                    return code;
                }
            }
        }
        finally
        {
            if (close)
            {
                reader.Dispose();
            }
        }

        return ExitSuccess;
    }

    private int GenerateForDm(XmlDocument pm, string dmpath, XmlDocument fmtypes, string? fmtype,
        bool overwrite, string? xslpath, XsltArgumentList xsltParams, HashSet<string> paramSet,
        string? issdate, Verbosity verbosity, TextWriter stdout, TextWriter stderr)
    {
        XmlDocument doc;
        try
        {
            doc = XmlUtils.ReadDoc(dmpath);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            return ExitSuccess; // C returns silently when the DM cannot be read.
        }

        string? type;
        string? fmxsl;
        bool ignoreDel;

        if (fmtype != null)
        {
            type = fmtype;
            fmxsl = null;
            ignoreDel = DefaultIgnoreDel(type);
        }
        else
        {
            string incode = XmlUtils.XPathFirstValue(doc, null, "//@infoCode|//incode") ?? string.Empty;
            string incodev = XmlUtils.XPathFirstValue(doc, null, "//@infoCodeVariant|//incodev") ?? string.Empty;

            XmlElement? fm = FindFm(fmtypes, incode, incodev);
            if (fm != null)
            {
                type = fm.GetAttribute("type");
                fmxsl = fm.HasAttribute("xsl") ? fm.GetAttribute("xsl") : null;
                if (string.IsNullOrEmpty(fmxsl))
                {
                    fmxsl = null;
                }

                if (fm.HasAttribute("ignoreDel"))
                {
                    ignoreDel = fm.GetAttribute("ignoreDel") == "yes";
                }
                else
                {
                    ignoreDel = DefaultIgnoreDel(type);
                }
            }
            else
            {
                if (verbosity >= Verbosity.Debug)
                {
                    stderr.WriteLine($"{InfPrefix}Skipping {dmpath} as no FM type is associated with info code: {incode}{incodev}");
                }
                return ExitSuccess;
            }
        }

        if (string.IsNullOrEmpty(type))
        {
            return ExitSuccess;
        }

        if (verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"{InfPrefix}Generating FM content for {dmpath} ({type})...");
        }

        XmlDocument? res = GenerateForType(pm, type, fmxsl, xslpath, xsltParams, paramSet,
            ignoreDel, out int genCode, stderr, verbosity);
        if (res == null)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Failed to update {dmpath}: Transformation using {xslpath ?? fmxsl} failed.");
            }
            return genCode;
        }

        if (type == "TITLE")
        {
            CopyTpElems(res, doc);
        }

        // Merge: replace the DM element whose name matches the transformation
        // root element (typically <content>).
        XmlElement? root = res.DocumentElement;
        if (root == null)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Failed to update {dmpath}: no front matter contents generated.");
            }
            return ExitMerge;
        }

        XmlNode? target = doc.SelectSingleNode($"//*[name()='{root.Name}']");
        if (target == null || target.ParentNode == null)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Failed to update {dmpath}: no <{root.Name}> element to merge on.");
            }
            return ExitMerge;
        }

        XmlNode imported = doc.ImportNode(root, true);
        target.ParentNode.ReplaceChild(imported, target);

        if (issdate != null)
        {
            if (!SetIssueDate(doc, pm, issdate))
            {
                if (verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{ErrPrefix}Bad date: {issdate}");
                }
                return ExitBadDate;
            }
        }

        if (overwrite)
        {
            XmlUtils.SaveDoc(doc, dmpath);
        }
        else
        {
            stdout.Write(XmlUtils.ToXmlString(doc));
            stdout.Write('\n');
        }

        return ExitSuccess;
    }

    /// <summary>
    /// Generate front matter contents of a given type by applying the appropriate
    /// stylesheet to the PM. Mirrors <c>generate_fm_content_for_type</c>. Returns
    /// null and sets <paramref name="code"/> on failure.
    /// </summary>
    private XmlDocument? GenerateForType(XmlDocument pm, string type, string? fmxsl, string? xslpath,
        XsltArgumentList xsltParams, HashSet<string> paramSet, bool ignoreDel, out int code,
        TextWriter stderr, Verbosity verbosity)
    {
        code = ExitSuccess;

        // Apply the "type" parameter the C supplies (placeholder), if not already set.
        // (None of the built-in stylesheets reference it, but we keep parity.)
        XsltArgumentList args = CloneParams(xsltParams, paramSet, "type", type);

        XmlDocument src;
        if (ignoreDel)
        {
            src = (XmlDocument)pm.Clone();
            RemoveDeletedDmodules(src);
            XmlUtils.RemoveDeleteElements(src);
        }
        else
        {
            src = pm;
        }

        try
        {
            if (xslpath != null)
            {
                return TransformWithFile(src, xslpath, args);
            }
            if (fmxsl != null)
            {
                return TransformWithFile(src, fmxsl, args);
            }
            if (!BuiltinXsl.TryGetValue(type, out string? resource))
            {
                if (verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{ErrPrefix}Unknown front matter type: {type}");
                }
                code = ExitBadType;
                return null;
            }
            return TransformWithResource(src, resource, args);
        }
        catch (FileNotFoundException)
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Failed to parse stylesheet {xslpath ?? fmxsl}");
            }
            code = ExitBadStylesheet;
            return null;
        }
        catch (Exception ex) when (ex is XmlException or XsltException or IOException)
        {
            code = ExitGenerateErr;
            return null;
        }
    }

    private static XsltArgumentList CloneParams(XsltArgumentList source, HashSet<string> names, string extraName, string extraValue)
    {
        var clone = new XsltArgumentList();
        foreach (string name in names)
        {
            object? v = source.GetParam(name, string.Empty);
            if (v != null)
            {
                clone.AddParam(name, string.Empty, v);
            }
        }
        if (!names.Contains(extraName))
        {
            clone.AddParam(extraName, string.Empty, extraValue);
        }
        return clone;
    }

    private XmlElement? FindFm(XmlDocument fmtypes, string incode, string incodev)
    {
        string concat = incode + incodev;
        foreach (XmlElement fm in fmtypes.SelectNodes("//fm")!.OfType<XmlElement>())
        {
            string ic = fm.GetAttribute("infoCode");
            if (ic.Length > 0 && concat.StartsWith(ic, StringComparison.Ordinal))
            {
                return fm;
            }
        }
        return null;
    }

    /// <summary>Remove "deleted" data modules from a flattened PM. Mirrors <c>rem_deleted_dmodules</c>.</summary>
    private static void RemoveDeletedDmodules(XmlDocument doc)
    {
        XmlNodeList? nodes = doc.SelectNodes(
            "//dmodule[identAndStatusSection/dmStatus/@issueType='deleted' or status/issno/@isstype='deleted']");
        if (nodes == null)
        {
            return;
        }
        foreach (XmlNode node in nodes.OfType<XmlNode>().ToList())
        {
            node.ParentNode?.RemoveChild(node);
        }
    }

    /// <summary>
    /// Copy elements from the source TITLE DM into the generated title page that
    /// can't be derived from the PM. Mirrors <c>copy_tp_elems</c>.
    /// </summary>
    private static void CopyTpElems(XmlDocument res, XmlDocument doc)
    {
        XmlNode? fmtp = res.SelectSingleNode("//frontMatterTitlePage");
        if (fmtp == null)
        {
            return;
        }

        void InsertBefore(string srcXpath, string anchorXpath)
        {
            XmlNode? node = doc.SelectSingleNode(srcXpath);
            XmlNode? anchor = fmtp.SelectSingleNode(anchorXpath);
            if (node != null && anchor?.ParentNode != null)
            {
                anchor.ParentNode.InsertBefore(res.ImportNode(node, true), anchor);
            }
        }

        void InsertAfter(string srcXpath, string anchorXpath)
        {
            XmlNode? node = doc.SelectSingleNode(srcXpath);
            XmlNode? anchor = fmtp.SelectSingleNode(anchorXpath);
            if (node != null && anchor?.ParentNode != null)
            {
                anchor.ParentNode.InsertAfter(res.ImportNode(node, true), anchor);
            }
        }

        InsertBefore("//productIntroName", "pmTitle");
        InsertAfter("//externalPubCode", "issueDate");
        InsertAfter("//productAndModel", "(issueDate|externalPubCode)[last()]");
        InsertAfter("//productIllustration", "(security|derivativeClassification|dataRestrictions)[last()]");
        InsertAfter("//enterpriseSpec", "(security|derivativeClassification|dataRestrictions|productIllustration)[last()]");
        InsertAfter("//enterpriseLogo", "(security|derivativeClassification|dataRestrictions|productIllustration|enterpriseSpec)[last()]");
        InsertAfter("//barCode", "(responsiblePartnerCompany|publisherLogo)[last()]");
        InsertAfter("//frontMatterInfo", "(responsiblePartnerCompany|publisherLogo|barCode)[last()]");
    }

    /// <summary>Set the issue date of the generated front matter. Mirrors <c>set_issue_date</c>.</summary>
    private static bool SetIssueDate(XmlDocument doc, XmlDocument pm, string issdate)
    {
        if (doc.SelectSingleNode("//issueDate|//issdate") is not XmlElement dmIssueDate)
        {
            return true;
        }

        string year, month, day;
        if (string.Equals(issdate, "pm", StringComparison.OrdinalIgnoreCase))
        {
            if (pm.SelectSingleNode("//issueDate|//issdate") is not XmlElement pmIssueDate)
            {
                return true;
            }
            year = pmIssueDate.GetAttribute("year");
            month = pmIssueDate.GetAttribute("month");
            day = pmIssueDate.GetAttribute("day");
        }
        else if (issdate == "-")
        {
            DateTime now = DateTime.Now;
            year = now.Year.ToString("D4", CultureInfo.InvariantCulture);
            month = now.Month.ToString("D2", CultureInfo.InvariantCulture);
            day = now.Day.ToString("D2", CultureInfo.InvariantCulture);
        }
        else
        {
            string[] parts = issdate.Split('-');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
            {
                return false;
            }
            year = parts[0];
            month = parts[1];
            day = parts[2];
        }

        dmIssueDate.SetAttribute("year", year);
        dmIssueDate.SetAttribute("month", month);
        dmIssueDate.SetAttribute("day", day);
        return true;
    }

    private XmlDocument TransformWithResource(XmlDocument doc, string resource, XsltArgumentList args)
    {
        using Stream styleStream = EmbeddedResources.Open(resource)
            ?? throw new FileNotFoundException($"Embedded stylesheet not found: {resource}");
        return Transform(doc, reader => LoadStylesheet(reader), styleStream, args);
    }

    private XmlDocument TransformWithFile(XmlDocument doc, string path, XsltArgumentList args)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(path);
        }
        using Stream styleStream = File.OpenRead(path);
        return Transform(doc, reader => LoadStylesheet(reader), styleStream, args);
    }

    private static XslCompiledTransform LoadStylesheet(XmlReader reader)
    {
        var xslt = new XslCompiledTransform();
        xslt.Load(reader);
        return xslt;
    }

    private static XmlDocument Transform(XmlDocument doc, Func<XmlReader, XslCompiledTransform> load,
        Stream styleStream, XsltArgumentList args)
    {
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };

        XslCompiledTransform xslt;
        using (XmlReader styleReader = XmlReader.Create(styleStream, readerSettings))
        {
            xslt = load(styleReader);
        }

        var output = XmlUtils.NewDocument();
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
            output.Load(resultReader);
        }

        return output;
    }

    /// <summary>
    /// Read a .fmtypes table. Supports both the XML form and the simple
    /// whitespace-delimited text form. Mirrors <c>read_fmtypes</c> (the
    /// <c>fix_fmxsl_paths</c> URI rewriting is applied for relative xsl paths).
    /// </summary>
    private static XmlDocument ReadFmtypes(string path)
    {
        XmlDocument doc;
        try
        {
            doc = XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is XmlException or IOException)
        {
            // Fall back to the simple text format: "<incode> <type> [<xsl>]".
            doc = XmlUtils.NewDocument();
            XmlElement root = doc.CreateElement("fmtypes");
            doc.AppendChild(root);

            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.TrimEnd('\r', '\n');
                if (line.Length == 0)
                {
                    continue;
                }
                string[] parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }
                XmlElement fm = doc.CreateElement("fm");
                fm.SetAttribute("infoCode", parts[0]);
                fm.SetAttribute("type", parts[1]);
                if (parts.Length == 3)
                {
                    fm.SetAttribute("xsl", parts[2]);
                }
                root.AppendChild(fm);
            }
        }

        FixFmxslPaths(doc, path);
        return doc;
    }

    /// <summary>Resolve relative <c>xsl</c> attribute paths against the .fmtypes location.</summary>
    private static void FixFmxslPaths(XmlDocument doc, string fmtypesPath)
    {
        string? baseDir = Path.GetDirectoryName(Path.GetFullPath(fmtypesPath));
        if (baseDir == null)
        {
            return;
        }
        foreach (XmlElement fm in doc.SelectNodes("//*[@xsl]")!.OfType<XmlElement>())
        {
            string xsl = fm.GetAttribute("xsl");
            if (xsl.Length == 0 || Path.IsPathRooted(xsl))
            {
                continue;
            }
            fm.SetAttribute("xsl", Path.GetFullPath(Path.Combine(baseDir, xsl)));
        }
    }

    private int DumpBuiltinXsl(string type, TextWriter stdout, TextWriter stderr, Verbosity verbosity)
    {
        if (!BuiltinXsl.TryGetValue(type, out string? resource))
        {
            if (verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{ErrPrefix}Unknown front matter type: {type}");
            }
            return ExitBadType;
        }
        stdout.Write(EmbeddedResources.ReadText(resource));
        return ExitSuccess;
    }

    private static void DumpFmtypesXml(TextWriter stdout)
    {
        XmlDocument doc = EmbeddedResources.LoadXml("fmgen/fmtypes.xml");
        stdout.Write(XmlUtils.ToXmlString(doc));
        stdout.Write('\n');
    }

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"{ToolPrefix} (s1kd-tools) {Version}");
    }

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: {ToolPrefix} [-D <TYPE>] [-F <FMTYPES>] [-I <date>] [-P <PM>] [-p <name>=<val> ...] [-t <TYPE>] [-x <XSL>] [-,flqvh?] [<DM>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -,, --dump-fmtypes-xml      Dump the built-in .fmtypes file in XML format.");
        stdout.WriteLine("  -., --dump-fmtypes          Dump the built-in .fmtypes file in simple text format.");
        stdout.WriteLine("  -D, --dump-xsl <TYPE>       Dump the built-in XSLT for a type of front matter.");
        stdout.WriteLine("  -F, --fmtypes <FMTYPES>     Specify .fmtypes file.");
        stdout.WriteLine("  -f, --overwrite             Overwrite input data modules.");
        stdout.WriteLine("  -h, -?, --help              Show usage message.");
        stdout.WriteLine("  -I, --date <date>           Set the issue date of the generated front matter.");
        stdout.WriteLine("  -l, --list                  Treat input as list of data modules.");
        stdout.WriteLine("  -P, --pm <PM>               Generate front matter from the specified PM.");
        stdout.WriteLine("  -p, --param <name>=<value>  Pass parameters to the XSLT used to generate the front matter.");
        stdout.WriteLine("  -q, --quiet                 Quiet mode.");
        stdout.WriteLine("  -t, --type <TYPE>           Generate the specified type of front matter.");
        stdout.WriteLine("  -v, --verbose               Verbose output.");
        stdout.WriteLine("  -x, --xsl <XSL>             Override built-in or user-configured XSLT.");
        stdout.WriteLine("  --version                   Show version information.");
        stdout.WriteLine("  <DM>                        Generate front matter content based on the specified data modules.");
    }
}
