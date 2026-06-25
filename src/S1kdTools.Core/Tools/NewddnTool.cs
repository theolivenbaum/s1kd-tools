using System.Globalization;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-newddn</c>: create a new S1000D Data Dispatch Note (DDN)
/// with the code, metadata, and list of dispatched files specified.
///
/// Mirrors the C tool's option set, exit codes, <c>.defaults</c> handling, DDN
/// code parsing, BREX setting, issue-date handling, remarks, delivery list and
/// the automatic <c>DDN-…​.XML</c> filename.
/// </summary>
/// <remarks>
/// Faithful to issue 6 (the default). For older S1000D issues (-$ 2.x..5.0) the
/// C tool applies a downissue XSLT (<c>common/to*.xsl</c>); the port reuses those
/// stylesheets (embedded under <c>Resources/newdm/common/</c>) via
/// <see cref="XslCompiledTransform"/>, mirroring <c>toissue</c>: the default BREX
/// DM code is set (as in the C) and the document is then down-converted.
/// </remarks>
public sealed class NewddnTool : ITool
{
    public string Name => "newddn";
    public string Description => "Create a new data dispatch note.";
    public string Version => "3.0.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitDdnExists = 1;
    private const int ExitMalformedCode = 2;
    private const int ExitBadBrexDmc = 3;
    private const int ExitBadDate = 4;
    private const int ExitBadIssue = 5;
    private const int ExitBadTemplate = 6;
    private const int ExitBadTemplDir = 7;
    private const int ExitOsError = 8;

    private const string ErrPrefix = "s1kd-newddn: ERROR: ";

    private enum Issue { NoIss, Iss20, Iss21, Iss22, Iss23, Iss30, Iss40, Iss41, Iss42, Iss50, Iss6 }

    private const Issue DefaultIssue = Issue.Iss6;

    /* DDN code components. */
    private string _modelIdentCode = "";
    private string _senderIdent = "";
    private string _receiverIdent = "";
    private string _yearOfDataIssue = "";
    private string _seqNumber = "";

    /* Address / status metadata. */
    private string _sender = "";
    private string _receiver = "";
    private string _senderCity = "";
    private string _receiverCity = "";
    private string _senderCountry = "";
    private string _receiverCountry = "";
    private string _securityClassification = "";
    private string _authorization = "";
    private string _brexDmcode = "";
    private string _ddnIssueDate = "";
    private string? _remarks;

    private Issue _issue = Issue.NoIss;
    private string? _templateDir;

    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? defaultsFname = null;
        bool customDefaults = false;

        string ddncode = "";
        bool skipcode = false;
        bool verbose = false;
        bool overwrite = false;
        bool noOverwriteError = false;
        bool showprompts = false; // accepted but interactive prompting is not performed

        string? @out = null;

