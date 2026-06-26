using System.Globalization;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-newsmc</c>: create a new SCORM content package (SMC).
///
/// Mirrors the upstream tool's option set, exit codes, the SMC filename it
/// derives from the SCORM content package code (modelIdentCode /
/// scormContentPackageIssuer / scormContentPackageNumber /
/// scormContentPackageVolume + optional issue/inwork + language/country) and the
/// content of the generated <c>scormContentPackage</c> XML.
/// </summary>
/// <remarks>
/// The skeleton is built from the embedded ISS_6 template
/// (<c>Resources/newsmc/scormcontentpackage.xml</c>) and populated with the DOM,
/// mirroring the C code. Downgrading the result to an older S1000D issue
/// (4.1/4.2/5.0) reuses the shared <c>to41/to42/to50</c> stylesheets (embedded
/// under <c>Resources/newdm/common/</c>) via <see cref="XslCompiledTransform"/>,
/// mirroring <c>toissue</c> (which for newsmc only supports down to issue 4.1):
/// the issue-specific default BREX is applied and the document is then
/// down-converted. The default issue (6) is fully supported.
/// </remarks>
public sealed class NewsmcTool : ITool
{
    public string Name => "newsmc";
    public string Description => "Create a new SCORM content package (SMC).";
    public string Version => "3.0.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitBadSmc = 1;        // EXIT_BAD_SMC
    private const int ExitSmcExists = 2;     // EXIT_SMC_EXISTS
    private const int ExitBadBrexDmc = 3;    // EXIT_BAD_BREX_DMC
    private const int ExitBadDate = 4;       // EXIT_BAD_DATE
    private const int ExitBadIssue = 5;      // EXIT_BAD_ISSUE
    private const int ExitBadTemplate = 6;   // EXIT_BAD_TEMPLATE
    private const int ExitBadTemplDir = 7;   // EXIT_BAD_TEMPL_DIR
    private const int ExitOsError = 8;       // EXIT_OS_ERROR

    private const string DefaultLanguageIsoCode = "und";
    private const string DefaultCountryIsoCode = "ZZ";

    private const string Iss41DefaultBrex = "S1000D-E-04-10-0301-00A-022A-D";
    private const string Iss42DefaultBrex = "S1000D-F-04-10-0301-00A-022A-D";
    private const string Iss50DefaultBrex = "S1000D-G-04-10-0301-00A-022A-D";

    private enum Issue { NoIss, Iss41, Iss42, Iss50, Iss6 }

