using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-newdm</c>: create a new S1000D data module from a template,
/// filling in its metadata (data module code, issue info, language, title,
/// security, RPC/originator, BREX, etc.) from command-line options and the
/// <c>.defaults</c>/<c>.dmtypes</c> configuration files.
///
/// The flagship "new" tool. It mirrors the C tool's option set, exit codes, the
/// generated data-module filename and the generated XML content. Issue 6 (the
/// default) is produced purely with the <see cref="XmlDocument"/> DOM; older
/// S1000D issues are produced by applying the corresponding downgrade XSLT
/// (the <c>common/toNN.xsl</c> stylesheets, pure XSLT 1.0).
///
/// The interactive prompt (<c>-p</c>) is not implemented as the .NET port keeps
/// IO at the edges and is exercised non-interactively; this is the one deviation
/// from the C behaviour (see the report/todo).
/// </summary>
public sealed class NewdmTool : ITool
{
    public string Name => "newdm";
    public string Description => "Create a new S1000D data module.";
    public string Version => "5.0.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitDmExists = 1;
    private const int ExitUnknownDmType = 2;
    private const int ExitBadDmc = 3;
    private const int ExitBadBrexDmc = 4;
    private const int ExitBadDate = 5;
    private const int ExitBadIssue = 6;
    private const int ExitBadTemplDir = 7;
    private const int ExitEncodingError = 8;
    private const int ExitOsError = 9;

    private const string DefaultLanguageIsoCode = "und";
    private const string DefaultCountryIsoCode = "ZZ";

    /* Default BREX codes for older issues. */
    private const string Iss22DefaultBrex = "AE-A-04-10-0301-00A-022A-D";
    private const string Iss23DefaultBrex = "AE-A-04-10-0301-00A-022A-D";
    private const string Iss30DefaultBrex = "AE-A-04-10-0301-00A-022A-D";
    private const string Iss40DefaultBrex = "S1000D-A-04-10-0301-00A-022A-D";
    private const string Iss41DefaultBrex = "S1000D-E-04-10-0301-00A-022A-D";
    private const string Iss42DefaultBrex = "S1000D-F-04-10-0301-00A-022A-D";
    private const string Iss50DefaultBrex = "S1000D-G-04-10-0301-00A-022A-D";

    private const string BrexInfocodeUse = "The information code used is not in the allowed set.";

    /// <summary>S1000D issue, ordered the same as the C enum so comparisons match.</summary>
    private enum Issue { NoIss = 0, Iss20, Iss21, Iss22, Iss23, Iss30, Iss40, Iss41, Iss42, Iss50, Iss6 }

    private const Issue DefaultIssue = Issue.Iss6;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    /* DMC component fields (mirror the static char[] in the C). */
    private string _modelIdentCode = "";
    private string _systemDiffCode = "";
    private string _systemCode = "";
    private string _subSystemCode = "";
    private string _subSubSystemCode = "";
    private string _assyCode = "";
    private string _disassyCode = "";
    private string _disassyCodeVariant = "";
    private string _infoCode = "";
    private string _infoCodeVariant = "";
    private string _itemLocationCode = "";
    private string _learnCode = "";
    private string _learnEventCode = "";

    private string _languageIsoCode = "";
    private string _countryIsoCode = "";

    private string _securityClassification = "";

    private string _issueNumber = "";
    private string _inWork = "";

    private string _rpcName = "";
    private string _originatorName = "";
    private string _rpcCode = "";
    private string _originatorCode = "";

    private string _techName = "";
    private string _infoName = "";
    private string? _infoNameVariant;

    private string _dmtype = "";

    private string _schema = "";
    private string _brexDmcode = "";
    private string? _snsFname;
    private string? _maintSns;
    private string _issueDate = "";
    private string? _issueType;

    private string? _remarks;
    private string? _skillLevelCode;

    private bool _noIssue;

    private Issue _issue = Issue.NoIss;

