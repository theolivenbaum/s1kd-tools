using System.Text;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-newpm</c>: create a new S1000D publication module with the
/// publication module code and other metadata specified. Mirrors the C tool's
/// option set, exit codes, defaults handling, generated file name (the PMC
/// assembly) and PM content.
/// </summary>
/// <remarks>
/// The built-in <c>pm.xml</c> template is the default S1000D issue 6 skeleton.
/// Down-issuing to an older S1000D issue (the C tool's <c>-$</c> path) reuses the
/// shared <c>common/to*.xsl</c> stylesheets (embedded under
/// <c>Resources/newdm/common/</c>) via <see cref="XslCompiledTransform"/>,
/// mirroring <c>toissue</c>. Interactive prompting (<c>-p</c>) is accepted but is
/// a no-op in this library port.
/// </remarks>
public sealed class NewpmTool : ITool
{
    public string Name => "newpm";
    public string Description => "Create a new S1000D publication module.";
    public string Version => "3.0.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitBadPmc = 1;        // EXIT_BAD_PMC
    private const int ExitPmExists = 2;       // EXIT_PM_EXISTS
    private const int ExitBadBrexDmc = 3;     // EXIT_BAD_BREX_DMC
    private const int ExitBadDate = 4;        // EXIT_BAD_DATE
    private const int ExitBadIssue = 5;       // EXIT_BAD_ISSUE
    private const int ExitBadTemplate = 6;    // EXIT_BAD_TEMPLATE
    private const int ExitBadTemplDir = 7;    // EXIT_BAD_TEMPL_DIR
    private const int ExitOsError = 8;        // EXIT_OS_ERROR

    private const string DefaultLanguageIsoCode = "und";
    private const string DefaultCountryIsoCode = "ZZ";

    private enum Issue { None, Iss20, Iss21, Iss22, Iss23, Iss30, Iss40, Iss41, Iss42, Iss50, Iss6 }

    private const Issue DefaultIssue = Issue.Iss6;

    // Default BREX DM codes for older issues (mirror the ISS_*_DEFAULT_BREX defines).
    private static string? DefaultBrexFor(Issue iss) => iss switch
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

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    // ---- Per-run metadata state (mirrors the C file-scope statics) ----
    private string _modelIdentCode = "";
    private string _pmIssuer = "";
    private string _pmNumber = "";
    private string _pmVolume = "";
    private string _languageIsoCode = "";
    private string _countryIsoCode = "";
    private string _issueNumber = "";
    private string _inWork = "";
    private string _pmTitle = "";
    private string _shortPmTitle = "";
    private string _securityClassification = "";
    private string _enterpriseName = "";
    private string _enterpriseCode = "";
    private string _brexDmcode = "";
    private string _issueDate = "";
    private string? _issueType;
    private string? _remarks;
    private Issue _issue = Issue.None;
    private string? _templateDir;
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
        string pmcode = "";
        bool skippmc = false;
        string? defaultsFname = null;
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