        var files = new List<string>();

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
                    case "-p" or "--prompt":
                        showprompts = true;
                        break;
                    case "-d" or "--defaults":
                        defaultsFname = NextArg(args, ref i, "-d", stderr);
                        customDefaults = true;
                        break;
                    case "-#" or "--code":
                        ddncode = NextArg(args, ref i, "-#", stderr);
                        skipcode = true;
                        break;
                    case "-o" or "--sender":
                        _sender = NextArg(args, ref i, "-o", stderr);
                        break;
                    case "-r" or "--receiver":
                        _receiver = NextArg(args, ref i, "-r", stderr);
                        break;
                    case "-t" or "--sender-city":
                        _senderCity = NextArg(args, ref i, "-t", stderr);
                        break;
                    case "-n" or "--sender-country":
                        _senderCountry = NextArg(args, ref i, "-n", stderr);
                        break;
                    case "-T" or "--receiver-city":
                        _receiverCity = NextArg(args, ref i, "-T", stderr);
                        break;
                    case "-N" or "--receiver-country":
                        _receiverCountry = NextArg(args, ref i, "-N", stderr);
                        break;
                    case "-a" or "--authorization":
                        _authorization = NextArg(args, ref i, "-a", stderr);
                        break;
                    case "-b" or "--brex":
                        _brexDmcode = NextArg(args, ref i, "-b", stderr);
                        break;
                    case "-I" or "--date":
                        _ddnIssueDate = NextArg(args, ref i, "-I", stderr);
                        break;
                    case "-v" or "--verbose":
                        verbose = true;
                        break;
                    case "-f" or "--overwrite":
                        overwrite = true;
                        break;
                    case "-$" or "--issue":
                        _issue = GetIssue(NextArg(args, ref i, "-$", stderr), stderr);
                        break;
                    case "-@" or "--out":
                        @out = NextArg(args, ref i, "-@", stderr);
                        break;
                    case "-%" or "--templates":
                        _templateDir = NextArg(args, ref i, "-%", stderr);
                        break;
                    case "-q" or "--quiet":
                        noOverwriteError = true;
                        break;
                    case "-m" or "--remarks":
                        _remarks = NextArg(args, ref i, "-m", stderr);
                        break;
                    case "-~" or "--dump-templates":
                        DumpTemplate(NextArg(args, ref i, "-~", stderr), stderr);
                        return 0;
                    default:
                        if (a.StartsWith('-') && a.Length > 1 && a != "-")
                        {
                            stderr.WriteLine($"{ErrPrefix}Unknown option: {a}");
                            return 2;
                        }
                        files.Add(a);
                        break;
                }
            }

            _ = showprompts; // prompting (-p) is a no-op in this non-interactive port

            // .defaults file ---------------------------------------------------
            if (!customDefaults)
            {
                Csdb.FindConfig(Csdb.DefaultsFileName, out string found);
                defaultsFname = found;
            }

            ReadDefaults(defaultsFname!, stderr);

            // DDN code from -# --------------------------------------------------
            if (ddncode.Length != 0)
            {
                int offset = ddncode.StartsWith("DDN-", StringComparison.Ordinal) ? 4 : 0;
                string[] parts = ddncode[offset..].Split('-');
                if (parts.Length < 5 || Array.Exists(parts[..5], string.IsNullOrEmpty))
                {
                    stderr.WriteLine($"{ErrPrefix}Bad DDN code.");
                    throw new ExitException(ExitMalformedCode);
                }

                _modelIdentCode = parts[0];
                _senderIdent = parts[1];
                _receiverIdent = parts[2];
                _yearOfDataIssue = parts[3];
                _seqNumber = parts[4];
            }
            _ = skipcode;

            // Validate required components -------------------------------------
            if (_modelIdentCode.Length == 0 ||
                _senderIdent.Length == 0 ||
                _receiverIdent.Length == 0 ||
                _yearOfDataIssue.Length == 0 ||
                _seqNumber.Length == 0)
            {
                stderr.Write($"{ErrPrefix}Missing required DDN code components: ");
                stderr.WriteLine("DDN-{0}-{1}-{2}-{3}-{4}",
                    _modelIdentCode.Length == 0 ? "???" : _modelIdentCode,
                    _senderIdent.Length == 0 ? "???" : _senderIdent,
                    _receiverIdent.Length == 0 ? "???" : _receiverIdent,
                    _yearOfDataIssue.Length == 0 ? "???" : _yearOfDataIssue,
                    _seqNumber.Length == 0 ? "???" : _seqNumber);
                throw new ExitException(ExitMalformedCode);
            }

            if (_issue == Issue.NoIss)
            {
                _issue = DefaultIssue;
            }
            if (_securityClassification.Length == 0)
            {
                _securityClassification = "01";
            }

            // Build the document ----------------------------------------------
            XmlDocument ddn = XmlSkeleton(stderr);

            var ddnCode = (XmlElement)XmlUtils.XPathFirstNode(ddn, null, "//ddnIdent/ddnCode")!;
            ddnCode.SetAttribute("modelIdentCode", _modelIdentCode);
            ddnCode.SetAttribute("senderIdent", _senderIdent);
            ddnCode.SetAttribute("receiverIdent", _receiverIdent);
            ddnCode.SetAttribute("yearOfDataIssue", _yearOfDataIssue);
            ddnCode.SetAttribute("seqNumber", _seqNumber);

            var issueDate = (XmlElement)XmlUtils.XPathFirstNode(ddn, null, "//ddnAddressItems/issueDate")!;
            SetIssueDate(issueDate);

            SetText(ddn, "//ddnAddressItems/dispatchFrom/dispatchAddress/enterprise/enterpriseName", _sender);
            SetText(ddn, "//ddnAddressItems/dispatchTo/dispatchAddress/enterprise/enterpriseName", _receiver);
            SetText(ddn, "//ddnAddressItems/dispatchFrom/dispatchAddress/address/city", _senderCity);
            SetText(ddn, "//ddnAddressItems/dispatchTo/dispatchAddress/address/city", _receiverCity);
            SetText(ddn, "//ddnAddressItems/dispatchFrom/dispatchAddress/address/country", _senderCountry);
            SetText(ddn, "//ddnAddressItems/dispatchTo/dispatchAddress/address/country", _receiverCountry);

            var security = (XmlElement)XmlUtils.XPathFirstNode(ddn, null, "//ddnStatus/security")!;
            security.SetAttribute("securityClassification", _securityClassification);

            SetText(ddn, "//ddnStatus/authorization", _authorization);

            if (_brexDmcode.Length != 0)
            {
                SetBrex(ddn, _brexDmcode, stderr);
            }

            SetRemarks(ddn, _remarks);

            var deliveryList = (XmlElement)XmlUtils.XPathFirstNode(ddn, null, "//ddnContent/deliveryList")!;
            if (files.Count > 0)
            {
                PopulateList(deliveryList, files);
            }
            else
            {
                deliveryList.ParentNode?.RemoveChild(deliveryList);
            }

            // Down-issue conversion (issue < 6) -------------------------------
            if (_issue < Issue.Iss6)
            {
                if (_brexDmcode.Length == 0)
                {
                    string? defBrex = DefaultBrexForIssue(_issue);
                    if (defBrex != null)
                    {
                        SetBrex(ddn, defBrex, stderr);
                    }
                }

                // Down-convert the document to the selected older issue using the
                // shared common/to<NN>.xsl stylesheets (mirror toissue()).
                ddn = ToIssue(ddn, _issue);
            }

            // Output path -----------------------------------------------------
            string? outdir = null;
            if (@out != null && Directory.Exists(@out))
            {
                outdir = @out;
                @out = null;
            }

            @out ??= $"DDN-{_modelIdentCode}-{_senderIdent}-{_receiverIdent}-{_yearOfDataIssue}-{_seqNumber}.XML";

            string outPath = outdir != null ? Path.Combine(outdir, @out) : @out;

            if (!overwrite && File.Exists(outPath))
            {
                if (noOverwriteError)
                {
                    return 0;
                }
                if (outdir != null)
                {
                    stderr.WriteLine($"{ErrPrefix}{outdir}/{@out} already exists. Use -f to overwrite.");
                }
                else
                {
                    stderr.WriteLine($"{ErrPrefix}{@out} already exists. Use -f to overwrite.");
                }
                throw new ExitException(ExitDdnExists);
            }

            try
            {
                XmlUtils.SaveDoc(ddn, outPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"{ErrPrefix}Could not write {outPath}: {ex.Message}");
                throw new ExitException(ExitOsError);
            }

            if (verbose)
            {
                stdout.WriteLine(outdir != null ? $"{outdir}/{@out}" : @out);
            }

            return 0;
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }
    }

    /* ----- skeleton / template ----- */

    private XmlDocument XmlSkeleton(TextWriter stderr)
    {
        if (_templateDir != null)
        {
            string src = Path.Combine(_templateDir, "ddn.xml");
            if (!File.Exists(src))
            {
                stderr.WriteLine($"{ErrPrefix}No schema ddn in template directory \"{_templateDir}\".");
                throw new ExitException(ExitBadTemplate);
            }
            return XmlUtils.ReadDoc(src);
        }

        return EmbeddedResources.LoadXml("newddn/ddn.xml");
    }

    private void DumpTemplate(string path, TextWriter stderr)
    {
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"{ErrPrefix}Cannot dump template to directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }

        try
        {
            byte[] bytes = EmbeddedResources.ReadBytes("newddn/ddn.xml");
            File.WriteAllBytes(Path.Combine(path, "ddn.xml"), bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{ErrPrefix}Cannot dump template to directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }
    }

    /* ----- defaults ----- */

    private void ReadDefaults(string defaultsFname, TextWriter stderr)
    {
        if (!File.Exists(defaultsFname))
        {
            return;
        }

        // Try XML form first (matches read_xml_doc); fall back to the plain
        // "key value" line form.
        XmlDocument? xml = null;
        try
        {
            xml = XmlUtils.ReadDoc(defaultsFname);
        }
        catch (Exception ex) when (ex is XmlException or IOException)
        {
            xml = null;
        }

        if (xml?.DocumentElement != null)
        {
            foreach (XmlNode cur in xml.DocumentElement.ChildNodes)
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
                CopyDefaultValue(el.GetAttribute("ident"), el.GetAttribute("value"), stderr);
            }
        }
        else
        {
            foreach (string line in File.ReadLines(defaultsFname))
            {
                string trimmed = line.TrimStart();
                int sp = trimmed.IndexOfAny(new[] { ' ', '\t' });
                if (sp <= 0)
                {
                    continue;
                }
                string key = trimmed[..sp];
                string val = trimmed[(sp + 1)..].TrimStart().TrimEnd('\r', '\n');
                if (val.Length == 0)
                {
                    continue;
                }
                CopyDefaultValue(key, val, stderr);
            }
        }
    }

    private void CopyDefaultValue(string key, string val, TextWriter stderr)
    {
        SetIfUnset(key, "modelIdentCode", val, ref _modelIdentCode);
        SetIfUnset(key, "senderIdent", val, ref _senderIdent);
        SetIfUnset(key, "receiverIdent", val, ref _receiverIdent);
        SetIfUnset(key, "yearOfDataIssue", val, ref _yearOfDataIssue);
        SetIfUnset(key, "seqNumber", val, ref _seqNumber);
        SetIfUnset(key, "originator", val, ref _sender);
        SetIfUnset(key, "receiver", val, ref _receiver);
        SetIfUnset(key, "city", val, ref _senderCity);
        SetIfUnset(key, "receiverCity", val, ref _receiverCity);
        SetIfUnset(key, "country", val, ref _senderCountry);
        SetIfUnset(key, "receiverCountry", val, ref _receiverCountry);
        SetIfUnset(key, "securityClassification", val, ref _securityClassification);
        SetIfUnset(key, "authorization", val, ref _authorization);
        SetIfUnset(key, "brex", val, ref _brexDmcode);

        if (key == "issue" && _issue == Issue.NoIss)
        {
            _issue = GetIssue(val, stderr);
        }
        if (key == "templates" && _templateDir == null)
        {
            _templateDir = val;
        }
        if (key == "remarks" && _remarks == null)
        {
            _remarks = val;
        }
    }

    private static void SetIfUnset(string key, string match, string val, ref string target)
    {
        if (key == match && target.Length == 0)
        {
            target = val;
        }
    }

    /* ----- issue helpers ----- */

    private Issue GetIssue(string iss, TextWriter stderr)
    {
        switch (iss)
        {
            case "6": return Issue.Iss6;
            case "5.0": return Issue.Iss50;
            case "4.2": return Issue.Iss42;
            case "4.1": return Issue.Iss41;
            case "4.0": return Issue.Iss40;
            case "3.0": return Issue.Iss30;
            case "2.3": return Issue.Iss23;
            case "2.2": return Issue.Iss22;
            case "2.1": return Issue.Iss21;
            case "2.0": return Issue.Iss20;
            default:
                stderr.WriteLine($"{ErrPrefix}Unsupported issue: {iss}");
                throw new ExitException(ExitBadIssue);
        }
    }

    private static string? DefaultBrexForIssue(Issue iss) => iss switch
    {
        Issue.Iss22 => "AE-A-04-10-0301-00A-022A-D",
        Issue.Iss23 => "AE-A-04-10-0301-00A-022A-D",
        Issue.Iss30 => "AE-A-04-10-0301-00A-022A-D",
        Issue.Iss40 => "S1000D-A-04-10-0301-00A-022A-D",
        Issue.Iss41 => "S1000D-E-04-10-0301-00A-022A-D",
        Issue.Iss42 => "S1000D-F-04-10-0301-00A-022A-D",
        Issue.Iss50 => "S1000D-G-04-10-0301-00A-022A-D",
        _ => null,
    };

    /// <summary>
    /// Down-issue the document to an older S1000D issue using the shared
    /// <c>common/to*.xsl</c> stylesheets (embedded under
    /// <c>Resources/newdm/common/</c>). Mirrors <c>toissue</c>.
    /// </summary>
    private static XmlDocument ToIssue(XmlDocument doc, Issue iss)
    {
        string? xsl = iss switch
        {
            Issue.Iss50 => "newdm/common/to50.xsl",
            Issue.Iss42 => "newdm/common/to42.xsl",
            Issue.Iss41 => "newdm/common/to41.xsl",
            Issue.Iss40 => "newdm/common/to40.xsl",
            Issue.Iss30 => "newdm/common/to30.xsl",
            Issue.Iss23 => "newdm/common/to23.xsl",
            Issue.Iss22 => "newdm/common/to22.xsl",
            Issue.Iss21 => "newdm/common/to21.xsl",
            Issue.Iss20 => "newdm/common/to20.xsl",
            _ => null,
        };
        if (xsl == null) return doc;

        var transform = new XslCompiledTransform();
        using (Stream s = EmbeddedResources.Open(xsl)
                          ?? throw new FileNotFoundException($"Embedded resource not found: {xsl}"))
        using (var reader = XmlReader.Create(s))
        {
            transform.Load(reader);
        }

        var result = XmlUtils.NewDocument();
        using (var sw = new StringWriter())
        {
            using (var xw = XmlWriter.Create(sw, transform.OutputSettings ?? new XmlWriterSettings()))
            {
                transform.Transform(doc, xw);
            }
            result.LoadXml(sw.ToString());
        }
        return result;
    }

    /* ----- content setters ----- */

    private static void SetText(XmlDocument doc, string xpath, string value)
    {
        if (XmlUtils.XPathFirstNode(doc, null, xpath) is { } node)
        {
            node.InnerText = value;
        }
    }

    private void SetIssueDate(XmlElement issueDate)
    {
        string yearS, monthS, dayS;

        if (_ddnIssueDate.Length == 0)
        {
            DateTime now = DateTime.Now;
            yearS = now.Year.ToString(CultureInfo.InvariantCulture);
            monthS = now.Month.ToString("D2", CultureInfo.InvariantCulture);
            dayS = now.Day.ToString("D2", CultureInfo.InvariantCulture);
        }
        else
        {
            // sscanf("%4s-%2s-%2s") — three '-'-separated fields are required.
            string[] parts = _ddnIssueDate.Split('-');
            if (parts.Length < 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
            {
                throw BadDate();
            }
            yearS = Take(parts[0], 4);
            monthS = Take(parts[1], 2);
            dayS = Take(parts[2], 2);
        }

        issueDate.SetAttribute("year", yearS);
        issueDate.SetAttribute("month", monthS);
        issueDate.SetAttribute("day", dayS);
    }

    private ExitException BadDate()
    {
        return new ExitException(ExitBadDate);
    }

    private static string Take(string s, int n) => s.Length <= n ? s : s[..n];

    private void SetBrex(XmlDocument doc, string code, TextWriter stderr)
    {
        var dmCode = XmlUtils.XPathFirstNode(doc, null, "//brexDmRef/dmRef/dmRefIdent/dmCode") as XmlElement;
        if (dmCode == null)
        {
            return;
        }

        // sscanf("%14[^-]-%4[^-]-%3[^-]-%c%c-%4[^-]-%2s%3[^-]-%3s%c-%c-%3s%1s")
        // Fields: modelIdentCode-systemDiffCode-systemCode-(subSys)(subSubSys)-
        //         assyCode-(disassy)(disassyVariant)-(infoCode)(infoVariant)-
        //         itemLocationCode[-learnCode learnEventCode]
        if (!ParseBrex(code,
                out string modelIdentCode, out string systemDiffCode, out string systemCode,
                out string subSystemCode, out string subSubSystemCode, out string assyCode,
                out string disassyCode, out string disassyCodeVariant, out string infoCode,
                out string infoCodeVariant, out string itemLocationCode,
                out string learnCode, out string learnEventCode))
        {
            stderr.WriteLine($"{ErrPrefix}Bad BREX data module code.");
            throw new ExitException(ExitBadBrexDmc);
        }

        dmCode.SetAttribute("modelIdentCode", modelIdentCode);
        dmCode.SetAttribute("systemDiffCode", systemDiffCode);
        dmCode.SetAttribute("systemCode", systemCode);
        dmCode.SetAttribute("subSystemCode", subSystemCode);
        dmCode.SetAttribute("subSubSystemCode", subSubSystemCode);
        dmCode.SetAttribute("assyCode", assyCode);
        dmCode.SetAttribute("disassyCode", disassyCode);
        dmCode.SetAttribute("disassyCodeVariant", disassyCodeVariant);
        dmCode.SetAttribute("infoCode", infoCode);
        dmCode.SetAttribute("infoCodeVariant", infoCodeVariant);
        dmCode.SetAttribute("itemLocationCode", itemLocationCode);

        if (learnCode.Length != 0)
        {
            dmCode.SetAttribute("learnCode", learnCode);
        }
        if (learnEventCode.Length != 0)
        {
            dmCode.SetAttribute("learnEventCode", learnEventCode);
        }
    }

    /// <summary>
    /// Parse a BREX DMC. Returns true for the 11-field form (no learn data) or
    /// the 13-field form (with learnCode/learnEventCode). Mirrors the C sscanf:
    /// the count must be exactly 11 or 13.
    /// </summary>
    private static bool ParseBrex(string code,
        out string modelIdentCode, out string systemDiffCode, out string systemCode,
        out string subSystemCode, out string subSubSystemCode, out string assyCode,
        out string disassyCode, out string disassyCodeVariant, out string infoCode,
        out string infoCodeVariant, out string itemLocationCode,
        out string learnCode, out string learnEventCode)
    {
        modelIdentCode = systemDiffCode = systemCode = subSystemCode = subSubSystemCode = "";
        assyCode = disassyCode = disassyCodeVariant = infoCode = infoCodeVariant = itemLocationCode = "";
        learnCode = learnEventCode = "";

        string[] g = code.Split('-');
        // Expected group layout: [MIC, sdc, sysCode, (subSys+subSubSys),
        //   assy, (disassy+disassyVariant), (info+infoVariant), itemLoc,
        //   (learnCode), (learnEventCode)]
        if (g.Length != 8 && g.Length != 10)
        {
            return false;
        }

        modelIdentCode = Take(g[0], 14);
        systemDiffCode = Take(g[1], 4);
        systemCode = Take(g[2], 3);

        // %c%c — two single chars.
        if (g[3].Length < 2)
        {
            return false;
        }
        subSystemCode = g[3][..1];
        subSubSystemCode = g[3].Substring(1, 1);

        assyCode = Take(g[4], 4);

        // %2s%3[^-] — first two chars disassyCode, remainder disassyCodeVariant.
        if (g[5].Length < 3)
        {
            return false;
        }
        disassyCode = g[5][..2];
        disassyCodeVariant = Take(g[5][2..], 3);

        // %3s%c — first three chars infoCode, next char infoCodeVariant.
        if (g[6].Length < 4)
        {
            return false;
        }
        infoCode = g[6][..3];
        infoCodeVariant = g[6].Substring(3, 1);

        // %c — single char itemLocationCode.
        if (g[7].Length < 1)
        {
            return false;
        }
        itemLocationCode = g[7][..1];

        if (g.Length == 10)
        {
            // %3s learnCode, %1s learnEventCode.
            if (g[8].Length < 1 || g[9].Length < 1)
            {
                return false;
            }
            learnCode = Take(g[8], 3);
            learnEventCode = g[9][..1];
        }

        return true;
    }

    private void SetRemarks(XmlDocument doc, string? text)
    {
        var remarks = XmlUtils.XPathFirstNode(doc, null, "//remarks");
        if (remarks == null)
        {
            return;
        }

        if (text != null)
        {
            var simplePara = doc.CreateElement("simplePara");
            simplePara.InnerText = text;
            remarks.AppendChild(simplePara);
        }
        else
        {
            remarks.ParentNode?.RemoveChild(remarks);
        }
    }

    private static void PopulateList(XmlElement deliv, IReadOnlyList<string> files)
    {
        XmlDocument doc = deliv.OwnerDocument;
        foreach (string f in files)
        {
            var item = doc.CreateElement("deliveryListItem");
            var fname = doc.CreateElement("dispatchFileName");
            fname.InnerText = Path.GetFileName(f);
            item.AppendChild(fname);
            deliv.AppendChild(item);
        }
    }

    /* ----- arg / help ----- */

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"{ErrPrefix}{opt} requires an argument");
            throw new ExitException(2);
        }
        return args[i];
    }

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] [<files>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -$, --issue <issue>               Specify which S1000D issue to use.");
        stdout.WriteLine("  -@, --out <path>                  Output to specified file or directory.");
        stdout.WriteLine("  -%, --templates <dir>             Use templates in specified directory.");
        stdout.WriteLine("  -~, --dump-templates <dir>        Dump built-in XML template to directory.");
        stdout.WriteLine("  -d, --defaults <file>             Specify the .defaults file name.");
        stdout.WriteLine("  -f, --overwrite                   Overwrite existing file.");
        stdout.WriteLine("  -p, --prompt                      Prompt user for values.");
        stdout.WriteLine("  -q, --quiet                       Don't report an error if file exists.");
        stdout.WriteLine("  -v, --verbose                     Print file name of DDN.");
        stdout.WriteLine("  --version                         Show version information.");
        stdout.WriteLine();
        stdout.WriteLine("In addition, the following metadata can be set:");
        stdout.WriteLine("  -#, --code <code>                 The DDN code (MIC-SENDER-RECEIVER-YEAR-SEQ)");
        stdout.WriteLine("  -a, --authorization <auth>        Authorization");
        stdout.WriteLine("  -b, --brex <BREX>                 BREX data module code");
        stdout.WriteLine("  -I, --date <date>                 Issue date");
        stdout.WriteLine("  -m, --remarks <remarks>           Remarks");
        stdout.WriteLine("  -N, --receiver-country <country>  Receiver country");
        stdout.WriteLine("  -n, --sender-country <country>    Sender country");
        stdout.WriteLine("  -o, --sender <sender>             Sender enterprise name");
        stdout.WriteLine("  -r, --receiver <receiver>         Receiver enterprise name");
        stdout.WriteLine("  -T, --receiver-city <city>        Receiver city");
        stdout.WriteLine("  -t, --sender-city <city>          Sender city");
    }
}
