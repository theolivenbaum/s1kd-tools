using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-newdml</c>: create a new data management list (DML). Mirrors
/// the C tool's option set, exit codes, DML code parsing, filename generation,
/// metadata population and content generation (entries built from CSDB objects
/// or from SNS rules via the bundled XSLT).
/// </summary>
/// <remarks>
/// Faithful to issue 6 (the default). For older S1000D issues (-$ 2.x..5.0) the
/// C tool applies a downissue XSLT (<c>common/to*.xsl</c>); the port reuses those
/// stylesheets (embedded under <c>Resources/newdm/common/</c>) via
/// <see cref="XslCompiledTransform"/>, mirroring <c>toissue</c>: the default BREX
/// DM code is set (as in the C) and the document is then down-converted.
/// </remarks>
public sealed class NewdmlTool : ITool
{
    public string Name => "newdml";
    public string Description => "Create a new data management list (DML).";
    public string Version => "3.0.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitDmlExists = 1;
    private const int ExitBadInput = 2;
    private const int ExitBadCode = 3;
    private const int ExitBadBrexDmc = 4;
    private const int ExitBadDate = 5;
    private const int ExitBadIssue = 6;
    private const int ExitBadTemplate = 7;
    private const int ExitBadTemplDir = 8;
    private const int ExitOsError = 9;
    private const int ExitBadSns = 10;

    private const string DefaultIssue = "6";

    // Default BREX DM codes per issue (mirrors the ISS_*_DEFAULT_BREX defines).
    private static string? DefaultBrex(string issue) => issue switch
    {
        "2.2" or "2.3" or "3.0" => "AE-A-04-10-0301-00A-022A-D",
        "4.0" => "S1000D-A-04-10-0301-00A-022A-D",
        "4.1" => "S1000D-E-04-10-0301-00A-022A-D",
        "4.2" => "S1000D-F-04-10-0301-00A-022A-D",
        "5.0" => "S1000D-G-04-10-0301-00A-022A-D",
        _ => null,
    };

    /* Per-run metadata state (mirror the C static char buffers). */
    private string _modelIdentCode = "";
    private string _senderIdent = "";
    private string _dmlType = "";
    private string _yearOfDataIssue = "";
    private string _seqNumber = "";
    private string _securityClassification = "";
    private string _issueNumber = "";
    private string _inWork = "";
    private string _brexDmcode = "";
    private string _issueDate = "";
    private string? _issueType;
    private string? _remarks;
    private string? _issue;
    private string? _defaultRpcName;
    private string? _defaultRpcCode;
    private string? _templateDir;

    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool showprompts = false; // accepted but non-interactive: ignored
        string code = "";
        bool skipcode = false;
        bool noissue = false;
        bool verbose = false;
        bool overwrite = false;
        bool noOverwriteError = false;
        string? sns = null;
        bool islist = false;
        string? outArg = null;
        string? defaultsFname = null;
        bool customDefaults = false;