            // Long options first.
            switch (a)
            {
                case "--version":
                    stdout.WriteLine($"{Name} ({Version})");
                    return 0;
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return 0;
                case "--act": _actDmcode = NextArg(args, ref i, "-a", stderr); break;
                case "--prompt": break; // interactive prompting is a no-op in the library port
                case "--include-date": includeDate = true; break;
                case "--defaults": defaultsFname = NextArg(args, ref i, "-d", stderr); break;
                case "--code": pmcode = NextArg(args, ref i, "-#", stderr); skippmc = true; break;
                case "--language": _languageIsoCode = NextArg(args, ref i, "-L", stderr); break;
                case "--country": _countryIsoCode = NextArg(args, ref i, "-C", stderr); break;
                case "--issno": _issueNumber = NextArg(args, ref i, "-n", stderr); break;
                case "--inwork": _inWork = NextArg(args, ref i, "-w", stderr); break;
                case "--security": _securityClassification = NextArg(args, ref i, "-c", stderr); break;
                case "--rpcname": _enterpriseName = NextArg(args, ref i, "-r", stderr); break;
                case "--rpccode": _enterpriseCode = NextArg(args, ref i, "-R", stderr); break;
                case "--title": _pmTitle = NextArg(args, ref i, "-t", stderr); break;
                case "--omit-issue": noIssue = true; break;
                case "--include-issue": includeIssueInfo = true; break;
                case "--include-lang": includeLanguage = true; break;
                case "--include-title": includeTitle = true; break;
                case "--brex": _brexDmcode = NextArg(args, ref i, "-b", stderr); break;
                case "--date": _issueDate = NextArg(args, ref i, "-I", stderr); break;
                case "--verbose": verbose = true; break;
                case "--overwrite": overwrite = true; break;
                case "--issue": _issue = GetIssue(NextArg(args, ref i, "-$", stderr), stderr); break;
                case "--out": @out = NextArg(args, ref i, "-@", stderr); break;
                case "--templates": _templateDir = NextArg(args, ref i, "-%", stderr); break;
                case "--short-title": _shortPmTitle = NextArg(args, ref i, "-s", stderr); break;
                case "--quiet": noOverwriteError = true; break;
                case "--remarks": _remarks = NextArg(args, ref i, "-m", stderr); break;
                case "--dump-templates":
                    return DumpTemplate(NextArg(args, ref i, "-~", stderr), stderr);
                case "--issue-type": _issueType = NextArg(args, ref i, "-z", stderr); break;
                default:
                    if (a.Length > 1 && a[0] == '-' && a != "-")
                    {
                        int? rc = ParseShort(a, args, ref i, ref pmcode, ref skippmc, ref defaultsFname,
                            ref noIssue, ref includeIssueInfo, ref includeLanguage, ref includeTitle,
                            ref includeDate, ref verbose, ref overwrite, ref noOverwriteError,
                            ref @out, stdout, stderr);
                        if (rc.HasValue) { return rc.Value; }
                        break;
                    }
                    dmodules.Add(a);
                    break;
            }
        }

        // Load defaults (.defaults), filling unset values. Mirrors find_config + parse.
        LoadDefaults(defaultsFname, stderr);

        XmlDocument pmDoc = XmlSkeleton(stderr);

        // Parse the PMC supplied via -#.
        if (pmcode.Length != 0)
        {
            int offset = pmcode.StartsWith("PMC-", StringComparison.Ordinal) ? 4 : 0;
            string[] parts = pmcode[offset..].Split('-');
            if (parts.Length != 4)
            {
                stderr.WriteLine($"{Name}: ERROR: Bad publication module code.");
                return ExitBadPmc;
            }
            _modelIdentCode = parts[0];
            _pmIssuer = parts[1];
            _pmNumber = parts[2];
            _pmVolume = parts[3];
        }

        if (_modelIdentCode.Length == 0 || _pmIssuer.Length == 0 ||
            _pmNumber.Length == 0 || _pmVolume.Length == 0)
        {
            stderr.Write($"{Name}: ERROR: Missing required PMC components: ");
            stderr.WriteLine("PMC-{0}-{1}-{2}-{3}",
                _modelIdentCode.Length == 0 ? "???" : _modelIdentCode,
                _pmIssuer.Length == 0 ? "???" : _pmIssuer,
                _pmNumber.Length == 0 ? "???" : _pmNumber,
                _pmVolume.Length == 0 ? "???" : _pmVolume);
            return ExitBadPmc;
        }

        if (_issue == Issue.None) { _issue = DefaultIssue; }
        if (_issueNumber.Length == 0) { _issueNumber = "000"; }
        if (_inWork.Length == 0) { _inWork = "01"; }
        if (_securityClassification.Length == 0) { _securityClassification = "01"; }

        SetEnvLang();
        _languageIsoCode = _languageIsoCode.ToLowerInvariant();
        _countryIsoCode = _countryIsoCode.ToUpperInvariant();

        XmlElement pm = pmDoc.DocumentElement!;
        XmlNode identAndStatusSection = FindChild(pm, "identAndStatusSection")!;
        XmlNode pmAddress = FindChild(identAndStatusSection, "pmAddress")!;
        XmlNode pmIdent = FindChild(pmAddress, "pmIdent")!;
        XmlElement pmCode = (XmlElement)FindChild(pmIdent, "pmCode")!;
        XmlElement language = (XmlElement)FindChild(pmIdent, "language")!;
        XmlElement issueInfo = (XmlElement)FindChild(pmIdent, "issueInfo")!;
        XmlElement pmAddressItems = (XmlElement)FindChild(pmAddress, "pmAddressItems")!;
        XmlElement issueDate = (XmlElement)FindChild(pmAddressItems, "issueDate")!;
        XmlElement pmTitle = (XmlElement)FindChild(pmAddressItems, "pmTitle")!;
        XmlElement pmStatus = (XmlElement)FindChild(identAndStatusSection, "pmStatus")!;
        XmlElement security = (XmlElement)FindChild(pmStatus, "security")!;
        XmlElement responsiblePartnerCompany = (XmlElement)FindChild(pmStatus, "responsiblePartnerCompany")!;
        XmlElement pmEntry = (XmlElement)FindChild(FindChild(pm, "content")!, "pmEntry")!;

        pmCode.SetAttribute("modelIdentCode", _modelIdentCode);
        pmCode.SetAttribute("pmIssuer", _pmIssuer);
        pmCode.SetAttribute("pmNumber", _pmNumber);
        pmCode.SetAttribute("pmVolume", _pmVolume);

        language.SetAttribute("languageIsoCode", _languageIsoCode);
        language.SetAttribute("countryIsoCode", _countryIsoCode);

        issueInfo.SetAttribute("issueNumber", _issueNumber);
        issueInfo.SetAttribute("inWork", _inWork);

        SetIssueDate(issueDate);

        if (_issueType != null) { pmStatus.SetAttribute("issueType", _issueType); }

        pmTitle.InnerText = _pmTitle;

        if (_shortPmTitle.Length != 0)
        {
            XmlElement shortPmTitle = pmDoc.CreateElement("shortPmTitle");
            shortPmTitle.InnerText = _shortPmTitle;
            pmAddressItems.AppendChild(shortPmTitle);
        }

        security.SetAttribute("securityClassification", _securityClassification);

        if (_enterpriseName.Length != 0)
        {
            XmlElement enterpriseName = pmDoc.CreateElement("enterpriseName");
            enterpriseName.InnerText = _enterpriseName;
            responsiblePartnerCompany.AppendChild(enterpriseName);
        }

        if (_enterpriseCode.Length != 0)
        {
            responsiblePartnerCompany.SetAttribute("enterpriseCode", _enterpriseCode);
        }

        if (_actDmcode != null)
        {
            SetAct(pmDoc, _actDmcode);
        }
        else
        {
            UnsetAct(pmDoc);
        }

        if (_brexDmcode.Length != 0)
        {
            SetBrex(pmDoc, _brexDmcode);
        }

        SetRemarks(pmDoc, _remarks);

        foreach (string path in dmodules)
        {
            AddDmRef(pmEntry, path, includeIssueInfo, includeLanguage, includeTitle, includeDate, stderr);
        }

        // The file name uses the upper-cased language ISO code.
        string langForName = _languageIsoCode.ToUpperInvariant();

        string iss = noIssue ? "" : $"_{_issueNumber}-{_inWork}";

        // Older issues: apply default BREX, then down-issue via the shared
        // common/to*.xsl stylesheets (mirror toissue()).
        if (_issue < Issue.Iss6)
        {
            string? defBrex = DefaultBrexFor(_issue);
            if (defBrex != null)
            {
                SetBrex(pmDoc, defBrex);
            }
            pmDoc = ToIssue(pmDoc, _issue);
        }

        // Resolve output path. -@ to an existing directory => outdir.
        if (@out != null && Directory.Exists(@out))
        {
            outdir = @out;
            @out = null;
        }

        @out ??= $"PMC-{_modelIdentCode}-{_pmIssuer}-{_pmNumber}-{_pmVolume}{iss}_{langForName}-{_countryIsoCode}.XML";

        // Resolve the output against outdir without changing the process working
        // directory (this runs in-process, so a chdir would leak globally).
        if (outdir != null && !Directory.Exists(outdir))
        {
            stderr.WriteLine($"{Name}: ERROR: Could not change to directory {outdir}: directory does not exist");
            return ExitOsError;
        }
        string target = outdir != null ? Path.Combine(outdir, @out) : @out;
        string display = outdir != null ? $"{outdir}/{@out}" : @out;

        if (!overwrite && File.Exists(target))
        {
            if (noOverwriteError) { return 0; }
            stderr.WriteLine($"{Name}: ERROR: {display} already exists. Use -f to overwrite.");
            return ExitPmExists;
        }

        XmlUtils.SaveDoc(pmDoc, target);

        if (verbose)
        {
            stdout.WriteLine(display);
        }

        return 0;
    }

    private static string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"newpm: ERROR: {opt} requires an argument");
            throw new ExitException(ExitOsError);
        }
        return args[i];
    }

    /// <summary>
    /// Parse a short-option cluster (e.g. <c>-vf</c> or <c>-#PMC-...</c>),
    /// consuming the remainder of the cluster or the next argv item for options
    /// that take an argument. Returns a non-null exit code to terminate, or null
    /// to continue parsing.
    /// </summary>
    private int? ParseShort(string cluster, IReadOnlyList<string> args, ref int i,
        ref string pmcode, ref bool skippmc, ref string? defaultsFname, ref bool noIssue,
        ref bool includeIssueInfo, ref bool includeLanguage, ref bool includeTitle, ref bool includeDate,
        ref bool verbose, ref bool overwrite, ref bool noOverwriteError, ref string? @out,
        TextWriter stdout, TextWriter stderr)
    {
        for (int k = 1; k < cluster.Length; k++)
        {
            char c = cluster[k];
            switch (c)
            {
                case 'p': break; // prompt: no-op in library port
                case 'D': includeDate = true; break;
                case 'N': noIssue = true; break;
                case 'i': includeIssueInfo = true; break;
                case 'l': includeLanguage = true; break;
                case 'T': includeTitle = true; break;
                case 'v': verbose = true; break;
                case 'f': overwrite = true; break;
                case 'q': noOverwriteError = true; break;
                case 'h' or '?': ShowHelp(stdout); return 0;
                case 'a' or 'd' or '#' or 'L' or 'C' or 'n' or 'w' or 'c' or 'r' or 'R'
                    or 't' or 'b' or 'I' or '$' or '@' or '%' or 's' or 'm' or '~' or 'z':
                {
                    string arg;
                    if (k + 1 < cluster.Length)
                    {
                        arg = cluster[(k + 1)..];
                        k = cluster.Length; // consume rest
                    }
                    else
                    {
                        if (++i >= args.Count)
                        {
                            stderr.WriteLine($"{Name}: ERROR: -{c} requires an argument");
                            return ExitOsError;
                        }
                        arg = args[i];
                    }

                    switch (c)
                    {
                        case 'a': _actDmcode = arg; break;
                        case 'd': defaultsFname = arg; break;
                        case '#': pmcode = arg; skippmc = true; break;
                        case 'L': _languageIsoCode = arg; break;
                        case 'C': _countryIsoCode = arg; break;
                        case 'n': _issueNumber = arg; break;
                        case 'w': _inWork = arg; break;
                        case 'c': _securityClassification = arg; break;
                        case 'r': _enterpriseName = arg; break;
                        case 'R': _enterpriseCode = arg; break;
                        case 't': _pmTitle = arg; break;
                        case 'b': _brexDmcode = arg; break;
                        case 'I': _issueDate = arg; break;
                        case '$': _issue = GetIssue(arg, stderr); break;
                        case '@': @out = arg; break;
                        case '%': _templateDir = arg; break;
                        case 's': _shortPmTitle = arg; break;
                        case 'm': _remarks = arg; break;
                        case '~': return DumpTemplate(arg, stderr);
                        case 'z': _issueType = arg; break;
                    }
                    break;
                }
                default:
                    stderr.WriteLine($"{Name}: ERROR: Unknown option: -{c}");
                    return ExitOsError;
            }
        }
        return null;
    }

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

    private XmlDocument XmlSkeleton(TextWriter stderr)
    {
        if (_templateDir != null)
        {
            string src = Path.Combine(_templateDir, "pm.xml");
            if (!File.Exists(src))
            {
                stderr.WriteLine($"{Name}: ERROR: No schema pm in template directory \"{_templateDir}\".");
                throw new ExitException(ExitBadTemplate);
            }
            return XmlUtils.ReadDoc(src);
        }
        return EmbeddedResources.LoadXml("newpm/pm.xml");
    }

    /// <summary>
    /// Down-issue the document to an older S1000D issue using the shared
    /// <c>common/to*.xsl</c> stylesheets. Mirrors <c>toissue</c>: the original
    /// document (DOCTYPE etc.) is preserved and only its root element is replaced
    /// with the transformed result.
    /// </summary>
    private XmlDocument ToIssue(XmlDocument doc, Issue iss)
    {
        string resource = iss switch
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
            _ => throw new ExitException(ExitBadIssue),
        };

        var orig = (XmlDocument)doc.CloneNode(true);

        var xslt = new XslCompiledTransform();
        var settings = new XsltSettings(enableDocumentFunction: false, enableScript: false);
        using (Stream s = EmbeddedResources.Open(resource)
                          ?? throw new FileNotFoundException(resource))
        using (var styleReader = XmlReader.Create(s))
        {
            xslt.Load(styleReader, settings, new XmlUrlResolver());
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

    private int DumpTemplate(string path, TextWriter stderr)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                stderr.WriteLine($"{Name}: ERROR: Cannot dump template to directory: {path}");
                return ExitBadTemplDir;
            }
            string content = EmbeddedResources.ReadText("newpm/pm.xml");
            File.WriteAllText(Path.Combine(path, "pm.xml"), content, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            stderr.WriteLine($"{Name}: ERROR: Cannot dump template to directory: {path}");
            return ExitBadTemplDir;
        }
        return 0;
    }

    /// <summary>Read the <c>.defaults</c> file (XML or simple text), filling unset values.</summary>
    private void LoadDefaults(string? defaultsFname, TextWriter stderr)
    {
        string fname;
        if (defaultsFname != null)
        {
            fname = defaultsFname;
        }
        else if (!Csdb.FindConfig(Csdb.DefaultsFileName, out fname))
        {
            return; // not found; FindConfig returns the bare name but it won't exist
        }

        if (!File.Exists(fname))
        {
            return;
        }

        XmlDocument? defaultsXml = null;
        try
        {
            defaultsXml = XmlUtils.ReadDoc(fname);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            defaultsXml = null;
        }

        if (defaultsXml?.DocumentElement != null)
        {
            foreach (XmlNode cur in defaultsXml.DocumentElement.ChildNodes)
            {
                if (cur is not XmlElement el) { continue; }
                if (!el.HasAttribute("ident") || !el.HasAttribute("value")) { continue; }
                CopyDefaultValue(el.GetAttribute("ident"), el.GetAttribute("value"), stderr);
            }
        }
        else
        {
            foreach (string line in File.ReadLines(fname))
            {
                // sscanf("%31s %255[^\n]"): key (first token), value (rest of line).
                string trimmed = line;
                int p = 0;
                while (p < trimmed.Length && char.IsWhiteSpace(trimmed[p])) { p++; }
                int keyStart = p;
                while (p < trimmed.Length && !char.IsWhiteSpace(trimmed[p])) { p++; }
                if (p == keyStart) { continue; }
                string key = trimmed[keyStart..p];
                while (p < trimmed.Length && char.IsWhiteSpace(trimmed[p])) { p++; }
                if (p >= trimmed.Length) { continue; }
                string val = trimmed[p..];
                CopyDefaultValue(key, val, stderr);
            }
        }
    }

    private void CopyDefaultValue(string key, string val, TextWriter stderr)
    {
        switch (key)
        {
            case "modelIdentCode" when _modelIdentCode.Length == 0: _modelIdentCode = val; break;
            case "pmIssuer" when _pmIssuer.Length == 0: _pmIssuer = val; break;
            case "pmNumber" when _pmNumber.Length == 0: _pmNumber = val; break;
            case "pmVolume" when _pmVolume.Length == 0: _pmVolume = val; break;
            case "languageIsoCode" when _languageIsoCode.Length == 0: _languageIsoCode = val; break;
            case "countryIsoCode" when _countryIsoCode.Length == 0: _countryIsoCode = val; break;
            case "securityClassification" when _securityClassification.Length == 0: _securityClassification = val; break;
            case "responsiblePartnerCompany" when _enterpriseName.Length == 0: _enterpriseName = val; break;
            case "responsiblePartnerCompanyCode" when _enterpriseCode.Length == 0: _enterpriseCode = val; break;
            case "issueNumber" when _issueNumber.Length == 0: _issueNumber = val; break;
            case "inWork" when _inWork.Length == 0: _inWork = val; break;
            case "brex" when _brexDmcode.Length == 0: _brexDmcode = val; break;
            case "issue" when _issue == Issue.None: _issue = GetIssue(val, stderr); break;
            case "templates" when _templateDir == null: _templateDir = val; break;
            case "remarks" when _remarks == null: _remarks = val; break;
            case "act" when _actDmcode == null: _actDmcode = val; break;
            case "issueType" when _issueType == null: _issueType = val; break;
        }
    }

    /// <summary>
    /// Try reading ISO language/country codes from the LANG environment variable,
    /// else default to "und"/"ZZ". Mirrors <c>set_env_lang</c>.
    /// </summary>
    private void SetEnvLang()
    {
        string? env = Environment.GetEnvironmentVariable("LANG");
        if (string.IsNullOrEmpty(env))
        {
            if (_languageIsoCode.Length == 0) { _languageIsoCode = DefaultLanguageIsoCode; }
            if (_countryIsoCode.Length == 0) { _countryIsoCode = DefaultCountryIsoCode; }
            return;
        }

        // strtok(lang, "_") then strtok(NULL, ".") => language, country.
        int us = env.IndexOf('_');
        string? langL = us < 0 ? (env.Length > 0 ? env : null) : env[..us];
        string? langC = null;
        if (us >= 0)
        {
            string after = env[(us + 1)..];
            int dot = after.IndexOf('.');
            langC = dot < 0 ? (after.Length > 0 ? after : null) : after[..dot];
        }

        if (_languageIsoCode.Length == 0)
        {
            _languageIsoCode = langL != null ? Truncate(langL, 3) : DefaultLanguageIsoCode;
        }
        if (_countryIsoCode.Length == 0)
        {
            _countryIsoCode = langC != null ? Truncate(langC, 2) : DefaultCountryIsoCode;
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    private void SetIssueDate(XmlElement issueDate)
    {
        string year, month, day;
        if (_issueDate.Length == 0)
        {
            DateTime now = DateTime.Now;
            year = now.Year.ToString("D4");
            month = now.Month.ToString("D2");
            day = now.Day.ToString("D2");
        }
        else
        {
            // sscanf("%4s-%2s-%2s", ...): three dash-separated components.
            string[] parts = _issueDate.Split('-');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
            {
                throw new ExitException(ExitBadDate);
            }
            year = Truncate(parts[0], 4);
            month = Truncate(parts[1], 2);
            day = Truncate(parts[2], 2);
        }

        issueDate.SetAttribute("year", year);
        issueDate.SetAttribute("month", month);
        issueDate.SetAttribute("day", day);
    }

    private static XmlNode? FindChild(XmlNode parent, string name)
    {
        foreach (XmlNode cur in parent.ChildNodes)
        {
            if (cur.Name == name) { return cur; }
        }
        return null;
    }

    /// <summary>
    /// Parse a DM code from a referenced file name and set the corresponding
    /// attributes on <paramref name="dmCode"/>. Mirrors <c>set_dmcode</c>.
    /// </summary>
    private void SetDmCode(XmlElement dmCode, string fname)
    {
        string code = Path.GetFileName(fname);
        int offset = code.StartsWith("DMC-", StringComparison.Ordinal) ? 4 : 0;
        string body = code[offset..];

        // C: "%14[^-]-%4[^-]-%3[^-]-%c%c-%4[^-]-%2s%3[^-]-%3s%c-%c-%3s%1s"
        // Split on '-' yields: modelIdent, systemDiff, system, subSys+subSubSys,
        // assy, disassy+variant, info+variant, item, learn, learnEvent.
        string[] f = body.Split('-');
        if (f.Length < 7)
        {
            throw new ExitException(ExitBadBrexDmc);
        }

        string modelIdentCode = f[0];
        string systemDiffCode = f[1];
        string systemCode = f[2];
        string subSysCombined = f[3];           // %c%c => subSystem + subSubSystem
        string assyCode = f[4];
        string disassyCombined = f[5];          // %2s%3 => disassy(2) + disassyVariant... actually disassyCode(2)+variant(rest)
        string infoCombined = f[6];             // %3s%c => infoCode(3) + infoCodeVariant(1) ... + itemLocationCode handled below

        if (subSysCombined.Length < 2 || disassyCombined.Length < 3 || infoCombined.Length < 4)
        {
            throw new ExitException(ExitBadBrexDmc);
        }

        string subSystemCode = subSysCombined[..1];
        string subSubSystemCode = subSysCombined.Substring(1, 1);

        string disassyCode = disassyCombined[..2];
        string disassyCodeVariant = disassyCombined[2..];

        string infoCode = infoCombined[..3];
        string infoCodeVariant = infoCombined.Substring(3, 1);

        // Remaining fields: itemLocationCode, then optional learnCode/learnEventCode.
        // C: "...-%c-%3s%1s" -> f[7] = itemLocationCode, f[8] = learnCode+learnEvent (4 chars).
        string itemLocationCode = f.Length > 7 ? f[7] : "";
        string learnCode = "";
        string learnEventCode = "";
        if (f.Length > 8 && f[8].Length >= 4)
        {
            learnCode = f[8][..3];
            learnEventCode = f[8].Substring(3, 1);
        }

        // C accepts only n == 11 (no learn) or n == 13 (with learn).
        bool withoutLearn = f.Length == 8 && itemLocationCode.Length >= 1;
        bool withLearn = f.Length == 9 && itemLocationCode.Length >= 1 && learnCode.Length == 3 && learnEventCode.Length == 1;
        if (!withoutLearn && !withLearn)
        {
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
        dmCode.SetAttribute("itemLocationCode", itemLocationCode[..1]);

        if (learnCode.Length != 0) { dmCode.SetAttribute("learnCode", learnCode); }
        if (learnEventCode.Length != 0) { dmCode.SetAttribute("learnEventCode", learnEventCode); }
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

    private static void SetRemarks(XmlDocument doc, string? text)
    {
        XmlNode? remarks = doc.SelectSingleNode("//remarks");
        if (remarks == null) { return; }

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

    /// <summary>
    /// Add a <c>dmRef</c> for a referenced data module to the pmEntry, copying
    /// the requested identity/address items. Mirrors <c>add_dm_ref</c>.
    /// </summary>
    private void AddDmRef(XmlElement pmEntry, string path, bool includeIssueInfo,
        bool includeLanguage, bool includeTitle, bool includeDate, TextWriter stderr)
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

        XmlDocument owner = pmEntry.OwnerDocument!;
        XmlNode? identExtension = dmodule.SelectSingleNode("//dmIdent/identExtension");
        XmlNode? dmCode = dmodule.SelectSingleNode("//dmIdent/dmCode");
        XmlNode? issueInfo = dmodule.SelectSingleNode("//dmIdent/issueInfo");
        XmlNode? language = dmodule.SelectSingleNode("//dmIdent/language");

        XmlElement dmRef = owner.CreateElement("dmRef");
        XmlElement dmRefIdent = owner.CreateElement("dmRefIdent");
        dmRef.AppendChild(dmRefIdent);

        if (identExtension != null)
        {
            dmRefIdent.AppendChild(owner.ImportNode(identExtension, true));
        }
        if (dmCode != null)
        {
            dmRefIdent.AppendChild(owner.ImportNode(dmCode, true));
        }
        if (includeIssueInfo && issueInfo != null)
        {
            dmRefIdent.AppendChild(owner.ImportNode(issueInfo, true));
        }
        if (includeLanguage && language != null)
        {
            dmRefIdent.AppendChild(owner.ImportNode(language, true));
        }

        if (includeTitle || includeDate)
        {
            XmlElement dmRefAddressItems = owner.CreateElement("dmRefAddressItems");
            dmRef.AppendChild(dmRefAddressItems);

            if (includeTitle)
            {
                XmlNode? dmTitle = dmodule.SelectSingleNode("//dmAddressItems/dmTitle");
                if (dmTitle != null)
                {
                    dmRefAddressItems.AppendChild(owner.ImportNode(dmTitle, true));
                }
            }
            if (includeDate)
            {
                XmlNode? issueDate = dmodule.SelectSingleNode("//dmAddressItems/issueDate");
                if (issueDate != null)
                {
                    dmRefAddressItems.AppendChild(owner.ImportNode(issueDate, true));
                }
            }
        }

        pmEntry.AppendChild(dmRef);
    }

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: s1kd-newpm [options] [<dmodule>...]");
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
        stdout.WriteLine("  -v, --verbose               Print file name of pub module.");
        stdout.WriteLine("  -T, --include-title         Include titles in referenced data modules.");
        stdout.WriteLine("  --version                   Show version information.");
        stdout.WriteLine("  <dmodule>...                Data modules to include in new PM.");
        stdout.WriteLine();
        stdout.WriteLine("In addition, the following pieces of meta data can be set:");
        stdout.WriteLine("  -#, --code <code>           Publication module code");
        stdout.WriteLine("  -a, --act <ACT>             ACT data module code");
        stdout.WriteLine("  -b, --brex <BREX>           BREX data module code");
        stdout.WriteLine("  -C, --country <country>     Country ISO code");
        stdout.WriteLine("  -c, --security <sec>        Security classification");
        stdout.WriteLine("  -I, --date <date>           Issue date");
        stdout.WriteLine("  -L, --language <lang>       Language ISO code");
        stdout.WriteLine("  -m, --remarks <remarks>     Remarks");
        stdout.WriteLine("  -n, --issno <iss>           Issue number");
        stdout.WriteLine("  -R, --rpccode <CAGE>        Responsible partner company code");
        stdout.WriteLine("  -r, --rpcname <RPC>         Responsible partner company enterprise name");
        stdout.WriteLine("  -s, --short-title <title>   Short PM title");
        stdout.WriteLine("  -t, --title <title>         Publication module title");
        stdout.WriteLine("  -w, --inwork <inwork>       Inwork issue");
        stdout.WriteLine("  -z, --issue-type <type>     Issue type");
    }
}