    private const Issue DefaultS1000DIssue = Issue.Iss6;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    /* Per-run metadata state (mirrors the C statics). */
    private string _modelIdentCode = "";
    private string _smcIssuer = "";
    private string _smcNumber = "";
    private string _smcVolume = "";
    private string _languageIsoCode = "";
    private string _countryIsoCode = "";
    private string _issueNumber = "";
    private string _inWork = "";
    private string _smcTitle = "";
    private string _securityClassification = "";
    private string _enterpriseName = "";
    private string _enterpriseCode = "";
    private string _brexDmcode = "";
    private string _issueDate = "";
    private string? _issueType;
    private string? _remarks;
    private string? _skillLevelCode;
    private string? _templateDir;
    private string? _actDmcode;
    private Issue _issue = Issue.NoIss;

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
        string smcode = "";
        string? defaultsFname = null;
        bool customDefaults = false;
        bool noIssue = false;
        bool includeIssueInfo = false;
        bool includeLanguage = false;
        bool includeTitle = false;
        bool includeDate = false;
        bool verbose = false;
        bool overwrite = false;
        bool noOverwriteError = false;
        string? @out = null;
        string? outdir = null;
        var dmodules = new List<string>();

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
                    // Interactive prompting is not supported in the library port.
                    break;
                case "-D" or "--include-date":
                    includeDate = true;
                    break;
                case "-d" or "--defaults":
                    defaultsFname = NextArg(args, ref i, "-d", stderr);
                    customDefaults = true;
                    break;
                case "-#" or "--code":
                    smcode = NextArg(args, ref i, "-#", stderr);
                    break;
                case "-L" or "--language":
                    _languageIsoCode = NextArg(args, ref i, "-L", stderr);
                    break;
                case "-C" or "--country":
                    _countryIsoCode = NextArg(args, ref i, "-C", stderr);
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
                case "-r" or "--rpcname":
                    _enterpriseName = NextArg(args, ref i, "-r", stderr);
                    break;
                case "-R" or "--rpccode":
                    _enterpriseCode = NextArg(args, ref i, "-R", stderr);
                    break;
                case "-t" or "--title":
                    _smcTitle = NextArg(args, ref i, "-t", stderr);
                    break;
                case "-N" or "--omit-issue":
                    noIssue = true;
                    break;
                case "-i" or "--include-issue":
                    includeIssueInfo = true;
                    break;
                case "-l" or "--include-lang":
                    includeLanguage = true;
                    break;
                case "-T" or "--include-title":
                    includeTitle = true;
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
                case "-k" or "--skill":
                    _skillLevelCode = NextArg(args, ref i, "-k", stderr);
                    break;
                case "-z" or "--issue-type":
                    _issueType = NextArg(args, ref i, "-z", stderr);
                    break;
                case "-a" or "--act":
                    _actDmcode = NextArg(args, ref i, "-a", stderr);
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return 2;
                    }
                    dmodules.Add(a);
                    break;
            }
        }

        ReadDefaults(customDefaults, defaultsFname, stderr);

        XmlDocument smcDoc = XmlSkeleton(stderr);

        if (smcode.Length != 0)
        {
            int offset = smcode.StartsWith("SMC-", StringComparison.Ordinal) ? 4 : 0;
            if (!ParseSmcCode(smcode[offset..]))
            {
                stderr.WriteLine($"{Name}: ERROR: Bad SCORM content package code.");
                throw new ExitException(ExitBadSmc);
            }
        }

        if (_modelIdentCode.Length == 0 ||
            _smcIssuer.Length == 0 ||
            _smcNumber.Length == 0 ||
            _smcVolume.Length == 0)
        {
            stderr.Write($"{Name}: ERROR: Missing required SMC components: ");
            stderr.WriteLine("SMC-{0}-{1}-{2}-{3}",
                _modelIdentCode.Length == 0 ? "???" : _modelIdentCode,
                _smcIssuer.Length == 0 ? "???" : _smcIssuer,
                _smcNumber.Length == 0 ? "???" : _smcNumber,
                _smcVolume.Length == 0 ? "???" : _smcVolume);
            throw new ExitException(ExitBadSmc);
        }

        if (_issue == Issue.NoIss) _issue = DefaultS1000DIssue;
        if (_issueNumber.Length == 0) _issueNumber = "000";
        if (_inWork.Length == 0) _inWork = "01";
        if (_securityClassification.Length == 0) _securityClassification = "01";
        _skillLevelCode ??= "sk01";

        SetEnvLang();
        _languageIsoCode = _languageIsoCode.ToLowerInvariant();
        _countryIsoCode = _countryIsoCode.ToUpperInvariant();

        XmlElement scormContentPackage = smcDoc.DocumentElement!;
        XmlElement identAndStatusSection = FindChild(scormContentPackage, "identAndStatusSection")!;
        XmlElement scormContentPackageAddress = FindChild(identAndStatusSection, "scormContentPackageAddress")!;
        XmlElement scormContentPackageIdent = FindChild(scormContentPackageAddress, "scormContentPackageIdent")!;
        XmlElement scormContentPackageCode = FindChild(scormContentPackageIdent, "scormContentPackageCode")!;
        XmlElement language = FindChild(scormContentPackageIdent, "language")!;
        XmlElement issueInfo = FindChild(scormContentPackageIdent, "issueInfo")!;
        XmlElement scormContentPackageAddressItems = FindChild(scormContentPackageAddress, "scormContentPackageAddressItems")!;
        XmlElement issueDate = FindChild(scormContentPackageAddressItems, "issueDate")!;
        XmlElement scormContentPackageTitle = FindChild(scormContentPackageAddressItems, "scormContentPackageTitle")!;
        XmlElement scormContentPackageStatus = FindChild(identAndStatusSection, "scormContentPackageStatus")!;
        XmlElement security = FindChild(scormContentPackageStatus, "security")!;
        XmlElement responsiblePartnerCompany = FindChild(scormContentPackageStatus, "responsiblePartnerCompany")!;
        XmlElement scoEntry = FindChild(FindChild(scormContentPackage, "content")!, "scoEntry")!;

        scormContentPackageCode.SetAttribute("modelIdentCode", _modelIdentCode);
        scormContentPackageCode.SetAttribute("scormContentPackageIssuer", _smcIssuer);
        scormContentPackageCode.SetAttribute("scormContentPackageNumber", _smcNumber);
        scormContentPackageCode.SetAttribute("scormContentPackageVolume", _smcVolume);

        language.SetAttribute("languageIsoCode", _languageIsoCode);
        language.SetAttribute("countryIsoCode", _countryIsoCode);

        issueInfo.SetAttribute("issueNumber", _issueNumber);
        issueInfo.SetAttribute("inWork", _inWork);

        SetIssueDate(issueDate);

        if (_issueType != null) scormContentPackageStatus.SetAttribute("issueType", _issueType);

        scormContentPackageTitle.InnerText = _smcTitle;

        security.SetAttribute("securityClassification", _securityClassification);

        if (_enterpriseName.Length != 0)
        {
            XmlElement en = smcDoc.CreateElement("enterpriseName");
            en.InnerText = _enterpriseName;
            responsiblePartnerCompany.AppendChild(en);
        }

        if (_enterpriseCode.Length != 0)
        {
            responsiblePartnerCompany.SetAttribute("enterpriseCode", _enterpriseCode);
        }

        if (_actDmcode != null)
        {
            SetAct(smcDoc, _actDmcode);
        }
        else
        {
            UnsetAct(smcDoc);
        }

        if (_brexDmcode.Length != 0)
        {
            SetBrex(smcDoc, _brexDmcode);
        }

        SetSkillLevel(smcDoc, _skillLevelCode);

        SetRemarks(smcDoc, _remarks);

        foreach (string dmodule in dmodules)
        {
            AddDmRef(smcDoc, scoEntry, dmodule, includeIssueInfo, includeLanguage, includeTitle, includeDate, stderr);
        }

        // Filename language code is upper case.
        string fileLang = _languageIsoCode.ToUpperInvariant();

        string iss = "";
        if (!noIssue)
        {
            iss = $"_{_issueNumber}-{_inWork}";
        }

        if (_issue < Issue.Iss6)
        {
            switch (_issue)
            {
                case Issue.Iss50:
                    SetBrex(smcDoc, Iss50DefaultBrex);
                    break;
                case Issue.Iss42:
                    SetBrex(smcDoc, Iss42DefaultBrex);
                    break;
                case Issue.Iss41:
                    SetBrex(smcDoc, Iss41DefaultBrex);
                    break;
            }

            // Down-convert the document to the selected older issue using the
            // shared common/to41.xsl/to42.xsl/to50.xsl stylesheets (mirror
            // toissue(); newsmc only supports down to issue 4.1).
            smcDoc = ToIssue(smcDoc, _issue);
        }

        if (@out != null && Directory.Exists(@out))
        {
            outdir = @out;
            @out = null;
        }

        @out ??= $"SMC-{_modelIdentCode}-{_smcIssuer}-{_smcNumber}-{_smcVolume}{iss}_{fileLang}-{_countryIsoCode}.XML";

        string outPath = outdir != null ? Path.Combine(outdir, @out) : @out;

        if (!overwrite && File.Exists(outPath))
        {
            if (noOverwriteError) return 0;
            stderr.WriteLine($"{Name}: ERROR: {outPath} already exists. Use -f to overwrite.");
            throw new ExitException(ExitSmcExists);
        }

        XmlUtils.SaveDoc(smcDoc, outPath);

        if (verbose)
        {
            stdout.WriteLine(outPath);
        }

        return 0;
    }

    /* ----- argument helper ----- */

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"{Name}: ERROR: {opt} requires an argument");
            throw new ExitException(2);
        }
        return args[i];
    }

    /* ----- skeleton / templates ----- */

    private XmlDocument XmlSkeleton(TextWriter stderr)
    {
        if (_templateDir != null)
        {
            string src = Path.Combine(_templateDir, "scormcontentpackage.xml");
            if (!File.Exists(src))
            {
                stderr.WriteLine($"{Name}: ERROR: No schema scormcontentpackage in template directory \"{_templateDir}\".");
                throw new ExitException(ExitBadTemplate);
            }
            return XmlUtils.ReadDoc(src);
        }

        return EmbeddedResources.LoadXml("newsmc/scormcontentpackage.xml");
    }

    /// <summary>
    /// Down-issue the document to an older S1000D issue using the shared
    /// <c>common/to*.xsl</c> stylesheets (embedded under
    /// <c>Resources/newdm/common/</c>). Mirrors <c>toissue</c>, which for newsmc
    /// only supports down to issue 4.1.
    /// </summary>
    private static XmlDocument ToIssue(XmlDocument doc, Issue iss)
    {
        string? xsl = iss switch
        {
            Issue.Iss50 => "newdm/common/to50.xsl",
            Issue.Iss42 => "newdm/common/to42.xsl",
            Issue.Iss41 => "newdm/common/to41.xsl",
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

    private void DumpTemplate(string path, TextWriter stderr)
    {
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"{Name}: ERROR: Cannot dump template to directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }

        try
        {
            byte[] bytes = EmbeddedResources.ReadBytes("newsmc/scormcontentpackage.xml");
            File.WriteAllBytes(Path.Combine(path, "scormcontentpackage.xml"), bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{Name}: ERROR: Cannot dump template to directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }
    }

    /* ----- issue parsing ----- */

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
            stderr.WriteLine($"{Name}: ERROR: Unsupported issue: {iss}");
            throw new ExitException(ExitBadIssue);
        }
    }

    /* ----- SMC code parsing ----- */

    /// <summary>
    /// Mirror the C <c>sscanf(code, "%14[^-]-%5s-%5s-%2s", …)</c>: four
    /// '-'-separated fields. Returns false when fewer than four are present.
    /// </summary>
    private bool ParseSmcCode(string code)
    {
        string[] parts = code.Split('-');
        if (parts.Length < 4)
        {
            return false;
        }
        // %14[^-] / %5s / %5s / %2s — bounded field widths.
        _modelIdentCode = Take(parts[0], 14);
        _smcIssuer = Take(parts[1], 5);
        _smcNumber = Take(parts[2], 5);
        _smcVolume = Take(parts[3], 2);
        return true;
    }

    private static string Take(string s, int n) => s.Length <= n ? s : s[..n];

    /* ----- defaults ----- */

    private void ReadDefaults(bool customDefaults, string? defaultsFname, TextWriter stderr)
    {
        string path;
        if (customDefaults && defaultsFname != null)
        {
            path = defaultsFname;
        }
        else
        {
            Csdb.FindConfig(Csdb.DefaultsFileName, out path);
        }

        if (!File.Exists(path))
        {
            return;
        }

        // Try XML form first (mirrors read_xml_doc); fall back to the simple
        // "key value" text form.
        XmlDocument? defaultsXml = null;
        try
        {
            defaultsXml = XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            defaultsXml = null;
        }

        if (defaultsXml?.DocumentElement != null)
        {
            foreach (XmlNode node in defaultsXml.DocumentElement.ChildNodes)
            {
                if (node is not XmlElement el) continue;
                if (!el.HasAttribute("ident") || !el.HasAttribute("value")) continue;
                CopyDefaultValue(el.GetAttribute("ident"), el.GetAttribute("value"), stderr);
            }
            return;
        }

        foreach (string line in File.ReadLines(path))
        {
            // sscanf("%31s %255[^\n]") — first whitespace-delimited token is the
            // key, remainder of the line (after the separating whitespace) is the
            // value.
            string trimmed = line.TrimStart();
            int sp = trimmed.IndexOfAny(new[] { ' ', '\t' });
            if (sp < 0) continue;
            string key = trimmed[..sp];
            string val = trimmed[(sp + 1)..].TrimStart();
            if (val.Length == 0) continue;
            CopyDefaultValue(key, val, stderr);
        }
    }

    private void CopyDefaultValue(string key, string val, TextWriter stderr)
    {
        switch (key)
        {
            case "modelIdentCode" when _modelIdentCode.Length == 0: _modelIdentCode = val; break;
            case "scormContentPackageIssuer" when _smcIssuer.Length == 0: _smcIssuer = val; break;
            case "scormContentPackageNumber" when _smcNumber.Length == 0: _smcNumber = val; break;
            case "scormContentPackageVolume" when _smcVolume.Length == 0: _smcVolume = val; break;
            case "languageIsoCode" when _languageIsoCode.Length == 0: _languageIsoCode = val; break;
            case "countryIsoCode" when _countryIsoCode.Length == 0: _countryIsoCode = val; break;
            case "securityClassification" when _securityClassification.Length == 0: _securityClassification = val; break;
            case "responsiblePartnerCompany" when _enterpriseName.Length == 0: _enterpriseName = val; break;
            case "responsiblePartnerCompanyCode" when _enterpriseCode.Length == 0: _enterpriseCode = val; break;
            case "issueNumber" when _issueNumber.Length == 0: _issueNumber = val; break;
            case "inWork" when _inWork.Length == 0: _inWork = val; break;
            case "brex" when _brexDmcode.Length == 0: _brexDmcode = val; break;
            case "issue" when _issue == Issue.NoIss: _issue = GetIssue(val, stderr); break;
            case "templates" when _templateDir == null: _templateDir = val; break;
            case "remarks" when _remarks == null: _remarks = val; break;
            case "skillLevelCode" when _skillLevelCode == null: _skillLevelCode = val; break;
            case "issueType" when _issueType == null: _issueType = val; break;
            case "act" when _actDmcode == null: _actDmcode = val; break;
        }
    }

    /* ----- language/country defaults ----- */

    private void SetEnvLang()
    {
        string? env = Environment.GetEnvironmentVariable("LANG");
        if (string.IsNullOrEmpty(env))
        {
            if (_languageIsoCode.Length == 0) _languageIsoCode = DefaultLanguageIsoCode;
            if (_countryIsoCode.Length == 0) _countryIsoCode = DefaultCountryIsoCode;
            return;
        }

        // strtok(lang, "_") then strtok(NULL, ".") — language before '_',
        // country between '_' and '.'.
        string langPart = env;
        string? countryPart = null;
        int us = env.IndexOf('_');
        if (us >= 0)
        {
            langPart = env[..us];
            string rest = env[(us + 1)..];
            int dot = rest.IndexOf('.');
            countryPart = dot >= 0 ? rest[..dot] : rest;
        }

        if (_languageIsoCode.Length == 0)
        {
            _languageIsoCode = langPart.Length != 0 ? Take(langPart, 3) : DefaultLanguageIsoCode;
        }
        if (_countryIsoCode.Length == 0)
        {
            _countryIsoCode = !string.IsNullOrEmpty(countryPart) ? Take(countryPart, 2) : DefaultCountryIsoCode;
        }
    }

    /* ----- DOM helpers ----- */

    private static XmlElement? FindChild(XmlNode parent, string name)
    {
        foreach (XmlNode cur in parent.ChildNodes)
        {
            if (cur is XmlElement el && el.Name == name)
            {
                return el;
            }
        }
        return null;
    }

    private void SetIssueDate(XmlElement issueDate)
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
            // sscanf("%4s-%2s-%2s") requires three '-'-separated fields.
            string[] parts = _issueDate.Split('-');
            if (parts.Length < 3)
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

    private void AddDmRef(XmlDocument smcDoc, XmlElement scoEntry, string path,
        bool includeIssueInfo, bool includeLanguage, bool includeTitle, bool includeDate, TextWriter stderr)
    {
        if (!File.Exists(path))
        {
            stderr.WriteLine($"{Name}: ERROR: Could not find referenced data module '{path}'.");
            return;
        }

        XmlDocument dmodule;
        try
        {
            dmodule = XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            stderr.WriteLine($"{Name}: ERROR: Could not find referenced data module '{path}'.");
            return;
        }

        XmlNode? identExtension = dmodule.SelectSingleNode("//dmIdent/identExtension");
        XmlNode? dmCode = dmodule.SelectSingleNode("//dmIdent/dmCode");
        XmlNode? issueInfo = dmodule.SelectSingleNode("//dmIdent/issueInfo");
        XmlNode? language = dmodule.SelectSingleNode("//dmIdent/language");

        XmlElement scoEntryItem = smcDoc.CreateElement("scoEntryItem");
        XmlElement dmRef = smcDoc.CreateElement("dmRef");
        scoEntryItem.AppendChild(dmRef);
        XmlElement dmRefIdent = smcDoc.CreateElement("dmRefIdent");
        dmRef.AppendChild(dmRefIdent);

        if (identExtension != null)
        {
            dmRefIdent.AppendChild(smcDoc.ImportNode(identExtension, true));
        }

        if (dmCode != null)
        {
            dmRefIdent.AppendChild(smcDoc.ImportNode(dmCode, true));
        }

        if (includeIssueInfo && issueInfo != null)
        {
            dmRefIdent.AppendChild(smcDoc.ImportNode(issueInfo, true));
        }

        if (includeLanguage && language != null)
        {
            dmRefIdent.AppendChild(smcDoc.ImportNode(language, true));
        }

        if (includeTitle || includeDate)
        {
            XmlElement dmRefAddressItems = smcDoc.CreateElement("dmRefAddressItems");
            dmRef.AppendChild(dmRefAddressItems);

            if (includeTitle)
            {
                XmlNode? dmTitle = dmodule.SelectSingleNode("//dmAddressItems/dmTitle");
                if (dmTitle != null)
                {
                    dmRefAddressItems.AppendChild(smcDoc.ImportNode(dmTitle, true));
                }
            }
            if (includeDate)
            {
                XmlNode? issueDate = dmodule.SelectSingleNode("//dmAddressItems/issueDate");
                if (issueDate != null)
                {
                    dmRefAddressItems.AppendChild(smcDoc.ImportNode(issueDate, true));
                }
            }
        }

        scoEntry.AppendChild(scoEntryItem);
    }

    /* ----- BREX / ACT dmCode ----- */

    private void SetDmCode(XmlElement dmCode, string fname)
    {
        string code = Path.GetFileName(fname);
        int offset = code.StartsWith("DMC-", StringComparison.Ordinal) ? 4 : 0;
        code = code[offset..];

        // %14[^-]-%4[^-]-%3[^-]-%c%c-%4[^-]-%2s%3[^-]-%3s%c-%c-%3s%1s
        // Fields are '-'-separated, but subSystem/subSubSystem are a single
        // 2-char field, assy/disassy share a field, infoCode/variant share a
        // field, and the optional learnCode/learnEventCode share the final field.
        string[] f = code.Split('-');
        if (f.Length < 7)
        {
            throw BadBrex();
        }

        string modelIdentCode = Take(f[0], 14);
        string systemDiffCode = Take(f[1], 4);
        string systemCode = Take(f[2], 3);

        string subSys = f[3];
        if (subSys.Length < 2)
        {
            throw BadBrex();
        }
        string subSystemCode = subSys[..1];
        string subSubSystemCode = subSys.Substring(1, 1);

        string assyCode = Take(f[4], 4);

        string disBlock = f[5]; // %2s%3[^-] : disassyCode (2) + disassyCodeVariant
        if (disBlock.Length < 3)
        {
            throw BadBrex();
        }
        string disassyCode = disBlock[..2];
        string disassyCodeVariant = Take(disBlock[2..], 3);

        string infoBlock = f[6]; // %3s%c : infoCode (3) + infoCodeVariant (1)
        if (infoBlock.Length < 4)
        {
            throw BadBrex();
        }
        string infoCode = infoBlock[..3];
        string infoCodeVariant = infoBlock.Substring(3, 1);

        // %c : itemLocationCode
        if (f.Length < 8 || f[7].Length < 1)
        {
            throw BadBrex();
        }
        string itemLocationCode = f[7][..1];

        string learnCode = "";
        string learnEventCode = "";
        // %3s%1s : optional learnCode (3) + learnEventCode (1)
        if (f.Length >= 9 && f[8].Length >= 4)
        {
            learnCode = f[8][..3];
            learnEventCode = f[8].Substring(3, 1);
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

        if (learnCode.Length != 0) dmCode.SetAttribute("learnCode", learnCode);
        if (learnEventCode.Length != 0) dmCode.SetAttribute("learnEventCode", learnEventCode);
    }

    private ExitException BadBrex()
    {
        return new ExitException(ExitBadBrexDmc);
    }

    private void SetBrex(XmlDocument doc, string fname)
    {
        if (doc.SelectSingleNode("//brexDmRef/dmRef/dmRefIdent/dmCode") is XmlElement dmCode)
        {
            SetDmCode(dmCode, fname);
        }
    }

    private void SetAct(XmlDocument doc, string fname)
    {
        if (doc.SelectSingleNode("//applicCrossRefTableRef/dmRef/dmRefIdent/dmCode") is XmlElement dmCode)
        {
            SetDmCode(dmCode, fname);
        }
    }

    private static void UnsetAct(XmlDocument doc)
    {
        XmlNode? node = doc.SelectSingleNode("//applicCrossRefTableRef");
        node?.ParentNode?.RemoveChild(node);
    }

    /* ----- remarks / skill level ----- */

    private void SetRemarks(XmlDocument doc, string? text)
    {
        if (doc.SelectSingleNode("//remarks") is not XmlElement remarks)
        {
            return;
        }

        if (text != null)
        {
            XmlElement simplePara = doc.CreateElement("simplePara");
            simplePara.InnerText = text;
            remarks.AppendChild(simplePara);
        }
        else
        {
            remarks.ParentNode?.RemoveChild(remarks);
        }
    }

    private void SetSkillLevel(XmlDocument doc, string? code)
    {
        if (doc.SelectSingleNode("//personSkill") is not XmlElement skillLevel)
        {
            return;
        }

        if (code != null)
        {
            skillLevel.SetAttribute("skillLevelCode", code);
        }
        else
        {
            skillLevel.ParentNode?.RemoveChild(skillLevel);
        }
    }

    /* ----- help / version ----- */

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] [<dmodule>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -$, --issue <issue>         Specify which S1000D issue to use.");
        stdout.WriteLine("  -@, --out <path>            Output to specified file or directory.");
        stdout.WriteLine("  -%, --templates <dir>       Use template in specified directory.");
        stdout.WriteLine("  -~, --dump-templates <dir>  Dump built-in template to directory.");
        stdout.WriteLine("  -D, --include-date          Include issue date in referenced data modules.");
        stdout.WriteLine("  -d, --defaults <file>       Specify the .defaults file name.");
        stdout.WriteLine("  -f, --overwrite             Overwrite existing file.");
        stdout.WriteLine("  -i, --include-issue         Include issue info in referenced data modules.");
        stdout.WriteLine("  -l, --include-lang          Include language info in referenced data modules.");
        stdout.WriteLine("  -N, --omit-issue            Omit issue/inwork from file name.");
        stdout.WriteLine("  -p, --prompt                Prompt the user for each value.");
        stdout.WriteLine("  -q, --quiet                 Don't report an error if file exists.");
        stdout.WriteLine("  -T, --include-title         Include titles in referenced data modules.");
        stdout.WriteLine("  -v, --verbose               Print file name of SMC.");
        stdout.WriteLine("  --version                   Show version information.");
        stdout.WriteLine("  <dmodule>...                Data modules to include in new SMC.");
        stdout.WriteLine();
        stdout.WriteLine("In addition, the following pieces of meta data can be set:");
        stdout.WriteLine("  -#, --code <code>           SCORM content package code");
        stdout.WriteLine("  -a, --act <ACT>             ACT data module code");
        stdout.WriteLine("  -b, --brex <BREX>           BREX data module code");
        stdout.WriteLine("  -C, --country <country>     Country ISO code");
        stdout.WriteLine("  -c, --security <sec>        Security classification");
        stdout.WriteLine("  -I, --date <date>           Issue date");
        stdout.WriteLine("  -k, --skill <skill>         Skill level");
        stdout.WriteLine("  -L, --language <lang>       Language ISO code");
        stdout.WriteLine("  -m, --remarks <remarks>     Remarks");
        stdout.WriteLine("  -n, --issno <iss>           Issue number");
        stdout.WriteLine("  -R, --rpccode <CAGE>        Responsible partner company code");
        stdout.WriteLine("  -r, --rpcname <RPC>         Responsible partner company enterprise name");
        stdout.WriteLine("  -t, --title <title>         SCORM content package title");
        stdout.WriteLine("  -w, --inwork <inwork>       Inwork issue");
        stdout.WriteLine("  -z, --issue-type <type>     Issue type");
    }
}
