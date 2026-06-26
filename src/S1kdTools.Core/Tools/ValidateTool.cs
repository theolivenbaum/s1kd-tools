using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-validate</c>: validate CSDB objects against their S1000D
/// schema, perform XML well-formedness checks and the hardcoded IDREF/IDREFS
/// integrity checks that libxml2 (and System.Xml) do not perform during schema
/// validation.
///
/// <para>
/// Schema (XSD) validation requires the schema referenced by
/// <c>xsi:noNamespaceSchemaLocation</c> (or the one given with <c>-s</c>) to be
/// resolvable. S1000D objects normally reference their schema via an absolute
/// http(s) URL; when that schema cannot be loaded (e.g. offline, or the schema
/// directory is not available) this port still runs the well-formedness and
/// IDREF/IDREFS checks and emits a "schema unavailable" note rather than
/// failing hard. When the schema resolves to a local file (or a local schema
/// directory is supplied via <c>-s</c>) full XSD validation is performed using
/// <see cref="XmlSchemaSet"/>.
/// </para>
///
/// <para>
/// The <c>-T/--summary</c> statistics output applies the original
/// <c>stats.xsl</c> stylesheet (a plain XSLT 1.0 transform, no EXSLT) to the XML
/// report via <see cref="XslCompiledTransform"/>, matching the C tool's
/// <c>print_stats</c> output byte-for-byte (written to stderr). The
/// libxml2-specific <c>--xml-catalog</c>/long parse options remain unported
/// (tracked in todo.md).
/// </para>
/// </summary>
public sealed class ValidateTool : ITool
{
    public string Name => "validate";
    public string Description => "Validate S1000D CSDB objects against their schema.";
    public string Version => "4.3.3";

    private const string XsiUri = "http://www.w3.org/2001/XMLSchema-instance";

    // Exit codes (mirror the C #defines).
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitMaxSchemas = 2;
    private const int ExitMissingSchema = 3;

    private enum Verbosity { Silent, Normal, Verbose }

    private enum ShowFnames { None, Invalid, Valid }

    /// <summary>
    /// Attributes of type xs:IDREF that must reference an existing @id.
    /// Mirrors INVALID_ID_XPATH from the C source (the per-attribute names).
    /// </summary>
    private static readonly string[] IdrefAttributes =
    {
        "applicMapRefId",
        "applicRefId",
        "condRefId",
        "condTypeRefId",
        "conditionidref",
        "condtyperef",
        "dependencyTest",
        "derivativeClassificationRefId",
        "internalRefId",
        "nextActionRefId",
        "refapplic",
        "refid",
        "xrefid",
    };

    /// <summary>
    /// Attributes of type xs:IDREFS (space-separated lists of @id references).
    /// Mirrors INVALID_IDS_XPATH from the C source.
    /// </summary>
    private static readonly string[] IdrefsAttributes =
    {
        "applicRefIds",
        "cautionRefs",
        "controlAuthorityRefs",
        "reasonForUpdateRefIds",
        "warningRefs",
    };

    /// <summary>XPath matching all invalid IDREF attributes (INVALID_ID_XPATH).</summary>
    private static readonly string InvalidIdXPath =
        string.Join("|", IdrefAttributes.Select(a => $"//@{a}[not(//@id=.)]"));