        var incodes = new List<string>();
        var objects = new List<string>();

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
                        code = NextArg(args, ref i, "-#", stderr);
                        skipcode = true;
                        break;
                    case "-n" or "--issno":
                        _issueNumber = NextArg(args, ref i, "-n", stderr);
                        break;
                    case "-w" or "--inwork":
                        _inWork = NextArg(args, ref i, "-w", stderr);
                        break;
                    case "-c" or "--security":
                        _securityClassification = NextArg(args, ref i, "-c", stderr);
                        break;
                    case "-N" or "--omit-issue":
                        noissue = true;
                        break;
                    case "-b" or "--brex":
                        _brexDmcode = NextArg(args, ref i, "-b", stderr);
                        break;
                    case "-I" or "--date":
                        _issueDate = NextArg(args, ref i, "-I", stderr);
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
                        outArg = NextArg(args, ref i, "-@", stderr);
                        break;
                    case "-r" or "--rpcname":
                        _defaultRpcName = NextArg(args, ref i, "-r", stderr);
                        break;
                    case "-R" or "--rpccode":
                        _defaultRpcCode = NextArg(args, ref i, "-R", stderr);
                        break;
                    case "-%" or "--templates":
                        _templateDir = NextArg(args, ref i, "-%", stderr);
                        break;
                    case "-q" or "--quiet":
                        noOverwriteError = true;
                        break;
                    case "-S" or "--sns":
                        sns = NextArg(args, ref i, "-S", stderr);
                        break;
                    case "-i" or "--info-code":
                        incodes.Add(NextArg(args, ref i, "-i", stderr));
                        break;
                    case "-m" or "--remarks":
                        _remarks = NextArg(args, ref i, "-m", stderr);
                        break;
                    case "-~" or "--dump-templates":
                        DumpTemplate(NextArg(args, ref i, "-~", stderr), stderr);
                        return 0;
                    case "-z" or "--issue-type":
                        _issueType = NextArg(args, ref i, "-z", stderr);
                        break;
                    case "-l" or "--list":
                        islist = true;
                        break;
                    default:
                        if (a.StartsWith('-') && a.Length > 1 && a != "-")
                        {
                            stderr.WriteLine($"s1kd-{Name}: ERROR: Unknown option: {a}");
                            return ExitBadInput;
                        }
                        objects.Add(a);
                        break;
                }
            }

            _ = showprompts; // prompting requires a TTY; values come from flags/defaults.

            // Locate and read the .defaults file.
            string defaultsPath;
            if (customDefaults)
            {
                defaultsPath = defaultsFname!;
            }
            else
            {
                Csdb.FindConfig(Csdb.DefaultsFileName, out defaultsPath);
            }
            ReadDefaults(defaultsPath);

            if (incodes.Count == 0)
            {
                incodes.Add("000");
            }

            // Parse a full DML code given via -# / --code.
            if (code.Length != 0)
            {
                int offset = code.StartsWith("DML-", StringComparison.Ordinal) ? 4 : 0;
                string[] parts = code[offset..].Split('-');
                if (parts.Length < 5)
                {
                    stderr.WriteLine($"s1kd-{Name}: ERROR: Bad DML code.");
                    return ExitBadCode;
                }
                _modelIdentCode = parts[0];
                _senderIdent = parts[1];
                _dmlType = parts[2];
                _yearOfDataIssue = parts[3];
                _seqNumber = parts[4];
            }

            _ = skipcode;

            if (_modelIdentCode.Length == 0 ||
                _senderIdent.Length == 0 ||
                _dmlType.Length == 0 ||
                _yearOfDataIssue.Length == 0 ||
                _seqNumber.Length == 0)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Missing required DML code components: " +
                    $"DML-{Or(_modelIdentCode)}-{Or(_senderIdent)}-{Or(_dmlType)}-{Or(_yearOfDataIssue)}-{Or(_seqNumber)}");
                return ExitBadCode;
            }

            _issue ??= DefaultIssue;
            if (_issueNumber.Length == 0) _issueNumber = "000";
            if (_inWork.Length == 0) _inWork = "01";
            if (_securityClassification.Length == 0) _securityClassification = "01";

            XmlDocument dmlDoc = XmlSkeleton(stderr);

            var dmlCode = (XmlElement)dmlDoc.SelectSingleNode("//dmlIdent/dmlCode")!;
            var issueInfo = (XmlElement)dmlDoc.SelectSingleNode("//dmlIdent/issueInfo")!;
            var security = (XmlElement)dmlDoc.SelectSingleNode("//dmlStatus/security")!;
            var issueDate = (XmlElement)dmlDoc.SelectSingleNode("//dmlAddressItems/issueDate")!;

            // dmlType is stored lowercase in the metadata, uppercase in the filename.
            _dmlType = LowerFirst(_dmlType);

            dmlCode.SetAttribute("modelIdentCode", _modelIdentCode);
            dmlCode.SetAttribute("senderIdent", _senderIdent);
            dmlCode.SetAttribute("dmlType", _dmlType);
            dmlCode.SetAttribute("yearOfDataIssue", _yearOfDataIssue);
            dmlCode.SetAttribute("seqNumber", _seqNumber);

            issueInfo.SetAttribute("issueNumber", _issueNumber);
            issueInfo.SetAttribute("inWork", _inWork);

            security.SetAttribute("securityClassification", _securityClassification);

            SetIssueDate(issueDate, stderr);

            var dmlStatus = (XmlElement)dmlDoc.SelectSingleNode("//dmlStatus")!;
            if (_issueType != null)
            {
                dmlStatus.SetAttribute("issueType", _issueType);
            }

            if (_brexDmcode.Length != 0)
            {
                SetBrex(dmlDoc, _brexDmcode, stderr);
            }

            SetRemarks(dmlDoc, _remarks);

            _dmlType = UpperFirst(_dmlType);

            var dmlContent = (XmlElement)dmlDoc.SelectSingleNode("//dmlContent")!;

            if (sns != null)
            {
                foreach (string inc in incodes)
                {
                    AddSns(dmlContent, sns, inc, stderr);
                }
            }

            if (objects.Count > 0)
            {
                foreach (string obj in objects)
                {
                    if (islist)
                    {
                        AddRefList(dmlContent, obj, stderr);
                    }
                    else
                    {
                        AddRef(dmlContent, obj);
                    }
                }
            }
            else if (islist)
            {
                AddRefList(dmlContent, null, stderr);
            }

            SortEntries(dmlDoc);

            // Older issues: set the default BREX if none was given, then
            // down-convert the document with the shared common/to<NN>.xsl
            // stylesheets (mirror toissue()).
            if (_issue != DefaultIssue)
            {
                if (_brexDmcode.Length == 0)
                {
                    string? db = DefaultBrex(_issue!);
                    if (db != null)
                    {
                        SetBrex(dmlDoc, db, stderr);
                    }
                }

                dmlDoc = ToIssue(dmlDoc, _issue!);
            }

            // Determine output path.
            string? outDir = null;
            string? outFile = outArg;
            if (outFile != null && Directory.Exists(outFile))
            {
                outDir = outFile;
                outFile = null;
            }

            outFile ??= noissue
                ? $"DML-{_modelIdentCode}-{_senderIdent}-{_dmlType}-{_yearOfDataIssue}-{_seqNumber}.XML"
                : $"DML-{_modelIdentCode}-{_senderIdent}-{_dmlType}-{_yearOfDataIssue}-{_seqNumber}_{_issueNumber}-{_inWork}.XML";

            string fullOut = outDir != null ? Path.Combine(outDir, outFile) : outFile;

            if (outDir != null && !Directory.Exists(outDir))
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not change to directory {outDir}");
                return ExitOsError;
            }

            if (!overwrite && File.Exists(fullOut))
            {
                if (noOverwriteError)
                {
                    return 0;
                }
                stderr.WriteLine($"s1kd-{Name}: ERROR: {fullOut} already exists. Use -f to overwrite.");
                return ExitDmlExists;
            }

            XmlUtils.SaveDoc(dmlDoc, fullOut);

            if (verbose)
            {
                stdout.WriteLine(fullOut);
            }

            return 0;
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }
    }

    /* ----- option helpers ----- */

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: {opt} requires an argument");
            throw new ExitException(ExitBadInput);
        }
        return args[i];
    }

    private string GetIssue(string iss, TextWriter stderr)
    {
        switch (iss)
        {
            case "6":
            case "5.0":
            case "4.2":
            case "4.1":
            case "4.0":
            case "3.0":
            case "2.3":
            case "2.2":
            case "2.1":
            case "2.0":
                return iss;
            default:
                stderr.WriteLine($"s1kd-{Name}: ERROR: Unsupported issue: {iss}");
                throw new ExitException(ExitBadIssue);
        }
    }

    private static string Or(string s) => s.Length == 0 ? "???" : s;

    private static string LowerFirst(string s) =>
        s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static string UpperFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /* ----- defaults ----- */

    private void ReadDefaults(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // Try XML form first (<defaults><default ident="" value=""/>…</defaults>),
        // then fall back to the whitespace-separated text form.
        XmlDocument? doc = null;
        try
        {
            doc = XmlUtils.ReadDoc(path);
        }
        catch (XmlException)
        {
            doc = null;
        }

        if (doc?.DocumentElement != null)
        {
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node is not XmlElement el)
                {
                    continue;
                }
                if (!el.HasAttribute("ident") || !el.HasAttribute("value"))
                {
                    continue;
                }
                CopyDefaultValue(el.GetAttribute("ident"), el.GetAttribute("value"));
            }
        }
        else
        {
            foreach (string line in File.ReadLines(path))
            {
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                int sp = trimmed.IndexOfAny(new[] { ' ', '\t' });
                if (sp < 0)
                {
                    continue;
                }
                string key = trimmed[..sp];
                string val = trimmed[(sp + 1)..].TrimEnd('\n', '\r').TrimStart();
                if (val.Length == 0)
                {
                    continue;
                }
                CopyDefaultValue(key, val);
            }
        }
    }

    private void CopyDefaultValue(string key, string val)
    {
        switch (key)
        {
            case "modelIdentCode" when _modelIdentCode.Length == 0: _modelIdentCode = val; break;
            case "senderIdent" when _senderIdent.Length == 0: _senderIdent = val; break;
            case "dmlType" when _dmlType.Length == 0: _dmlType = val; break;
            case "yearOfDataIssue" when _yearOfDataIssue.Length == 0: _yearOfDataIssue = val; break;
            case "seqNumber" when _seqNumber.Length == 0: _seqNumber = val; break;
            case "issueNumber" when _issueNumber.Length == 0: _issueNumber = val; break;
            case "inWork" when _inWork.Length == 0: _inWork = val; break;
            case "securityClassification" when _securityClassification.Length == 0: _securityClassification = val; break;
            case "brex" when _brexDmcode.Length == 0: _brexDmcode = val; break;
            case "responsiblePartnerCompany" when _defaultRpcName == null: _defaultRpcName = val; break;
            case "responsiblePartnerCompanyCode" when _defaultRpcCode == null: _defaultRpcCode = val; break;
            case "templates" when _templateDir == null: _templateDir = val; break;
            case "remarks" when _remarks == null: _remarks = val; break;
            case "issue" when _issue == null: _issue = val; break;
            case "issueType" when _issueType == null: _issueType = val; break;
        }
    }

    /* ----- skeleton / template ----- */

    private XmlDocument XmlSkeleton(TextWriter stderr)
    {
        if (_templateDir != null)
        {
            string src = Path.Combine(_templateDir, "dml.xml");
            if (!File.Exists(src))
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: No schema dml found in template directory \"{_templateDir}\".");
                throw new ExitException(ExitBadTemplate);
            }
            return XmlUtils.ReadDoc(src);
        }
        return EmbeddedResources.LoadXml("newdml/dml.xml");
    }

    private void DumpTemplate(string path, TextWriter stderr)
    {
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Cannot dump template in directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }
        string content = EmbeddedResources.ReadText("newdml/dml.xml");
        File.WriteAllText(Path.Combine(path, "dml.xml"), content, new UTF8Encoding(false));
    }

    /* ----- metadata setters ----- */

    private void SetIssueDate(XmlElement issueDate, TextWriter stderr)
    {
        string yearS, monthS, dayS;
        if (_issueDate.Length == 0)
        {
            DateTime now = DateTime.Now;
            yearS = now.Year.ToString(CultureInfo.InvariantCulture);
            monthS = now.Month.ToString("D2", CultureInfo.InvariantCulture);
            dayS = now.Day.ToString("D2", CultureInfo.InvariantCulture);
        }
        else
        {
            string[] parts = _issueDate.Split('-');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Bad issue date: {_issueDate}");
                throw new ExitException(ExitBadDate);
            }
            yearS = Take(parts[0], 4);
            monthS = Take(parts[1], 2);
            dayS = Take(parts[2], 2);
        }

        issueDate.SetAttribute("year", yearS);
        issueDate.SetAttribute("month", monthS);
        issueDate.SetAttribute("day", dayS);
    }

    private static string Take(string s, int n) => s.Length <= n ? s : s[..n];

    private void SetRemarks(XmlDocument doc, string? text)
    {
        var remarks = doc.SelectSingleNode("//remarks");
        if (remarks == null)
        {
            return;
        }
        if (text != null)
        {
            var sp = doc.CreateElement("simplePara");
            sp.InnerText = text;
            remarks.AppendChild(sp);
        }
        else
        {
            remarks.ParentNode?.RemoveChild(remarks);
        }
    }

    private void SetBrex(XmlDocument doc, string code, TextWriter stderr)
    {
        var dmCode = doc.SelectSingleNode("//brexDmRef/dmRef/dmRefIdent/dmCode") as XmlElement;
        if (dmCode == null)
        {
            return;
        }

        // Format: MIC-SDC-SC-SubC SubSubC-AC-DC DCV-IC ICV-ILC[-LC-LEC]
        // sscanf("%14[^-]-%4[^-]-%3[^-]-%c%c-%4[^-]-%2s%3[^-]-%3s%c-%c-%3s%1s")
        if (!TryParseBrex(code, out var p))
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Bad BREX data module code.");
            throw new ExitException(ExitBadBrexDmc);
        }

        dmCode.SetAttribute("modelIdentCode", p.ModelIdentCode);
        dmCode.SetAttribute("systemDiffCode", p.SystemDiffCode);
        dmCode.SetAttribute("systemCode", p.SystemCode);
        dmCode.SetAttribute("subSystemCode", p.SubSystemCode);
        dmCode.SetAttribute("subSubSystemCode", p.SubSubSystemCode);
        dmCode.SetAttribute("assyCode", p.AssyCode);
        dmCode.SetAttribute("disassyCode", p.DisassyCode);
        dmCode.SetAttribute("disassyCodeVariant", p.DisassyCodeVariant);
        dmCode.SetAttribute("infoCode", p.InfoCode);
        dmCode.SetAttribute("infoCodeVariant", p.InfoCodeVariant);
        dmCode.SetAttribute("itemLocationCode", p.ItemLocationCode);

        if (p.LearnCode.Length != 0) dmCode.SetAttribute("learnCode", p.LearnCode);
        if (p.LearnEventCode.Length != 0) dmCode.SetAttribute("learnEventCode", p.LearnEventCode);
    }

    private readonly record struct BrexParts(
        string ModelIdentCode, string SystemDiffCode, string SystemCode,
        string SubSystemCode, string SubSubSystemCode, string AssyCode,
        string DisassyCode, string DisassyCodeVariant, string InfoCode,
        string InfoCodeVariant, string ItemLocationCode, string LearnCode,
        string LearnEventCode);

    private static bool TryParseBrex(string code, out BrexParts parts)
    {
        parts = default;

        // The C scanf parses the dash-separated DM code fields as:
        //   MIC - SDC - SC - <subSystemCode><subSubSystemCode> - AC -
        //   <disassyCode><disassyCodeVariant> - <infoCode><infoCodeVariant> -
        //   <itemLocationCode> [ - <learnCode><learnEventCode> ]
        // i.e. 8 fields => 11 components (no learn), 9 fields => 13 (with learn).
        string[] f = code.Split('-');
        if (f.Length != 8 && f.Length != 9)
        {
            return false;
        }

        string mic = f[0];
        string sdc = f[1];
        string sc = f[2];
        string subField = f[3];     // subSystemCode(1) + subSubSystemCode(1)
        string ac = f[4];
        string disField = f[5];     // disassyCode(2) + disassyCodeVariant(1)
        string infoField = f[6];    // infoCode(3) + infoCodeVariant(1)
        string ilc = f[7];          // itemLocationCode(1)

        if (subField.Length < 2 || disField.Length < 2 || infoField.Length < 2 || ilc.Length < 1)
        {
            return false;
        }

        string subSystemCode = subField[..1];
        string subSubSystemCode = subField.Substring(1, 1);

        // disassyCode = leading chars, disassyCodeVariant = trailing char.
        string disassyCodeVariant = disField[^1..];
        string disassyCode = disField[..^1];

        string infoCodeVariant = infoField[^1..];
        string infoCode = infoField[..^1];

        string itemLocationCode = ilc[..1];

        string learnCode = "";
        string learnEventCode = "";
        if (f.Length == 9)
        {
            // learnField = learnCode(3) + learnEventCode(1)
            string learnField = f[8];
            if (learnField.Length < 1)
            {
                return false;
            }
            learnEventCode = learnField[^1..];
            learnCode = learnField[..^1];
        }

        parts = new BrexParts(mic, sdc, sc, subSystemCode, subSubSystemCode, ac,
            disassyCode, disassyCodeVariant, infoCode, infoCodeVariant, itemLocationCode,
            learnCode, learnEventCode);
        return true;
    }

    /* ----- content: refs ----- */

    private void AddRef(XmlElement dmlContent, string path)
    {
        XmlDocument? doc = null;
        if (File.Exists(path))
        {
            try
            {
                doc = XmlUtils.ReadDoc(path);
            }
            catch (XmlException)
            {
                doc = null;
            }
        }

        if (doc?.DocumentElement != null)
        {
            string root = doc.DocumentElement.Name;
            bool csl = string.Equals(_dmlType, "S", StringComparison.Ordinal);
            switch (root)
            {
                case "dmodule": AddDmRef(doc, dmlContent, csl); break;
                case "pm": AddPmRef(doc, dmlContent, csl); break;
                case "icnMetadataFile": AddImfRef(doc, dmlContent); break;
                case "comment": AddComRef(doc, dmlContent); break;
                case "dml": AddDmlRef(doc, dmlContent, csl); break;
            }
        }
        else
        {
            string baseName = Path.GetFileName(path);
            if (baseName.StartsWith("ICN-", StringComparison.Ordinal))
            {
                AddIcnRef(baseName, dmlContent);
            }
        }
    }

    private void AddRefList(XmlElement dmlContent, string? fname, TextWriter stderr)
    {
        TextReader reader;
        if (fname != null)
        {
            try
            {
                reader = new StreamReader(fname);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read list: {fname}");
                return;
            }
        }
        else
        {
            reader = Console.In;
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                int cut = line.IndexOfAny(new[] { '\t', '\r', '\n' });
                string entry = cut < 0 ? line : line[..cut];
                if (entry.Length == 0)
                {
                    continue;
                }
                AddRef(dmlContent, entry);
            }
        }
        finally
        {
            if (fname != null)
            {
                reader.Dispose();
            }
        }
    }

    private static XmlNode? Copy(XmlDocument owner, XmlDocument src, string xpath)
    {
        var node = src.SelectSingleNode(xpath);
        return node == null ? null : owner.ImportNode(node, true);
    }

    private static void AppendIfPresent(XmlElement parent, XmlDocument src, string xpath)
    {
        var copy = Copy(parent.OwnerDocument, src, xpath);
        if (copy != null)
        {
            parent.AppendChild(copy);
        }
    }

    private void AddDmRef(XmlDocument dm, XmlElement dmlContent, bool csl)
    {
        XmlDocument owner = dmlContent.OwnerDocument;
        var dmlEntry = owner.CreateElement("dmlEntry");
        dmlContent.AppendChild(dmlEntry);

        if (csl)
        {
            var status = dm.SelectSingleNode("//dmStatus") as XmlElement;
            string? it = status != null && status.HasAttribute("issueType") ? status.GetAttribute("issueType") : null;
            if (it != null)
            {
                dmlEntry.SetAttribute("issueType", it);
            }
        }

        var dmRef = owner.CreateElement("dmRef");
        dmlEntry.AppendChild(dmRef);
        var dmRefIdent = owner.CreateElement("dmRefIdent");
        dmRef.AppendChild(dmRefIdent);
        AppendIfPresent(dmRefIdent, dm, "//dmIdent/identExtension");
        AppendIfPresent(dmRefIdent, dm, "//dmIdent/dmCode");
        if (csl)
        {
            AppendIfPresent(dmRefIdent, dm, "//dmIdent/issueInfo");
        }
        AppendIfPresent(dmRefIdent, dm, "//dmIdent/language");

        var dmRefAddressItems = owner.CreateElement("dmRefAddressItems");
        dmRef.AppendChild(dmRefAddressItems);
        AppendIfPresent(dmRefAddressItems, dm, "//dmAddressItems/dmTitle");
        if (csl)
        {
            AppendIfPresent(dmRefAddressItems, dm, "//dmAddressItems/issueDate");
        }

        AppendIfPresent(dmlEntry, dm, "//dmStatus/security");
        AppendIfPresent(dmlEntry, dm, "//dmStatus/responsiblePartnerCompany");
    }

    private void AddPmRef(XmlDocument pm, XmlElement dmlContent, bool csl)
    {
        XmlDocument owner = dmlContent.OwnerDocument;
        var dmlEntry = owner.CreateElement("dmlEntry");
        dmlContent.AppendChild(dmlEntry);

        if (csl)
        {
            var status = pm.SelectSingleNode("//pmStatus") as XmlElement;
            string? it = status != null && status.HasAttribute("issueType") ? status.GetAttribute("issueType") : null;
            if (it != null)
            {
                dmlEntry.SetAttribute("issueType", it);
            }
        }

        var pmRef = owner.CreateElement("pmRef");
        dmlEntry.AppendChild(pmRef);
        var pmRefIdent = owner.CreateElement("pmRefIdent");
        pmRef.AppendChild(pmRefIdent);
        AppendIfPresent(pmRefIdent, pm, "//pmIdent/identExtension");
        AppendIfPresent(pmRefIdent, pm, "//pmIdent/pmCode");
        if (csl)
        {
            AppendIfPresent(pmRefIdent, pm, "//pmIdent/issueInfo");
        }
        AppendIfPresent(pmRefIdent, pm, "//pmIdent/language");

        var pmRefAddressItems = owner.CreateElement("pmRefAddressItems");
        pmRef.AppendChild(pmRefAddressItems);
        AppendIfPresent(pmRefAddressItems, pm, "//pmAddressItems/pmTitle");
        if (csl)
        {
            AppendIfPresent(pmRefAddressItems, pm, "//pmAddressItems/issueDate");
        }
        AppendIfPresent(pmRefAddressItems, pm, "//pmAddressItems/shortPmTitle");

        AppendIfPresent(dmlEntry, pm, "//pmStatus/security");
        AppendIfPresent(dmlEntry, pm, "//pmStatus/responsiblePartnerCompany");
    }

    private void AddIcnRef(string str, XmlElement dmlContent)
    {
        XmlDocument owner = dmlContent.OwnerDocument;
        var dmlEntry = owner.CreateElement("dmlEntry");
        dmlContent.AppendChild(dmlEntry);

        var infoEntityRef = owner.CreateElement("infoEntityRef");
        dmlEntry.AppendChild(infoEntityRef);

        // strtok(icn, ".") -> drop extension; sec = chars after last '-'.
        int dot = str.IndexOf('.');
        string icn = dot < 0 ? str : str[..dot];
        int dash = icn.LastIndexOf('-');
        string sec = dash < 0 ? "" : icn[(dash + 1)..];

        infoEntityRef.SetAttribute("infoEntityRefIdent", icn);

        var security = owner.CreateElement("security");
        dmlEntry.AppendChild(security);
        security.SetAttribute("securityClassification", sec);

        AppendRpc(dmlEntry);
    }

    private void AddImfRef(XmlDocument imf, XmlElement dmlContent)
    {
        var imfCode = imf.SelectSingleNode("//imfIdent/imfCode") as XmlElement;
        string idIcn = imfCode != null && imfCode.HasAttribute("imfIdentIcn")
            ? imfCode.GetAttribute("imfIdentIcn") : "";
        AddIcnRef("ICN-" + idIcn, dmlContent);
    }

    private void AddComRef(XmlDocument com, XmlElement dmlContent)
    {
        XmlDocument owner = dmlContent.OwnerDocument;
        var dmlEntry = owner.CreateElement("dmlEntry");
        dmlContent.AppendChild(dmlEntry);

        var commentRef = owner.CreateElement("commentRef");
        dmlEntry.AppendChild(commentRef);
        var commentRefIdent = owner.CreateElement("commentRefIdent");
        commentRef.AppendChild(commentRefIdent);
        AppendIfPresent(commentRefIdent, com, "//commentIdent/commentCode");
        AppendIfPresent(commentRefIdent, com, "//commentIdent/language");

        AppendIfPresent(dmlEntry, com, "//commentStatus/security");

        AppendRpc(dmlEntry);
    }

    private void AddDmlRef(XmlDocument dml, XmlElement dmlContent, bool csl)
    {
        XmlDocument owner = dmlContent.OwnerDocument;
        var dmlEntry = owner.CreateElement("dmlEntry");
        dmlContent.AppendChild(dmlEntry);

        var dmlRef = owner.CreateElement("dmlRef");
        dmlEntry.AppendChild(dmlRef);
        var dmlRefIdent = owner.CreateElement("dmlRefIdent");
        dmlRef.AppendChild(dmlRefIdent);
        AppendIfPresent(dmlRefIdent, dml, "//dmlIdent/dmlCode");
        if (csl)
        {
            AppendIfPresent(dmlRefIdent, dml, "//dmlIdent/issueInfo");
        }

        AppendIfPresent(dmlEntry, dml, "//dmlStatus/security");

        AppendRpc(dmlEntry);
    }

    private void AppendRpc(XmlElement dmlEntry)
    {
        XmlDocument owner = dmlEntry.OwnerDocument;
        var rpc = owner.CreateElement("responsiblePartnerCompany");
        dmlEntry.AppendChild(rpc);
        if (_defaultRpcCode != null)
        {
            rpc.SetAttribute("enterpriseCode", _defaultRpcCode);
        }
        if (_defaultRpcName != null)
        {
            var name = owner.CreateElement("enterpriseName");
            name.InnerText = _defaultRpcName;
            rpc.AppendChild(name);
        }
    }

    /* ----- SNS-generated DMRL ----- */

    private void AddSns(XmlElement content, string path, string incode, TextWriter stderr)
    {
        XmlDocument doc;
        try
        {
            doc = XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Specified BREX DM could not be read: {path}");
            throw new ExitException(ExitBadSns);
        }

        // Parse "%3s%1s-%1s": infoCode[-variant][-itemLocation].
        string infoCode, variant = "A", itemloc = "D";
        int dash = incode.IndexOf('-');
        string head = dash < 0 ? incode : incode[..dash];
        if (head.Length < 1)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Bad info code: {incode}");
            throw new ExitException(ExitBadInput);
        }
        infoCode = head.Length <= 3 ? head : head[..3];
        if (head.Length > 3)
        {
            variant = head.Substring(3, 1);
        }
        if (dash >= 0 && dash + 1 < incode.Length)
        {
            itemloc = incode.Substring(dash + 1, 1);
        }

        var xsl = LoadStylesheet("newdml/sns2dmrl.xsl");
        var args = new XsltArgumentList();
        args.AddParam("infoCode", "", infoCode);
        args.AddParam("infoCodeVariant", "", variant);
        args.AddParam("itemLocationCode", "", itemloc);
        if (_defaultRpcCode != null) args.AddParam("RPCcode", "", _defaultRpcCode);
        if (_defaultRpcName != null) args.AddParam("RPCname", "", _defaultRpcName);

        XmlDocument result = Transform(xsl, doc, args);

        if (result.DocumentElement != null)
        {
            foreach (XmlNode child in result.DocumentElement.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                {
                    content.AppendChild(content.OwnerDocument.ImportNode(child, true));
                }
            }
        }
    }

    /* ----- sorting ----- */

    private void SortEntries(XmlDocument doc)
    {
        var xsl = LoadStylesheet("newdml/sort.xsl");
        XmlDocument result = Transform(xsl, doc, null);
        if (result.DocumentElement != null)
        {
            var imported = doc.ImportNode(result.DocumentElement, true);
            doc.ReplaceChild(imported, doc.DocumentElement!);
        }
    }

    /// <summary>
    /// Down-issue the document to an older S1000D issue using the shared
    /// <c>common/to*.xsl</c> stylesheets (embedded under
    /// <c>Resources/newdm/common/</c>). Mirrors <c>toissue</c>.
    /// </summary>
    private static XmlDocument ToIssue(XmlDocument doc, string issue)
    {
        string? resource = issue switch
        {
            "5.0" => "newdm/common/to50.xsl",
            "4.2" => "newdm/common/to42.xsl",
            "4.1" => "newdm/common/to41.xsl",
            "4.0" => "newdm/common/to40.xsl",
            "3.0" => "newdm/common/to30.xsl",
            "2.3" => "newdm/common/to23.xsl",
            "2.2" => "newdm/common/to22.xsl",
            "2.1" => "newdm/common/to21.xsl",
            "2.0" => "newdm/common/to20.xsl",
            _ => null,
        };
        if (resource == null) return doc;

        XslCompiledTransform xslt = LoadStylesheet(resource);
        return Transform(xslt, doc, null);
    }

    private static XslCompiledTransform LoadStylesheet(string resource)
    {
        var xslt = new XslCompiledTransform();
        using Stream stream = EmbeddedResources.Open(resource)
            ?? throw new FileNotFoundException($"Embedded resource not found: {resource}");
        using var reader = XmlReader.Create(stream);
        xslt.Load(reader);
        return xslt;
    }

    private static XmlDocument Transform(XslCompiledTransform xslt, XmlDocument input, XsltArgumentList? args)
    {
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = new UTF8Encoding(false) };
        using (var writer = XmlWriter.Create(ms, settings))
        {
            xslt.Transform(input, args, writer);
        }
        ms.Position = 0;
        var result = XmlUtils.NewDocument();
        result.Load(ms);
        return result;
    }

    /* ----- help / version ----- */

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -$, --issue <issue>         Specify which S1000d issue to use.");
        stdout.WriteLine("  -@, --out <path>            Output to specified file or directory.");
        stdout.WriteLine("  -%, --templates <dir>       Use template in specified directory.");
        stdout.WriteLine("  -~, --dump-templates <dir>  Dump built-in template to directory.");
        stdout.WriteLine("  -d, --defaults <file>       Specify .defaults file name.");
        stdout.WriteLine("  -f, --overwrite             Overwrite existing file.");
        stdout.WriteLine("  -h, -?, --help              Show usage message.");
        stdout.WriteLine("  -i, --info-code <code>      Specify info code for SNS-generated DMRL.");
        stdout.WriteLine("  -l, --list                  Treat input as a list of objects to add to the new list.");
        stdout.WriteLine("  -N, --omit-issue            Omit issue/inwork from filename.");
        stdout.WriteLine("  -p, --prompt                Prompt the user for each value.");
        stdout.WriteLine("  -q, --quiet                 Don't report an error if file exists.");
        stdout.WriteLine("  -S, --sns <BREX>            Create a DMRL from SNS rules.");
        stdout.WriteLine("  -v, --verbose               Print file name of DML.");
        stdout.WriteLine("  --version                   Show version information.");
        stdout.WriteLine("  <object>...                 CSDB object(s) to add to the new list.");
        stdout.WriteLine();
        stdout.WriteLine("In addition, the following pieces of metadata can be set:");
        stdout.WriteLine("  -#, --code <code>           DML code");
        stdout.WriteLine("  -b, --brex <BREX>           BREX data module code");
        stdout.WriteLine("  -c, --security <sec>        Security classification");
        stdout.WriteLine("  -I, --date <date>           Issue date");
        stdout.WriteLine("  -m, --remarks <remarks>     Remarks");
        stdout.WriteLine("  -n, --issno <iss>           Issue number");
        stdout.WriteLine("  -R, --rpccode <CAGE>        Default RPC code");
        stdout.WriteLine("  -r, --rpcname <RPC>         Default RPC name");
        stdout.WriteLine("  -w, --inwork <inwork>       Inwork issue");
        stdout.WriteLine("  -z, --issue-type <type>     Issue type");
    }
}