    private string? _templateDir;
    private int _snsTitleLevels;
    private bool _noInfoName;
    private string? _actDmcode;

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            return RunCore(args, stdout, stderr);
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }
    }

    private int RunCore(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? defaultsFname = null;
        string? dmtypesFname = null;
        bool customDefaults = false;
        bool customDmtypes = false;

        bool verbose = false;
        bool overwrite = false;
        string? outArg = null;
        bool techNameFlag = false;
        bool noOverwriteError = false;
        string dmcode = "";
        bool genBrexRules = false;
        string? brexmapFname = null;

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
                case "-a" or "--act":
                    _actDmcode = NextArg(args, ref i, a, stderr);
                    break;
                case "-p" or "--prompt":
                    // Interactive prompting is not supported in the library port.
                    break;
                case "-d" or "--defaults":
                    defaultsFname = NextArg(args, ref i, a, stderr);
                    customDefaults = true;
                    break;
                case "-D" or "--dmtypes":
                    dmtypesFname = NextArg(args, ref i, a, stderr);
                    customDmtypes = true;
                    break;
                case "-L" or "--language":
                    _languageIsoCode = NextArg(args, ref i, a, stderr);
                    break;
                case "-C" or "--country":
                    _countryIsoCode = NextArg(args, ref i, a, stderr);
                    break;
                case "-n" or "--issno":
                    _issueNumber = NextArg(args, ref i, a, stderr);
                    break;
                case "-w" or "--inwork":
                    _inWork = NextArg(args, ref i, a, stderr);
                    break;
                case "-c" or "--security":
                    _securityClassification = NextArg(args, ref i, a, stderr);
                    break;
                case "-r" or "--rpcname":
                    _rpcName = NextArg(args, ref i, a, stderr);
                    break;
                case "-R" or "--rpccode":
                    _rpcCode = NextArg(args, ref i, a, stderr);
                    break;
                case "-o" or "--origname":
                    _originatorName = NextArg(args, ref i, a, stderr);
                    break;
                case "-O" or "--origcode":
                    _originatorCode = NextArg(args, ref i, a, stderr);
                    break;
                case "-t" or "--techname":
                    _techName = NextArg(args, ref i, a, stderr);
                    techNameFlag = true;
                    break;
                case "-i" or "--infoname":
                    _infoName = NextArg(args, ref i, a, stderr);
                    break;
                case "-T" or "--type":
                    _dmtype = NextArg(args, ref i, a, stderr);
                    break;
                case "-#" or "--code":
                {
                    string code = NextArg(args, ref i, a, stderr);
                    dmcode = code.Contains('-') ? code : RandomCode(code);
                    break;
                }
                case "-N" or "--omit-issue":
                    _noIssue = true;
                    break;
                case "-s" or "--schema":
                    _schema = NextArg(args, ref i, a, stderr);
                    break;
                case "-B" or "--generate-brex-rules":
                    genBrexRules = true;
                    break;
                case "-b" or "--brex":
                    _brexDmcode = NextArg(args, ref i, a, stderr);
                    break;
                case "-S" or "--sns":
                    _snsFname = NextArg(args, ref i, a, stderr);
                    break;
                case "-I" or "--date":
                    _issueDate = NextArg(args, ref i, a, stderr);
                    break;
                case "-V" or "--infoname-variant":
                    _infoNameVariant = NextArg(args, ref i, a, stderr);
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-$" or "--issue":
                    _issue = GetIssue(NextArg(args, ref i, a, stderr), stderr);
                    break;
                case "-@" or "--out":
                    outArg = NextArg(args, ref i, a, stderr);
                    break;
                case "-m" or "--remarks":
                    _remarks = NextArg(args, ref i, a, stderr);
                    break;
                case "-," or "--dump-dmtypes-xml":
                    stdout.Write(EmbeddedResources.ReadText("newdm/dmtypes.xml"));
                    return 0;
                case "-." or "--dump-dmtypes":
                    stdout.Write(EmbeddedResources.ReadText("newdm/dmtypes.txt"));
                    return 0;
                case "-%" or "--templates":
                    _templateDir = NextArg(args, ref i, a, stderr);
                    break;
                case "-q" or "--quiet":
                    noOverwriteError = true;
                    break;
                case "-M" or "--maintained-sns":
                    _maintSns = NextArg(args, ref i, a, stderr);
                    break;
                case "-P" or "--sns-levels":
                    _snsTitleLevels = Atoi(NextArg(args, ref i, a, stderr));
                    break;
                case "-!" or "--no-infoname":
                    _noInfoName = true;
                    break;
                case "-k" or "--skill":
                    _skillLevelCode = NextArg(args, ref i, a, stderr);
                    break;
                case "-j" or "--brexmap":
                    brexmapFname ??= NextArg(args, ref i, a, stderr);
                    break;
                case "-~" or "--dump-templates":
                    DumpTemplates(NextArg(args, ref i, a, stderr), stderr);
                    return 0;
                case "-z" or "--issue-type":
                    _issueType = NextArg(args, ref i, a, stderr);
                    break;
                default:
                    stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                    return 2;
            }
        }

        // Resolve config file locations (mirrors find_config).
        if (!customDefaults)
        {
            Csdb.FindConfig(Csdb.DefaultsFileName, out defaultsFname);
        }
        if (!customDmtypes)
        {
            Csdb.FindConfig(Csdb.DmTypesFileName, out dmtypesFname);
        }

        string defaultsDir = Path.GetDirectoryName(Path.GetFullPath(defaultsFname!)) ?? ".";

        XmlDocument brexmap = ReadBrexmap(brexmapFname);

        // The BREX-rule-group node is built up while reading the .defaults and
        // .dmtypes; it is assembled in an owning document so it can be imported
        // into the data module later.
        XmlDocument rulesDoc = XmlUtils.NewDocument();
        XmlElement? brexRules = null;
        if (genBrexRules)
        {
            brexRules = rulesDoc.CreateElement("structureObjectRuleGroup");
            rulesDoc.AppendChild(brexRules);

            XmlNode? def = brexmap.SelectSingleNode("//dmtypes");
            if (def is XmlElement defEl)
            {
                string? id = GetAttr(defEl, "id");
                string path = defEl.GetAttribute("path");

                XmlElement rule = rulesDoc.CreateElement("structureObjectRule");
                brexRules.AppendChild(rule);
                if (id != null)
                {
                    rule.SetAttribute("id", id);
                }
                XmlElement objpath = rulesDoc.CreateElement("objectPath");
                objpath.InnerText = path;
                objpath.SetAttribute("allowedObjectFlag", "2");
                rule.AppendChild(objpath);
                XmlElement objuse = rulesDoc.CreateElement("objectUse");
                objuse.InnerText = BrexInfocodeUse;
                rule.AppendChild(objuse);
            }
        }

        ReadDefaults(defaultsFname!, brexmap, brexRules, rulesDoc);

        if (dmcode == "-")
        {
            dmcode = RandomCode(_modelIdentCode);
        }

        if (dmcode != "")
        {
            ParseDmCode(dmcode, stderr, ExitBadDmc, isBrex: false);
        }

        if (_dmtype == "" || (_infoName == "" && !_noInfoName))
        {
            ReadDmTypes(dmtypesFname!, brexRules, rulesDoc);
        }

        ValidateRequiredDmc(stderr);

        if (!techNameFlag && (_maintSns != null || _snsFname != null || _brexDmcode != ""))
        {
            SetTechFromSns(defaultsDir);
        }

        if (_issue == Issue.NoIss) _issue = DefaultIssue;
        if (_issueNumber == "") _issueNumber = "000";
        if (_inWork == "") _inWork = "01";
        if (_securityClassification == "") _securityClassification = "01";

        SetEnvLang();
        _languageIsoCode = _languageIsoCode.ToLowerInvariant();
        _countryIsoCode = _countryIsoCode.ToUpperInvariant();

        if (_snsTitleLevels == 0) _snsTitleLevels = 1;

        XmlDocument dm = XmlSkeleton(_dmtype, _issue, stderr);

        FillMetadata(dm, brexRules);

        // BREX for older issues without an explicit -b.
        if ((int)_issue < (int)Issue.Iss6)
        {
            if (_brexDmcode == "")
            {
                string? brex = _issue switch
                {
                    Issue.Iss22 => Iss22DefaultBrex,
                    Issue.Iss23 => Iss23DefaultBrex,
                    Issue.Iss30 => Iss30DefaultBrex,
                    Issue.Iss40 => Iss40DefaultBrex,
                    Issue.Iss41 => Iss41DefaultBrex,
                    Issue.Iss42 => Iss42DefaultBrex,
                    Issue.Iss50 => Iss50DefaultBrex,
                    _ => null,
                };
                if (brex != null)
                {
                    SetDmCodeFromFilename(dm.SelectSingleNode("//brexDmRef/dmRef/dmRefIdent/dmCode"), brex, stderr);
                }
            }

            dm = ToIssue(dm, _issue);
        }

        // Build the issue / learn fragments for the filename.
        string learn = "";
        if (_learnCode != "" && _learnEventCode != "")
        {
            learn = $"-{_learnCode}{_learnEventCode}";
        }

        string iss = "";
        if (!_noIssue)
        {
            iss = $"_{_issueNumber}-{_inWork}";
        }

        string upperLang = _languageIsoCode.ToUpperInvariant();

        // Determine output destination.
        string? outDir = null;
        string? outFile = outArg;
        if (outFile != null && Directory.Exists(outFile))
        {
            outDir = outFile;
            outFile = null;
        }

        if (outFile == null)
        {
            outFile = string.Format(CultureInfo.InvariantCulture,
                "DMC-{0}-{1}-{2}-{3}{4}-{5}-{6}{7}-{8}{9}-{10}{11}{12}_{13}-{14}.XML",
                _modelIdentCode,
                _systemDiffCode,
                _systemCode,
                _subSystemCode,
                _subSubSystemCode,
                _assyCode,
                _disassyCode,
                _disassyCodeVariant,
                _infoCode,
                _infoCodeVariant,
                _itemLocationCode,
                learn,
                iss,
                upperLang,
                _countryIsoCode);
        }

        string targetPath = outDir != null ? Path.Combine(outDir, outFile) : outFile;

        if (!overwrite && File.Exists(targetPath))
        {
            if (noOverwriteError) return 0;
            if (outDir != null)
            {
                stderr.WriteLine($"{Name}: ERROR: {outDir}/{outFile} already exists. Use -f to overwrite.");
            }
            else
            {
                stderr.WriteLine($"{Name}: ERROR: {outFile} already exists. Use -f to overwrite.");
            }
            throw new ExitException(ExitDmExists);
        }

        try
        {
            XmlUtils.SaveDoc(dm, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{Name}: ERROR: Could not write {targetPath}: {ex.Message}");
            throw new ExitException(ExitOsError);
        }

        if (verbose)
        {
            stdout.WriteLine(outDir != null ? $"{outDir}/{outFile}" : outFile);
        }

        return 0;
    }

    /* ----- metadata filling (DOM, mirrors the second half of main) ----- */

    private void FillMetadata(XmlDocument dm, XmlElement? brexRules)
    {
        XmlElement dmodule = dm.DocumentElement!;
        XmlElement identAndStatus = FindChild(dmodule, "identAndStatusSection")!;
        XmlElement dmAddress = FindChild(identAndStatus, "dmAddress")!;
        XmlElement dmIdent = FindChild(dmAddress, "dmIdent")!;
        XmlElement dmCode = FindChild(dmIdent, "dmCode")!;
        XmlElement language = FindChild(dmIdent, "language")!;
        XmlElement issueInfo = FindChild(dmIdent, "issueInfo")!;
        XmlElement dmAddressItems = FindChild(dmAddress, "dmAddressItems")!;
        XmlElement issueDate = FindChild(dmAddressItems, "issueDate")!;
        XmlElement dmStatus = FindChild(identAndStatus, "dmStatus")!;
        XmlElement security = FindChild(dmStatus, "security")!;
        XmlElement dmTitle = FindChild(dmAddressItems, "dmTitle")!;
        XmlElement techName = FindChild(dmTitle, "techName")!;
        XmlElement? infoName = FindChild(dmTitle, "infoName");
        XmlElement? infoNameVariant = FindChild(dmTitle, "infoNameVariant");

        if (_schema != "")
        {
            // The template already declares xmlns:xsi; set the prefixed attribute
            // against that namespace so it round-trips as xsi:noNamespaceSchemaLocation.
            const string xsiNs = "http://www.w3.org/2001/XMLSchema-instance";
            XmlAttribute attr = dm.CreateAttribute("xsi", "noNamespaceSchemaLocation", xsiNs);
            attr.Value = _schema;
            dmodule.SetAttributeNode(attr);
        }

        dmCode.SetAttribute("modelIdentCode", _modelIdentCode);
        dmCode.SetAttribute("systemDiffCode", _systemDiffCode);
        dmCode.SetAttribute("systemCode", _systemCode);
        dmCode.SetAttribute("subSystemCode", _subSystemCode);
        dmCode.SetAttribute("subSubSystemCode", _subSubSystemCode);
        dmCode.SetAttribute("assyCode", _assyCode);
        dmCode.SetAttribute("disassyCode", _disassyCode);
        dmCode.SetAttribute("disassyCodeVariant", _disassyCodeVariant);
        dmCode.SetAttribute("infoCode", _infoCode);
        dmCode.SetAttribute("infoCodeVariant", _infoCodeVariant);
        dmCode.SetAttribute("itemLocationCode", _itemLocationCode);

        if (_learnCode != "") dmCode.SetAttribute("learnCode", _learnCode);
        if (_learnEventCode != "") dmCode.SetAttribute("learnEventCode", _learnEventCode);

        language.SetAttribute("languageIsoCode", _languageIsoCode);
        language.SetAttribute("countryIsoCode", _countryIsoCode);

        issueInfo.SetAttribute("issueNumber", _issueNumber);
        issueInfo.SetAttribute("inWork", _inWork);

        SetIssueDate(issueDate);

        if (_issueType != null) dmStatus.SetAttribute("issueType", _issueType);

        // SB DMs also contain an "original issue date".
        if (_dmtype == "sb")
        {
            if (dm.SelectSingleNode("//sbOriginalIssueDate/issueDate") is XmlElement sbDate)
            {
                SetIssueDate(sbDate);
            }
        }

        security.SetAttribute("securityClassification", _securityClassification);

        techName.InnerText = _techName;

        if (_infoName == "")
        {
            infoName?.ParentNode?.RemoveChild(infoName);
        }
        else if (infoName != null)
        {
            infoName.InnerText = _infoName;
        }

        if (_infoNameVariant != null && infoNameVariant != null)
        {
            infoNameVariant.InnerText = _infoNameVariant;
        }
        else
        {
            infoNameVariant?.ParentNode?.RemoveChild(infoNameVariant);
        }

        XmlElement rpc = FindChild(dmStatus, "responsiblePartnerCompany")!;
        if (_rpcCode != "")
        {
            rpc.SetAttribute("enterpriseCode", _rpcCode);
        }
        if (_rpcName != "")
        {
            if (dm.SelectSingleNode("//responsiblePartnerCompany/enterpriseName") is XmlElement node)
            {
                node.InnerText = _rpcName;
            }
            else
            {
                XmlElement en = dm.CreateElement("enterpriseName");
                en.InnerText = _rpcName;
                rpc.AppendChild(en);
            }
        }

        XmlElement originator = FindChild(dmStatus, "originator")!;
        if (_originatorCode != "")
        {
            originator.SetAttribute("enterpriseCode", _originatorCode);
        }
        if (_originatorName != "")
        {
            if (dm.SelectSingleNode("//originator/enterpriseName") is XmlElement node)
            {
                node.InnerText = _originatorName;
            }
            else
            {
                XmlElement en = dm.CreateElement("enterpriseName");
                en.InnerText = _originatorName;
                originator.AppendChild(en);
            }
        }

        SetSkillLevel(dm, _skillLevelCode);
        SetRemarks(dm, _remarks);

        if (_actDmcode != null)
        {
            SetDmCodeFromFilename(dm.SelectSingleNode("//applicCrossRefTableRef/dmRef/dmRefIdent/dmCode"), _actDmcode, NullWriter, isAct: true);
        }
        else
        {
            XmlNode? act = dm.SelectSingleNode("//applicCrossRefTableRef");
            act?.ParentNode?.RemoveChild(act);
        }

        if (_brexDmcode != "")
        {
            SetDmCodeFromFilename(dm.SelectSingleNode("//brexDmRef/dmRef/dmRefIdent/dmCode"), _brexDmcode, NullWriter);
        }

        if (brexRules != null)
        {
            if (dm.SelectSingleNode("//contextRules") is XmlNode contextRules)
            {
                XmlNode imported = dm.ImportNode(brexRules, true);
                contextRules.AppendChild(imported);
            }
        }
    }

    private static readonly TextWriter NullWriter = TextWriter.Null;

    /* ----- defaults / dmtypes reading ----- */

    private void ReadDefaults(string fname, XmlDocument brexmap, XmlElement? brexRules, XmlDocument rulesDoc)
    {
        XmlDocument? xml = TryReadXml(fname);
        if (xml != null)
        {
            foreach (XmlNode cur in ChildElements(xml.DocumentElement))
            {
                var el = (XmlElement)cur;
                if (!el.HasAttribute("ident") || !el.HasAttribute("value")) continue;
                string key = el.GetAttribute("ident");
                string val = el.GetAttribute("value");
                CopyDefaultValue(key, val);
                if (brexRules != null) AddBrexRule(brexRules, rulesDoc, brexmap, key, val);
            }
            return;
        }

        // Plain-text .defaults: "<key> <value...>".
        if (!File.Exists(fname)) return;
        foreach (string line in File.ReadLines(fname))
        {
            if (!TryScanKeyVal(line, out string key, out string val)) continue;
            CopyDefaultValue(key, val);
            if (brexRules != null) AddBrexRule(brexRules, rulesDoc, brexmap, key, val);
        }
    }

    private void ReadDmTypes(string fname, XmlElement? brexRules, XmlDocument rulesDoc)
    {
        XmlDocument? xml = TryReadXml(fname);
        if (xml != null)
        {
            ProcessDmTypesXml(xml, brexRules, rulesDoc);
            return;
        }

        if (File.Exists(fname))
        {
            foreach (string line in File.ReadLines(fname))
            {
                if (!TryScanThree(line, out string key, out string val, out string? infName)) continue;
                ApplyDmType(key, val, infName);
                if (brexRules != null) AddDmTypesBrexVal(brexRules, rulesDoc, key, infName);
            }
            return;
        }

        // Fall back to the embedded default dmtypes XML.
        XmlDocument builtin = EmbeddedResources.LoadXml("newdm/dmtypes.xml");
        ProcessDmTypesXml(builtin, brexRules, rulesDoc);
    }

    private void ProcessDmTypesXml(XmlDocument doc, XmlElement? brexRules, XmlDocument rulesDoc)
    {
        foreach (XmlNode cur in ChildElements(doc.DocumentElement))
        {
            var el = (XmlElement)cur;
            if (!el.HasAttribute("infoCode") || !el.HasAttribute("schema")) continue;
            string key = el.GetAttribute("infoCode");
            string val = el.GetAttribute("schema");
            string? infName = el.HasAttribute("infoName") ? el.GetAttribute("infoName") : null;
            string? infNameVar = el.HasAttribute("infoNameVariant") ? el.GetAttribute("infoNameVariant") : null;

            ApplyDmType(key, val, infName, infNameVar);
            if (brexRules != null) AddDmTypesBrexVal(brexRules, rulesDoc, key, infName);
        }
    }

    /// <summary>Match a dmtypes key against the current info code components and
    /// apply the schema/info-name (mirrors the shared matching logic).</summary>
    private void ApplyDmType(string key, string schemaVal, string? infName, string? infNameVar = null)
    {
        // sscanf(def_key, "%3s%1s-%1s-%3s%1s", code, variant, itemloc, learn, levent)
        ScanKey(key, out string code, out string variant, out string itemloc,
            out string learn, out string levent, out int p);

        bool match =
            code == _infoCode &&
            (p < 2 || variant == "*" || variant == _infoCodeVariant) &&
            (p < 3 || itemloc == "*" || itemloc == _itemLocationCode) &&
            (p < 4 || learn == "***" || learn == _learnCode) &&
            (p < 5 || levent == "*" || levent == _learnEventCode);

        if (_dmtype == "" && match)
        {
            _dmtype = schemaVal;
        }

        if (infName != null && _infoName == "" && !_noInfoName && match)
        {
            _infoName = infName;
            if (infNameVar != null && _infoNameVariant == null)
            {
                _infoNameVariant = infNameVar;
            }
        }
    }

    private static void ScanKey(string key, out string code, out string variant,
        out string itemloc, out string learn, out string levent, out int p)
    {
        // Format: %3s%1s-%1s-%3s%1s  -> code(3) variant(1) - itemloc(1) - learn(3) levent(1)
        code = ""; variant = ""; itemloc = ""; learn = ""; levent = ""; p = 0;

        string[] parts = key.Split('-');
        if (parts.Length == 0 || parts[0].Length == 0) return;

        string first = parts[0];
        code = Take(first, 3);
        p = 1;
        if (first.Length > 3)
        {
            variant = first.Substring(3, 1);
            p = 2;
        }
        if (parts.Length < 2) return;
        itemloc = Take(parts[1], 1);
        p = 3;
        if (parts.Length < 3) return;
        string third = parts[2];
        learn = Take(third, 3);
        p = 4;
        if (third.Length > 3)
        {
            levent = third.Substring(3, 1);
            p = 5;
        }
    }

    private void CopyDefaultValue(string key, string val)
    {
        switch (key)
        {
            case "modelIdentCode": SetIfEmpty(ref _modelIdentCode, val); break;
            case "systemDiffCode": SetIfEmpty(ref _systemDiffCode, val); break;
            case "systemCode": SetIfEmpty(ref _systemCode, val); break;
            case "subSystemCode": SetIfEmpty(ref _subSystemCode, val); break;
            case "subSubSystemCode": SetIfEmpty(ref _subSubSystemCode, val); break;
            case "assyCode": SetIfEmpty(ref _assyCode, val); break;
            case "disassyCode": SetIfEmpty(ref _disassyCode, val); break;
            case "disassyCodeVariant": SetIfEmpty(ref _disassyCodeVariant, val); break;
            case "infoCode": SetIfEmpty(ref _infoCode, val); break;
            case "infoCodeVariant": SetIfEmpty(ref _infoCodeVariant, val); break;
            case "itemLocationCode": SetIfEmpty(ref _itemLocationCode, val); break;
            case "learnCode": SetIfEmpty(ref _learnCode, val); break;
            case "learnEventCode": SetIfEmpty(ref _learnEventCode, val); break;
            case "languageIsoCode": SetIfEmpty(ref _languageIsoCode, val); break;
            case "countryIsoCode": SetIfEmpty(ref _countryIsoCode, val); break;
            case "issueNumber": SetIfEmpty(ref _issueNumber, val); break;
            case "inWork": SetIfEmpty(ref _inWork, val); break;
            case "securityClassification": SetIfEmpty(ref _securityClassification, val); break;
            case "responsiblePartnerCompany": SetIfEmpty(ref _rpcName, val); break;
            case "responsiblePartnerCompanyCode": SetIfEmpty(ref _rpcCode, val); break;
            case "originator": SetIfEmpty(ref _originatorName, val); break;
            case "originatorCode": SetIfEmpty(ref _originatorCode, val); break;
            case "techName": SetIfEmpty(ref _techName, val); break;
            case "infoName":
                if (_infoName == "" && !_noInfoName) _infoName = val;
                break;
            case "infoNameVariant": _infoNameVariant ??= val; break;
            case "schema": SetIfEmpty(ref _schema, val); break;
            case "brex": SetIfEmpty(ref _brexDmcode, val); break;
            case "sns": _snsFname ??= val; break;
            case "issue": if (_issue == Issue.NoIss) _issue = GetIssue(val, NullWriter); break;
            case "remarks": _remarks ??= val; break;
            case "templates": _templateDir ??= val; break;
            case "maintainedSns": _maintSns ??= val; break;
            case "snsLevels": if (_snsTitleLevels == 0) _snsTitleLevels = Atoi(val); break;
            case "skillLevelCode": _skillLevelCode ??= val; break;
            case "act": _actDmcode ??= val; break;
            case "issueType": _issueType ??= val; break;
        }
    }

    private static void SetIfEmpty(ref string field, string val)
    {
        if (field == "") field = val;
    }

    /* ----- BREX rule generation ----- */

    private static void AddBrexRule(XmlElement rules, XmlDocument rulesDoc, XmlDocument brexmap, string key, string val)
    {
        if (brexmap.SelectSingleNode($"//default[@ident=\"{key}\"]") is not XmlElement def)
        {
            return;
        }

        string? id = GetAttr(def, "id");
        string path = def.GetAttribute("path");
        string use = $"{key} must be {val}";

        XmlElement rule = rulesDoc.CreateElement("structureObjectRule");
        rules.AppendChild(rule);
        if (id != null) rule.SetAttribute("id", id);

        XmlElement objpath = rulesDoc.CreateElement("objectPath");
        objpath.InnerText = path;
        objpath.SetAttribute("allowedObjectFlag", "2");
        rule.AppendChild(objpath);

        XmlElement objuse = rulesDoc.CreateElement("objectUse");
        objuse.InnerText = use;
        rule.AppendChild(objuse);

        XmlElement objval = rulesDoc.CreateElement("objectValue");
        objval.SetAttribute("valueAllowed", val);
        rule.AppendChild(objval);
    }

    private static void AddDmTypesBrexVal(XmlElement rules, XmlDocument rulesDoc, string key, string? val)
    {
        // C appends to rules->children (the dmtypes rule added first).
        XmlNode? parent = rules.FirstChild;
        if (parent == null) return;

        XmlElement objval = rulesDoc.CreateElement("objectValue");
        objval.SetAttribute("valueAllowed", key);
        if (val != null) objval.InnerText = val;
        parent.AppendChild(objval);
    }

    /* ----- SNS title resolution ----- */

    private void SetTechFromSns(string dir)
    {
        XmlDocument? brex = null;

        if (_maintSns != null)
        {
            string? res = MaintSnsResource(_maintSns);
            if (res != null) brex = EmbeddedResources.LoadXml(res);
        }
        else if (_snsFname != null && FindBrexFile(dir, _snsFname, out string snsPath))
        {
            brex = XmlUtils.ReadDoc(snsPath);
        }
        else if (_brexDmcode != "" && FindBrexFile(dir, _brexDmcode, out string brexPath))
        {
            brex = XmlUtils.ReadDoc(brexPath);
        }

        if (brex == null) return;

        string[] xpaths =
        {
            $"//snsSystem[snsCode='{_systemCode}']/snsSubSystem[snsCode='{_subSystemCode}']/snsSubSubSystem[snsCode='{_subSubSystemCode}']/snsAssy[snsCode='{_assyCode}']/snsTitle",
            $"//snsSystem[snsCode='{_systemCode}']/snsSubSystem[snsCode='{_subSystemCode}']/snsSubSubSystem[snsCode='{_subSubSystemCode}']/snsTitle",
            $"//snsSystem[snsCode='{_systemCode}']/snsSubSystem[snsCode='{_subSystemCode}']/snsTitle",
            $"//snsSystem[snsCode='{_systemCode}']/snsTitle",
        };

        foreach (string xp in xpaths)
        {
            if (brex.SelectSingleNode(xp) is XmlNode snsTitle)
            {
                SetSnsTitle(snsTitle);
                return;
            }
        }
    }

    private void SetSnsTitle(XmlNode snsTitle)
    {
        string title = snsTitle.InnerText;
        string last = title;

        _techName = "";

        XmlNode? cur = snsTitle;
        for (int n = _snsTitleLevels; n > 1 && cur != null; --n, cur = cur.ParentNode)
        {
            XmlNode? prev = cur.SelectSingleNode("parent::*/parent::*/snsTitle");
            if (prev == null) break;

            string p = prev.InnerText;
            if (p != last)
            {
                _techName = $"{p} - {_techName}";
            }
            last = p;
        }

        _techName += title;
    }

    private static string? MaintSnsResource(string maintSns)
    {
        return maintSns.ToLowerInvariant() switch
        {
            "generic" => "newdm/sns/DMC-S1000D-A-08-02-0100-00A-022A-D_EN-CA.XML",
            "support and training equipment" => "newdm/sns/DMC-S1000D-A-08-02-0200-00A-022A-D_EN-CA.XML",
            "ordnance" => "newdm/sns/DMC-S1000D-A-08-02-0300-00A-022A-D_EN-CA.XML",
            "general communications" => "newdm/sns/DMC-S1000D-A-08-02-0400-00A-022A-D_EN-CA.XML",
            "air vehicle, engines and equipment" => "newdm/sns/DMC-S1000D-A-08-02-0500-00A-022A-D_EN-CA.XML",
            "tactical missiles" => "newdm/sns/DMC-S1000D-A-08-02-0600-00A-022A-D_EN-CA.XML",
            "general surface vehicles" => "newdm/sns/DMC-S1000D-A-08-02-0700-00A-022A-D_EN-CA.XML",
            "general sea vehicles" => "newdm/sns/DMC-S1000D-A-08-02-0800-00A-022A-D_EN-CA.XML",
            _ => null,
        };
    }

    /// <summary>
    /// Find the latest version of a BREX/SNS data module by code in
    /// <paramref name="dir"/> (mirrors find_brex_file + find_csdb_object,
    /// non-recursive). The code may already carry the <c>DMC-</c> prefix.
    /// </summary>
    private static bool FindBrexFile(string dir, string code, out string path)
    {
        path = "";
        string pattern = code.StartsWith("DMC-", StringComparison.Ordinal) ? code : "DMC-" + code;

        if (!Directory.Exists(dir)) return false;

        string? best = null;
        // Mirrors find_csdb_object(..., is=NULL, recursive=true): match any file
        // whose base name begins with the code, keeping the latest by base name.
        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (!Csdb.StrMatch(pattern, name)) continue;
            if (best == null || Csdb.CompareBaseName(name, Path.GetFileName(best)) > 0)
            {
                best = file;
            }
        }

        if (best == null) return false;
        path = best;
        return true;
    }

    /* ----- skeleton selection ----- */

    private XmlDocument XmlSkeleton(string dmtype, Issue iss, TextWriter stderr)
    {
        if (dmtype == "")
        {
            if (_learnCode == "")
            {
                stderr.WriteLine($"{Name}: ERROR: No schema defined for information type {_infoCode}{_infoCodeVariant}-{_itemLocationCode}");
            }
            else
            {
                stderr.WriteLine($"{Name}: ERROR: No schema defined for information type {_infoCode}{_infoCodeVariant}-{_itemLocationCode}-{_learnCode}{_learnEventCode}");
            }
            throw new ExitException(ExitUnknownDmType);
        }

        if (_templateDir != null)
        {
            string src = Path.Combine(_templateDir, dmtype + ".xml");
            if (!File.Exists(src))
            {
                stderr.WriteLine($"{Name}: ERROR: No schema {dmtype} in template directory \"{_templateDir}\".");
                throw new ExitException(ExitUnknownDmType);
            }
            return XmlUtils.ReadDoc(src);
        }

        if (!TemplateExistsForType(dmtype))
        {
            stderr.WriteLine($"{Name}: ERROR: Unknown schema {dmtype}");
            throw new ExitException(ExitUnknownDmType);
        }

        if (!TemplateAvailableForIssue(dmtype, iss))
        {
            stderr.WriteLine($"{Name}: ERROR: No schema {dmtype} for issue {IssueName(iss)}");
            throw new ExitException(ExitUnknownDmType);
        }

        // BREX with a maintained SNS uses the SNS document as the skeleton.
        if (dmtype == "brex" && _maintSns != null)
        {
            string? res = MaintSnsResource(_maintSns);
            if (res == null)
            {
                stderr.WriteLine($"{Name}: ERROR: No maintained SNS: {_maintSns}");
                throw new ExitException(ExitUnknownDmType);
            }
            return EmbeddedResources.LoadXml(res);
        }

        return EmbeddedResources.LoadXml($"newdm/templates/{dmtype}.xml");
    }

    private static bool TemplateExistsForType(string dmtype) =>
        Array.IndexOf(AllTemplateTypes, dmtype) >= 0;

    private static readonly string[] AllTemplateTypes =
    {
        "descript", "proced", "frontmatter", "brex", "brdoc", "appliccrossreftable",
        "prdcrossreftable", "condcrossreftable", "comrep", "process", "ipd", "fault",
        "checklist", "learning", "container", "crew", "sb", "schedul", "wrngdata",
        "wrngflds", "scocontent", "techrep",
    };

    /// <summary>
    /// Whether a built-in template exists for the given type at the given issue,
    /// reproducing the per-type issue switch in the C <c>xml_skeleton</c>.
    /// </summary>
    private static bool TemplateAvailableForIssue(string dmtype, Issue iss)
    {
        int v = (int)iss;
        int I20 = (int)Issue.Iss20, I22 = (int)Issue.Iss22,
            I23 = (int)Issue.Iss23, I30 = (int)Issue.Iss30, I40 = (int)Issue.Iss40,
            I41 = (int)Issue.Iss41, I42 = (int)Issue.Iss42, I50 = (int)Issue.Iss50,
            I6 = (int)Issue.Iss6;

        return dmtype switch
        {
            "descript" or "proced" or "process" or "ipd" or "fault" or "crew" or
            "schedul" or "wrngdata" or "wrngflds" => v >= I20 && v <= I6,
            "frontmatter" or "comrep" or "sb" or "scocontent" => v is var x && (x == I41 || x == I42 || x == I50 || x == I6),
            "brex" => v == I22 || v == I23 || v == I30 || (v >= I40 && v <= I6),
            "brdoc" => v == I42 || v == I50 || v == I6,
            "appliccrossreftable" or "prdcrossreftable" or "condcrossreftable" => v >= I30 && v <= I6,
            "checklist" or "learning" => v >= I40 && v <= I6,
            "container" => v >= I23 && v <= I6,
            "techrep" => v == I23 || v == I30 || v == I40,
            _ => false,
        };
    }

    /* ----- downgrade XSLT ----- */

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
        using (Stream s = EmbeddedResources.Open(xsl)!)
        using (var reader = XmlReader.Create(s))
        {
            transform.Load(reader);
        }

        // The C tool copies the original doctype across by re-rooting; here we
        // transform into a new document, preserving whitespace.
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

    /* ----- small DOM helpers ----- */

    private void SetIssueDate(XmlElement issueDate)
    {
        string yearS, monthS, dayS;
        if (_issueDate == "")
        {
            DateTime now = DateTime.Now;
            yearS = now.Year.ToString(CultureInfo.InvariantCulture);
            monthS = now.Month.ToString("D2", CultureInfo.InvariantCulture);
            dayS = now.Day.ToString("D2", CultureInfo.InvariantCulture);
        }
        else
        {
            string[] parts = _issueDate.Split('-');
            if (parts.Length < 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
            {
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

    private static void SetRemarks(XmlDocument doc, string? text)
    {
        if (doc.SelectSingleNode("//remarks") is not XmlElement remarks) return;
        if (text != null)
        {
            XmlElement sp = doc.CreateElement("simplePara");
            sp.InnerText = text;
            remarks.AppendChild(sp);
        }
        else
        {
            remarks.ParentNode?.RemoveChild(remarks);
        }
    }

    private static void SetSkillLevel(XmlDocument doc, string? code)
    {
        if (doc.SelectSingleNode("//skillLevel") is not XmlElement skill) return;
        if (code != null)
        {
            skill.SetAttribute("skillLevelCode", code);
        }
        else
        {
            skill.ParentNode?.RemoveChild(skill);
        }
    }

    /// <summary>
    /// Set a dmCode element's attributes from a (BREX/ACT) data module code or
    /// filename. Mirrors set_dmcode/set_brex/set_act.
    /// </summary>
    private void SetDmCodeFromFilename(XmlNode? dmCodeNode, string fname, TextWriter stderr, bool isBrex = true, bool isAct = false)
    {
        if (dmCodeNode is not XmlElement dmCode) return;

        string code = Path.GetFileName(fname);
        if (code.StartsWith("DMC-", StringComparison.Ordinal))
        {
            code = code[4..];
        }

        var fields = new DmcFields();
        int n = ParseDmcInto(code, fields);
        if (n != 11 && n != 13)
        {
            stderr.WriteLine($"{Name}: ERROR: Bad BREX data module code.");
            throw new ExitException(ExitBadBrexDmc);
        }

        dmCode.SetAttribute("modelIdentCode", fields.ModelIdentCode);
        dmCode.SetAttribute("systemDiffCode", fields.SystemDiffCode);
        dmCode.SetAttribute("systemCode", fields.SystemCode);
        dmCode.SetAttribute("subSystemCode", fields.SubSystemCode);
        dmCode.SetAttribute("subSubSystemCode", fields.SubSubSystemCode);
        dmCode.SetAttribute("assyCode", fields.AssyCode);
        dmCode.SetAttribute("disassyCode", fields.DisassyCode);
        dmCode.SetAttribute("disassyCodeVariant", fields.DisassyCodeVariant);
        dmCode.SetAttribute("infoCode", fields.InfoCode);
        dmCode.SetAttribute("infoCodeVariant", fields.InfoCodeVariant);
        dmCode.SetAttribute("itemLocationCode", fields.ItemLocationCode);
        if (fields.LearnCode != "") dmCode.SetAttribute("learnCode", fields.LearnCode);
        if (fields.LearnEventCode != "") dmCode.SetAttribute("learnEventCode", fields.LearnEventCode);
    }

    /// <summary>Parse a DMC string into the tool's component fields.</summary>
    private void ParseDmCode(string dmcode, TextWriter stderr, int errExit, bool isBrex)
    {
        string code = dmcode;
        if (code.StartsWith("DMC-", StringComparison.Ordinal))
        {
            code = code[4..];
        }

        var fields = new DmcFields();
        int n = ParseDmcInto(code, fields);
        if (n != 11 && n != 13)
        {
            stderr.WriteLine($"{Name}: ERROR: Bad data module code: {dmcode}");
            throw new ExitException(errExit);
        }

        _modelIdentCode = fields.ModelIdentCode;
        _systemDiffCode = fields.SystemDiffCode;
        _systemCode = fields.SystemCode;
        _subSystemCode = fields.SubSystemCode;
        _subSubSystemCode = fields.SubSubSystemCode;
        _assyCode = fields.AssyCode;
        _disassyCode = fields.DisassyCode;
        _disassyCodeVariant = fields.DisassyCodeVariant;
        _infoCode = fields.InfoCode;
        _infoCodeVariant = fields.InfoCodeVariant;
        _itemLocationCode = fields.ItemLocationCode;
        _learnCode = fields.LearnCode;
        _learnEventCode = fields.LearnEventCode;
    }

    private sealed class DmcFields
    {
        public string ModelIdentCode = "";
        public string SystemDiffCode = "";
        public string SystemCode = "";
        public string SubSystemCode = "";
        public string SubSubSystemCode = "";
        public string AssyCode = "";
        public string DisassyCode = "";
        public string DisassyCodeVariant = "";
        public string InfoCode = "";
        public string InfoCodeVariant = "";
        public string ItemLocationCode = "";
        public string LearnCode = "";
        public string LearnEventCode = "";
    }

    /// <summary>
    /// Reproduce the C sscanf:
    /// "%14[^-]-%4[^-]-%3[^-]-%c%c-%4[^-]-%2s%3[^-]-%3s%c-%c-%3s%1s".
    /// Returns the number of fields populated (11 or 13 for a valid code).
    /// </summary>
    private static int ParseDmcInto(string code, DmcFields f)
    {
        // Tokens separated by '-':
        //  0: modelIdentCode (<=14)
        //  1: systemDiffCode (<=4)
        //  2: systemCode (<=3)
        //  3: subSystemCode(1) + subSubSystemCode(1)  -> "%c%c"
        //  4: assyCode (<=4)
        //  5: disassyCode(2) + disassyCodeVariant(<=3) -> "%2s%3[^-]"
        //  6: infoCode(3) + infoCodeVariant(1)         -> "%3s%c"
        //  7: itemLocationCode(1)                       -> "%c"
        //  8: learnCode(3)                              -> "%3s"  (optional)
        //  9: learnEventCode(1)                         -> "%1s"  (optional)
        string[] t = code.Split('-');
        int count = 0;

        if (t.Length < 1 || t[0].Length == 0) return count;
        f.ModelIdentCode = Take(t[0], 14); count++;
        if (t.Length < 2 || t[1].Length == 0) return count;
        f.SystemDiffCode = Take(t[1], 4); count++;
        if (t.Length < 3 || t[2].Length == 0) return count;
        f.SystemCode = Take(t[2], 3); count++;
        if (t.Length < 4 || t[3].Length < 2) return count;
        f.SubSystemCode = t[3].Substring(0, 1); count++;
        f.SubSubSystemCode = t[3].Substring(1, 1); count++;
        if (t.Length < 5 || t[4].Length == 0) return count;
        f.AssyCode = Take(t[4], 4); count++;
        if (t.Length < 6 || t[5].Length < 2) return count;
        f.DisassyCode = t[5].Substring(0, 2); count++;
        f.DisassyCodeVariant = Take(t[5].Substring(2), 3); count++;
        if (t.Length < 7 || t[6].Length < 3) return count;
        f.InfoCode = t[6].Substring(0, 3); count++;
        if (t[6].Length < 4) return count;
        f.InfoCodeVariant = t[6].Substring(3, 1); count++;
        if (t.Length < 8 || t[7].Length < 1) return count;
        f.ItemLocationCode = t[7].Substring(0, 1); count++;
        // Optional learn fields.
        if (t.Length < 9 || t[8].Length < 3) return count;
        f.LearnCode = t[8].Substring(0, 3); count++;
        if (t.Length < 10 || t[9].Length < 1) return count;
        f.LearnEventCode = t[9].Substring(0, 1); count++;
        return count;
    }

    private void ValidateRequiredDmc(TextWriter stderr)
    {
        if (_modelIdentCode != "" && _systemDiffCode != "" && _systemCode != "" &&
            _subSystemCode != "" && _subSubSystemCode != "" && _assyCode != "" &&
            _disassyCode != "" && _disassyCodeVariant != "" && _infoCode != "" &&
            _infoCodeVariant != "" && _itemLocationCode != "")
        {
            return;
        }

        string Q(string s) => s == "" ? "???" : s;
        stderr.Write($"{Name}: ERROR: Missing required DMC components: ");
        stderr.WriteLine(
            $"DMC-{Q(_modelIdentCode)}-{Q(_systemDiffCode)}-{Q(_systemCode)}-" +
            $"{Q(_subSystemCode)}{Q(_subSubSystemCode)}-{Q(_assyCode)}-" +
            $"{Q(_disassyCode)}{Q(_disassyCodeVariant)}-{Q(_infoCode)}{Q(_infoCodeVariant)}-{Q(_itemLocationCode)}");
        throw new ExitException(ExitBadDmc);
    }

    /* ----- env / language ----- */

    private void SetEnvLang()
    {
        string? env = Environment.GetEnvironmentVariable("LANG");
        if (env == null)
        {
            if (_languageIsoCode == "") _languageIsoCode = DefaultLanguageIsoCode;
            if (_countryIsoCode == "") _countryIsoCode = DefaultCountryIsoCode;
            return;
        }

        // strtok(lang, "_"); strtok(NULL, ".")
        string langL = "";
        string langC = "";
        int us = env.IndexOf('_');
        if (us < 0)
        {
            langL = env;
        }
        else
        {
            langL = env[..us];
            string rest = env[(us + 1)..];
            int dot = rest.IndexOf('.');
            langC = dot < 0 ? rest : rest[..dot];
        }

        if (_languageIsoCode == "")
        {
            _languageIsoCode = langL.Length > 0 ? Take(langL, 3) : DefaultLanguageIsoCode;
        }
        if (_countryIsoCode == "")
        {
            _countryIsoCode = langC.Length > 0 ? Take(langC, 2) : DefaultCountryIsoCode;
        }
    }

    /* ----- template dumping ----- */

    private void DumpTemplates(string path, TextWriter stderr)
    {
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"{Name}: ERROR: Cannot dump templates in directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }

        foreach (string type in AllTemplateTypes)
        {
            string text = EmbeddedResources.ReadText($"newdm/templates/{type}.xml");
            File.WriteAllText(Path.Combine(path, type + ".xml"), text, new UTF8Encoding(false));
        }
    }

    /* ----- brexmap ----- */

    private static XmlDocument ReadBrexmap(string? fname)
    {
        if (fname != null && File.Exists(fname))
        {
            return XmlUtils.ReadDoc(fname);
        }
        if (Csdb.FindConfig(Csdb.BrexMapFileName, out string found))
        {
            return XmlUtils.ReadDoc(found);
        }
        return EmbeddedResources.LoadXml("newdm/common/brexmap.xml");
    }

    /* ----- random code ----- */

    private static readonly char[] Alphanum = "ABCDEFGHIJKLMNOPQRSTUVXWYZ0123456789".ToCharArray();
    private static readonly Random Rng = new();

    private static char R() => Alphanum[Rng.Next(Alphanum.Length)];

    private static string RandomCode(string modelid)
    {
        string mic = modelid != ""
            ? modelid
            : new string(new[] { R(), R(), R(), R(), R(), R(), R(), R(), R(), R(), R(), R(), R(), R() });

        return $"{mic}-{R()}{R()}{R()}{R()}-{R()}{R()}-{R()}{R()}-{R()}{R()}{R()}{R()}-{R()}{R()}{R()}{R()}{R()}-000A-D";
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
        }
        stderr.WriteLine($"{Name}: ERROR: Unsupported issue: {iss}");
        throw new ExitException(ExitBadIssue);
    }

    private static string IssueName(Issue iss) => iss switch
    {
        Issue.Iss6 => "6",
        Issue.Iss50 => "5.0",
        Issue.Iss42 => "4.2",
        Issue.Iss41 => "4.1",
        Issue.Iss40 => "4.0",
        Issue.Iss30 => "3.0",
        Issue.Iss23 => "2.3",
        Issue.Iss22 => "2.2",
        Issue.Iss21 => "2.1",
        Issue.Iss20 => "2.0",
        _ => "",
    };

    /* ----- generic helpers ----- */

    private static XmlElement? FindChild(XmlNode parent, string name)
    {
        foreach (XmlNode cur in parent.ChildNodes)
        {
            if (cur.NodeType == XmlNodeType.Element && cur.Name == name)
            {
                return (XmlElement)cur;
            }
        }
        return null;
    }

    private static IEnumerable<XmlNode> ChildElements(XmlNode? parent)
    {
        if (parent == null) yield break;
        foreach (XmlNode cur in parent.ChildNodes)
        {
            if (cur.NodeType == XmlNodeType.Element)
            {
                yield return cur;
            }
        }
    }

    private static string? GetAttr(XmlElement el, string name) =>
        el.HasAttribute(name) ? el.GetAttribute(name) : null;

    private static XmlDocument? TryReadXml(string fname)
    {
        if (!File.Exists(fname)) return null;
        try
        {
            return XmlUtils.ReadDoc(fname);
        }
        catch (Exception ex) when (ex is XmlException or IOException)
        {
            return null;
        }
    }

    /// <summary>sscanf "%31s %255[^\n]" — first whitespace-token + rest.</summary>
    private static bool TryScanKeyVal(string line, out string key, out string val)
    {
        key = ""; val = "";
        string trimmed = line.TrimStart();
        int sp = trimmed.IndexOfAny(new[] { ' ', '\t' });
        if (sp < 0) return false;
        key = trimmed[..sp];
        val = trimmed[(sp + 1)..].TrimStart().TrimEnd('\r', '\n');
        if (key.Length == 0 || val.Length == 0) return false;
        return true;
    }

    /// <summary>sscanf "%31s %255s %255[^\n]" — key, value, optional rest.</summary>
    private static bool TryScanThree(string line, out string key, out string val, out string? rest)
    {
        key = ""; val = ""; rest = null;
        string s = line.TrimStart();
        int sp1 = s.IndexOfAny(new[] { ' ', '\t' });
        if (sp1 < 0) return false;
        key = s[..sp1];
        string after = s[(sp1 + 1)..].TrimStart();
        int sp2 = after.IndexOfAny(new[] { ' ', '\t' });
        if (sp2 < 0)
        {
            val = after.TrimEnd('\r', '\n');
            return val.Length != 0;
        }
        val = after[..sp2];
        string r = after[(sp2 + 1)..].TrimStart().TrimEnd('\r', '\n');
        rest = r.Length == 0 ? null : r;
        return true;
    }

    private static string Take(string s, int n) => s.Length <= n ? s : s[..n];

    private static int Atoi(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int idx = 0; bool neg = false;
        if (s[0] is '+' or '-') { neg = s[0] == '-'; idx = 1; }
        long val = 0;
        for (; idx < s.Length && char.IsAsciiDigit(s[idx]); idx++)
        {
            val = val * 10 + (s[idx] - '0');
        }
        return (int)(neg ? -val : val);
    }

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"{Name}: ERROR: {opt} requires an argument");
            throw new ExitException(2);
        }
        return args[i];
    }

    /* ----- help / version ----- */

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -$, --issue <issue>               Specify which S1000D issue to use.");
        stdout.WriteLine("  -@, --out <path>                  Output to specified file or directory.");
        stdout.WriteLine("  -%, --templates <dir>             Use templates in specified directory.");
        stdout.WriteLine("  -~, --dump-templates <dir>        Dump default templates to a directory.");
        stdout.WriteLine("  -,, --dump-dmtypes-xml            Dump default dmtypes XML.");
        stdout.WriteLine("  -., --dump-dmtypes                Dump default dmtypes text file.");
        stdout.WriteLine("  -!, --no-infoname                 Do not include an info name.");
        stdout.WriteLine("  -B, --generate-brex-rules         Generate BREX rules from .defaults file.");
        stdout.WriteLine("  -D, --dmtypes <dmtypes>           Specify .dmtypes file name.");
        stdout.WriteLine("  -d, --defaults <defaults>         Specify .defaults file name.");
        stdout.WriteLine("  -f, --overwrite                   Overwrite existing file.");
        stdout.WriteLine("  -j, --brexmap <map>               Use a custom .brexmap file.");
        stdout.WriteLine("  -M, --maintained-sns <SNS>        Use one of the maintained SNS.");
        stdout.WriteLine("  -N, --omit-issue                  Omit issue/inwork from filename.");
        stdout.WriteLine("  -P, --sns-levels <levels>         Levels of SNS to include in tech name.");
        stdout.WriteLine("  -p, --prompt                      Prompt the user for each value.");
        stdout.WriteLine("  -q, --quiet                       Don't report an error if file exists.");
        stdout.WriteLine("  -S, --sns <BREX>                  Get tech name from BREX SNS.");
        stdout.WriteLine("  -v, --verbose                     Print file name of new data module.");
        stdout.WriteLine("  --version                         Show version information.");
        stdout.WriteLine();
        stdout.WriteLine("In addition, the following pieces of meta data can be set:");
        stdout.WriteLine("  -#, --code <code>                 Data module code");
        stdout.WriteLine("  -a, --act <ACT>                   ACT data module code");
        stdout.WriteLine("  -b, --brex <BREX>                 BREX data module code");
        stdout.WriteLine("  -C, --country <country>           Country ISO code");
        stdout.WriteLine("  -c, --security <sec>              Security classification");
        stdout.WriteLine("  -I, --date <date>                 Issue date");
        stdout.WriteLine("  -i, --infoname <info>             Info name");
        stdout.WriteLine("  -k, --skill <skill>               Skill level");
        stdout.WriteLine("  -L, --language <lang>             Language ISO code");
        stdout.WriteLine("  -m, --remarks <remarks>           Remarks");
        stdout.WriteLine("  -n, --issno <iss>                 Issue number");
        stdout.WriteLine("  -O, --origcode <CAGE>             Originator CAGE code.");
        stdout.WriteLine("  -o, --origname <orig>             Originator enterprise name");
        stdout.WriteLine("  -R, --rpccode <CAGE>              Responsible partner company CAGE code.");
        stdout.WriteLine("  -r, --rpcname <RPC>               Responsible partner company enterprise name");
        stdout.WriteLine("  -s, --schema <schema>             Schema");
        stdout.WriteLine("  -T, --type <type>                 DM type (descript, proced, frontmatter, etc.)");
        stdout.WriteLine("  -t, --techname <tech>             Tech name");
        stdout.WriteLine("  -V, --infoname-variant <variant>  Info name variant");
        stdout.WriteLine("  -w, --inwork <inwork>             Inwork issue");
        stdout.WriteLine("  -z, --issue-type <type>           Issue type");
    }
}