    /// <summary>XPath matching all IDREFS attributes to check (INVALID_IDS_XPATH).</summary>
    private static readonly string InvalidIdsXPath =
        string.Join("|", IdrefsAttributes.Select(a => $"//@{a}"));

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var verbosity = Verbosity.Normal;
        var showFnames = ShowFnames.None;
        bool isList = false;
        bool ignoreEmpty = false;
        bool remDel = false;
        bool outputTree = false;
        bool xml = false;
        bool showStats = false;
        bool deepCopyNodes = false;
        string? schema = null;
        var ignoreNs = new List<string>();
        var files = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return ExitSuccess;
                case "--version":
                    stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
                    return ExitSuccess;
                case "-q" or "--quiet":
                    verbosity = Verbosity.Silent;
                    break;
                case "-v" or "--verbose":
                    verbosity = Verbosity.Verbose;
                    break;
                case "-X" or "--exclude":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: {a} requires an argument"); return ExitFailure; }
                    ignoreNs.Add(args[i]);
                    break;
                case "-F" or "--valid-filenames":
                    showFnames = ShowFnames.Valid;
                    break;
                case "-f" or "--filenames":
                    showFnames = ShowFnames.Invalid;
                    break;
                case "-l" or "--list":
                    isList = true;
                    break;
                case "-o" or "--output-valid":
                    outputTree = true;
                    break;
                case "-e" or "--ignore-empty":
                    ignoreEmpty = true;
                    break;
                case "-s" or "--schema":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: {a} requires an argument"); return ExitFailure; }
                    schema = args[i];
                    break;
                case "-^" or "--remove-deleted":
                    remDel = true;
                    break;
                case "-T" or "--summary":
                    showStats = true;
                    break;
                case "-8" or "--deep-copy-nodes":
                    deepCopyNodes = true;
                    // C falls through to also set xml = 1.
                    xml = true;
                    break;
                case "-x" or "--xml":
                    xml = true;
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return ExitFailure;
                    }
                    files.Add(a);
                    break;
            }
        }

        XmlDocument? report = null;
        XmlElement? reportRoot = null;
        if (xml || showStats)
        {
            report = new XmlDocument();
            reportRoot = report.CreateElement("s1kdValidateReport");
            report.AppendChild(reportRoot);
        }

        var ctx = new Context(verbosity, showFnames, ignoreEmpty, remDel, outputTree,
            deepCopyNodes, schema, ignoreNs, report, reportRoot, stdout, stderr);

        int err = 0;
        if (files.Count > 0)
        {
            foreach (string f in files)
            {
                err += isList
                    ? ValidateFileList(f, ctx)
                    : ValidateFile(f, ctx);
            }
        }
        else if (isList)
        {
            err += ValidateFileList(null, ctx);
        }
        else
        {
            err += ValidateFile("-", ctx);
        }

        if (xml && report != null)
        {
            stdout.Write(ReportToString(report));
        }

        if (showStats && report != null)
        {
            PrintStats(report, stderr);
        }

        return err != 0 ? ExitFailure : ExitSuccess;
    }

    private sealed record Context(
        Verbosity Verbosity,
        ShowFnames ShowFnames,
        bool IgnoreEmpty,
        bool RemDel,
        bool OutputTree,
        bool DeepCopyNodes,
        string? Schema,
        List<string> IgnoreNs,
        XmlDocument? Report,
        XmlElement? ReportRoot,
        TextWriter Stdout,
        TextWriter Stderr);

    private int ValidateFileList(string? fname, Context ctx)
    {
        TextReader reader;
        bool dispose = false;
        if (fname != null)
        {
            try
            {
                reader = new StreamReader(fname);
                dispose = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ctx.Stderr.WriteLine($"{Name}: ERROR: Could not read list file: {fname}");
                return 0;
            }
        }
        else
        {
            reader = Console.In;
        }

        int err = 0;
        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string path = line.Split('\t')[0].TrimEnd('\r', '\n');
                if (path.Length == 0)
                {
                    continue;
                }
                err += ValidateFile(path, ctx);
            }
        }
        finally
        {
            if (dispose)
            {
                reader.Dispose();
            }
        }

        return err;
    }

    private int ValidateFile(string fname, Context ctx)
    {
        XmlDocument doc;
        string? source = null;
        try
        {
            if (fname == "-")
            {
                using var stdin = new StreamReader(Console.OpenStandardInput());
                source = stdin.ReadToEnd();
            }
            else
            {
                source = File.ReadAllText(fname);
            }
            doc = XmlUtils.ReadMem(source);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            // read_xml_doc returns NULL -> validate_file returns !ignore_empty.
            if (!ctx.IgnoreEmpty)
            {
                if (ctx.Verbosity > Verbosity.Silent)
                {
                    ctx.Stderr.WriteLine($"{Name}: ERROR: {fname}: {ex.Message}");
                }
                if (ctx.Report != null)
                {
                    XmlElement rn = AddDocumentNode(ctx, fname);
                    AddXmlReportError(ctx, rn, null, GetLine(ex), StripNewline(ex.Message));
                }
            }
            return ctx.IgnoreEmpty ? 0 : 1;
        }

        // Keep a copy of the original tree before extra processing (for -o).
        // Build the source line map from the original parse, before any
        // structural modification (strip-ns / remove-deleted only remove nodes,
        // so surviving elements keep their mapped line — matching libxml2, which
        // preserves the line numbers from the original parse).
        LineInfo lineInfo = source != null
            ? LineInfo.Build(doc, source)
            : LineInfo.BuildFromFile(doc, fname);

        XmlDocument? validTree = null;
        if (ctx.OutputTree)
        {
            validTree = (XmlDocument)doc.Clone();
        }

        // Exclude namespaces (-X).
        foreach (string uri in ctx.IgnoreNs)
        {
            StripNs(doc, uri);
        }

        // Remove elements marked "delete" (-^).
        if (ctx.RemDel)
        {
            XmlUtils.RemoveDeleteElements(doc);
        }

        XmlElement? reportNode = null;
        if (ctx.Report != null)
        {
            reportNode = AddDocumentNode(ctx, fname);
        }

        int err = 0;

        // ID / IDREF / IDREFS checks (libxml2 / System.Xml do not do these).
        err += CheckIdrefs(doc, fname, ctx, reportNode, lineInfo);

        // Schema validation.
        XmlElement? root = doc.DocumentElement;
        string? url = ctx.Schema
            ?? root?.GetAttribute("noNamespaceSchemaLocation", XsiUri);
        if (string.IsNullOrEmpty(url))
        {
            url = null;
        }

        string schemaLabel = url ?? "(none)";

        if (url == null)
        {
            if (ctx.Verbosity > Verbosity.Silent)
            {
                ctx.Stderr.WriteLine($"{Name}: ERROR: {fname} has no schema.");
            }
            // C returns 1 here directly (does not free, but counts as error).
            return 1;
        }

        var schemaSet = ResolveSchema(url, out string? schemaError);
        if (schemaSet == null)
        {
            // Schema could not be loaded. Offline-graceful behaviour: note it,
            // keep the IDREF result, do not hard-fail the run.
            if (ctx.Verbosity > Verbosity.Silent)
            {
                ctx.Stderr.WriteLine(
                    $"{Name}: WARNING: {fname}: schema unavailable ({schemaLabel}): {schemaError}");
            }
            if (reportNode != null)
            {
                XmlElement note = ctx.Report!.CreateElement("schemaUnavailable");
                note.SetAttribute("schema", schemaLabel);
                if (schemaError != null)
                {
                    note.SetAttribute("reason", schemaError);
                }
                reportNode.AppendChild(note);
            }
        }
        else
        {
            err += SchemaValidate(doc, schemaSet, ctx, reportNode);
        }

        // Output the original tree if valid (-o).
        if (ctx.OutputTree && validTree != null)
        {
            if (err == 0)
            {
                ctx.Stdout.Write(XmlUtils.ToXmlString(validTree));
            }
        }

        if (ctx.Verbosity >= Verbosity.Verbose)
        {
            if (err != 0)
            {
                ctx.Stderr.WriteLine($"{Name}: FAILED: {fname} fails to validate against schema {schemaLabel}");
            }
            else
            {
                ctx.Stderr.WriteLine($"{Name}: SUCCESS: {fname} validates against schema {schemaLabel}");
            }
        }

        if ((ctx.ShowFnames == ShowFnames.Invalid && err != 0) ||
            (ctx.ShowFnames == ShowFnames.Valid && err == 0))
        {
            ctx.Stdout.WriteLine(fname);
        }

        return err;
    }

    private XmlElement AddDocumentNode(Context ctx, string fname)
    {
        XmlElement rn = ctx.Report!.CreateElement("document");
        rn.SetAttribute("path", fname);
        ctx.ReportRoot!.AppendChild(rn);
        return rn;
    }

    /// <summary>
    /// Check that xs:IDREF and xs:IDREFS attributes reference an existing @id.
    /// Mirrors <c>check_idrefs</c> in the C source.
    /// </summary>
    private int CheckIdrefs(XmlDocument doc, string fname, Context ctx, XmlElement? reportNode, LineInfo lineInfo)
    {
        int err = 0;

        // xs:IDREF
        XmlNodeList? badIdrefs = doc.SelectNodes(InvalidIdXPath);
        if (badIdrefs != null && badIdrefs.Count > 0)
        {
            foreach (XmlNode node in badIdrefs)
            {
                string id = node.Value ?? string.Empty;
                int line = lineInfo.LineOfNode(node);
                if (ctx.Verbosity > Verbosity.Silent)
                {
                    ctx.Stderr.WriteLine($"{Name}: ERROR: {fname} ({line}): No matching ID for '{id}'.");
                }
                if (ctx.Report != null)
                {
                    AddXmlReportError(ctx, reportNode!, node, line, $"No matching ID for '{id}'.");
                }
            }
            ++err;
        }

        // xs:IDREFS
        XmlNodeList? idrefsNodes = doc.SelectNodes(InvalidIdsXPath);
        if (idrefsNodes != null && idrefsNodes.Count > 0)
        {
            foreach (XmlNode node in idrefsNodes)
            {
                string ids = node.Value ?? string.Empty;
                foreach (string id in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    XmlNode? match = doc.SelectSingleNode($"//*[@id='{id}']");
                    if (match == null)
                    {
                        int line = lineInfo.LineOfNode(node);
                        if (ctx.Verbosity > Verbosity.Silent)
                        {
                            ctx.Stderr.WriteLine($"{Name}: ERROR: {fname} ({line}): No matching ID for '{id}'.");
                        }
                        if (ctx.Report != null)
                        {
                            AddXmlReportError(ctx, reportNode!, node, line, $"No matching ID for '{id}'.");
                        }
                        ++err;
                    }
                }
            }
        }

        return err;
    }

    /// <summary>
    /// Resolve the schema referenced by an object into an <see cref="XmlSchemaSet"/>.
    /// Returns null (with a reason) when the schema cannot be loaded — typically
    /// because it is a remote URL and the network is unavailable.
    /// </summary>
    private static XmlSchemaSet? ResolveSchema(string url, out string? error)
    {
        error = null;
        string? localPath = LocateLocalSchema(url);
        if (localPath == null)
        {
            error = "schema is not locally resolvable";
            return null;
        }

        try
        {
            var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
            using var reader = XmlReader.Create(localPath);
            // noNamespaceSchemaLocation schemas have no target namespace.
            set.Add(null, reader);
            set.Compile();
            return set;
        }
        catch (Exception ex) when (ex is XmlSchemaException or XmlException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Map a schema reference to a local file path, if one exists. Accepts a
    /// direct local path, a file:// URI, or an http(s) URL whose final path
    /// segment exists in the current directory (a common offline mirror layout).
    /// Remote URLs that cannot be mapped to a local file return null so the
    /// caller can degrade gracefully.
    /// </summary>
    private static string? LocateLocalSchema(string url)
    {
        if (File.Exists(url))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            if (uri.IsFile)
            {
                return File.Exists(uri.LocalPath) ? uri.LocalPath : null;
            }

            // Remote URL: try a local mirror by file name in the working dir.
            string name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrEmpty(name) && File.Exists(name))
            {
                return name;
            }
            return null;
        }

        return null;
    }

    /// <summary>Validate a document against a compiled schema set.</summary>
    private int SchemaValidate(XmlDocument doc, XmlSchemaSet schemaSet, Context ctx, XmlElement? reportNode)
    {
        int errors = 0;

        void Handler(object? sender, ValidationEventArgs e)
        {
            // Only warnings/errors count; treat errors as validation failures.
            if (e.Severity == XmlSeverityType.Error)
            {
                ++errors;
            }
            if (ctx.Verbosity > Verbosity.Silent)
            {
                ctx.Stderr.WriteLine($"{Name}: ERROR: ({e.Exception?.LineNumber ?? 0}): {e.Message}");
            }
            if (ctx.Report != null)
            {
                AddXmlReportError(ctx, reportNode!, null, e.Exception?.LineNumber ?? 0, StripNewline(e.Message));
            }
        }

        // Validate by streaming the serialized document through a validating reader.
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemaSet,
        };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += Handler;

        try
        {
            string xml = XmlUtils.ToXmlString(doc);
            using var sr = new StringReader(xml);
            using var reader = XmlReader.Create(sr, settings);
            while (reader.Read())
            {
                // Validation happens as a side effect of reading.
            }
        }
        catch (XmlException ex)
        {
            ++errors;
            if (ctx.Verbosity > Verbosity.Silent)
            {
                ctx.Stderr.WriteLine($"{Name}: ERROR: {ex.Message}");
            }
            if (ctx.Report != null)
            {
                AddXmlReportError(ctx, reportNode!, null, ex.LineNumber, StripNewline(ex.Message));
            }
        }

        return errors == 0 ? 0 : 1;
    }

    /// <summary>
    /// Strip elements in a given namespace URI from a document. Mirrors
    /// <c>strip_ns</c>.
    /// </summary>
    private static void StripNs(XmlDocument doc, string uri)
    {
        var toRemove = new List<XmlNode>();
        Walk(doc.DocumentElement);
        foreach (XmlNode n in toRemove)
        {
            n.ParentNode?.RemoveChild(n);
        }

        void Walk(XmlNode? node)
        {
            if (node == null)
            {
                return;
            }
            if (node.NodeType == XmlNodeType.Element && node.NamespaceURI == uri)
            {
                toRemove.Add(node);
                return; // children go with it
            }
            for (XmlNode? c = node.FirstChild; c != null; c = c.NextSibling)
            {
                Walk(c);
            }
        }
    }

    /// <summary>Add an &lt;error&gt; node to the XML report. Mirrors <c>add_xml_report_error</c>.</summary>
    private void AddXmlReportError(Context ctx, XmlElement reportNode, XmlNode? node, int lineno, string message)
    {
        XmlDocument report = ctx.Report!;
        XmlElement error = report.CreateElement("error");
        reportNode.AppendChild(error);

        XmlElement msg = report.CreateElement("message");
        msg.InnerText = message;
        error.AppendChild(msg);
        error.SetAttribute("line", lineno.ToString());

        if (node != null)
        {
            XmlElement obj = report.CreateElement("object");
            obj.SetAttribute("xpath", XmlUtils.XPathOf(node));

            XmlNode subject = node.NodeType == XmlNodeType.Attribute
                ? ((XmlAttribute)node).OwnerElement!
                : node;

            // deep_copy_nodes ? full copy : shallow (element + attributes only).
            XmlNode imported = report.ImportNode(subject, ctx.DeepCopyNodes);
            obj.AppendChild(imported);
            error.AppendChild(obj);
        }
    }

    private static int GetLine(Exception ex) => ex is XmlException xe ? xe.LineNumber : 0;

    private static string StripNewline(string s)
    {
        int nl = s.IndexOf('\n');
        return nl < 0 ? s : s[..nl];
    }

    private static string ReportToString(XmlDocument report)
    {
        var settings = new XmlWriterSettings
        {
            Indent = false,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
        };
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            report.Save(writer);
        }
        return new UTF8Encoding(false).GetString(ms.ToArray()) + "\n";
    }

    /// <summary>
    /// The original <c>stats.xsl</c> (embedded verbatim from
    /// <c>reference/tools/s1kd-validate/stats.xsl</c>). It is a plain XSLT 1.0
    /// transform with no EXSLT use, so <see cref="XslCompiledTransform"/> runs it
    /// directly.
    /// </summary>
    private const string StatsXsl =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <xsl:stylesheet
          xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
          version="1.0">

          <xsl:output method="text"/>

          <xsl:template match="s1kdValidateReport">
            <xsl:variable name="total" select="count(document)"/>
            <xsl:text>Total documents checked: </xsl:text>
            <xsl:value-of select="$total"/>
            <xsl:text>&#10;</xsl:text>
            <xsl:if test="$total &gt; 0">
              <xsl:variable name="errors" select="count(document/error)"/>
              <xsl:variable name="fail" select="count(document[error])"/>
              <xsl:variable name="pass" select="count(document[not(error)])"/>
              <xsl:text>Total errors: </xsl:text>
              <xsl:value-of select="$errors"/>
              <xsl:text>&#10;</xsl:text>
              <xsl:text>Total documents that pass the check: </xsl:text>
              <xsl:value-of select="$pass"/>
              <xsl:text>&#10;</xsl:text>
              <xsl:text>Total documents that fail the check: </xsl:text>
              <xsl:value-of select="$fail"/>
              <xsl:text>&#10;</xsl:text>
              <xsl:text>Percentage passed: </xsl:text>
              <xsl:value-of select="floor($pass div $total * 100)"/>
              <xsl:text>%&#10;</xsl:text>
              <xsl:text>Percentage failed: </xsl:text>
              <xsl:value-of select="ceiling($fail div $total * 100)"/>
              <xsl:text>%&#10;</xsl:text>
            </xsl:if>
          </xsl:template>

        </xsl:stylesheet>
        """;

    /// <summary>
    /// Print a summary of the check by applying <c>stats.xsl</c> to the XML
    /// report. Mirrors <c>print_stats</c> in the C source (output to stderr).
    /// </summary>
    private static void PrintStats(XmlDocument report, TextWriter stderr)
    {
        var xslt = new XslCompiledTransform();
        using (var sr = new StringReader(StatsXsl))
        using (var xr = XmlReader.Create(sr))
        {
            xslt.Load(xr);
        }

        var sb = new StringBuilder();
        // method="text" output: write the raw transform result. Use a writer
        // configured to honour the stylesheet's text output method.
        using (var sw = new StringWriter(sb))
        {
            xslt.Transform(report, null, sw);
        }

        stderr.Write(sb.ToString());
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-s <path>] [-X <URI>] [-F|-f] [-o|-x] [-elqTv8^h?] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -e, --ignore-empty     Ignore empty/non-XML documents.");
        stdout.WriteLine("  -F, --valid-filenames  List valid files.");
        stdout.WriteLine("  -f, --filenames        List invalid files.");
        stdout.WriteLine("  -h, -?, --help         Show help/usage message.");
        stdout.WriteLine("  -l, --list             Treat input as list of filenames.");
        stdout.WriteLine("  -o, --output-valid     Output valid CSDB objects to stdout.");
        stdout.WriteLine("  -q, --quiet            Silent (no output).");
        stdout.WriteLine("  -s, --schema <path>    Validate against the given schema.");
        stdout.WriteLine("  -T, --summary          Print a summary of the check.");
        stdout.WriteLine("  -v, --verbose          Verbose output.");
        stdout.WriteLine("  -X, --exclude <URI>    Exclude namespace from validation by URI.");
        stdout.WriteLine("  -x, --xml              Output an XML report.");
        stdout.WriteLine("  -8, --deep-copy-nodes  The XML report will include a deep copy of invalid nodes.");
        stdout.WriteLine("  -^, --remove-deleted   Validate with elements marked as \"delete\" removed.");
        stdout.WriteLine("      --version          Show version information.");
        stdout.WriteLine("  <object>               Any number of CSDB objects to validate.");
    }
}
