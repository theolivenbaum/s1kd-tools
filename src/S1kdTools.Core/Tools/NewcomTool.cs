using System.Globalization;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-newcom</c>: create a new S1000D comment with the code and
/// metadata specified. Mirrors the C tool's option set, exit codes, comment-code
/// parsing/validation, the generated <c>COM-…</c> filename, the issue-date,
/// language, originator, security, priority, response-type, BREX, remarks and
/// title handling, and the <c>.defaults</c> file lookup.
/// </summary>
/// <remarks>
/// The built-in template (<c>comment.xml</c>, S1000D issue 6) is embedded under
/// <c>Resources/newcom/</c> and populated entirely with the
/// <see cref="XmlDocument"/> DOM — no XSLT is needed for the default issue.
///
/// For issues earlier than 6 the C tool applies the shared
/// <c>common/to&lt;NN&gt;.xsl</c> stylesheets to down-convert the document. Those
/// stylesheets are not embedded in this port, so the element-renaming conversion
/// is not performed; the issue-specific default BREX is still applied (matching
/// the C tool's behaviour for that part). This deviation is documented for the
/// record — the default and most common path (issue 6) is fully ported.
/// </remarks>
public sealed class NewcomTool : ITool
{
    public string Name => "newcom";
    public string Description => "Create a new S1000D comment.";
    public string Version => "3.0.2";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitBadCode = 1;       // EXIT_BAD_CODE
    private const int ExitCommentExists = 2; // EXIT_COMMENT_EXISTS
    private const int ExitBadBrexDmc = 3;    // EXIT_BAD_BREX_DMC
    private const int ExitBadDate = 4;       // EXIT_BAD_DATE
    private const int ExitBadIssue = 5;      // EXIT_BAD_ISSUE
    private const int ExitBadTemplate = 6;   // EXIT_BAD_TEMPLATE
    private const int ExitBadTemplDir = 7;   // EXIT_BAD_TEMPL_DIR
    private const int ExitOsError = 8;       // EXIT_OS_ERROR

    private const string DefaultLanguageIsoCode = "und";
    private const string DefaultCountryIsoCode = "ZZ";

    // Issue ordering matches the C enum so "< ISS_6" comparisons work.
    private enum Issue { None, Iss20, Iss21, Iss22, Iss23, Iss30, Iss40, Iss41, Iss42, Iss50, Iss6 }

    private const Issue DefaultIssue = Issue.Iss6;

    private static readonly Dictionary<Issue, string> DefaultBrex = new()
    {
        [Issue.Iss22] = "AE-A-04-10-0301-00A-022A-D",
        [Issue.Iss23] = "AE-A-04-10-0301-00A-022A-D",
        [Issue.Iss30] = "AE-A-04-10-0301-00A-022A-D",
        [Issue.Iss40] = "S1000D-A-04-10-0301-00A-022A-D",
        [Issue.Iss41] = "S1000D-E-04-10-0301-00A-022A-D",
        [Issue.Iss42] = "S1000D-F-04-10-0301-00A-022A-D",
        [Issue.Iss50] = "S1000D-G-04-10-0301-00A-022A-D",
    };

    /* Per-run metadata state (mirrors the C static char buffers). */
    private string _modelIdentCode = "";
    private string _senderIdent = "";
    private string _yearOfDataIssue = "";
    private string _seqNumber = "";
    private string _commentType = "";
    private string _languageIsoCode = "";
    private string _countryIsoCode = "";
    private string _enterpriseName = "";
    private string _addressCity = "";
    private string _addressCountry = "";
    private string _securityClassification = "";
    private string _commentPriorityCode = "";
    private string _responseType = "";
    private string _brexDmcode = "";
    private string _issueDate = "";
    private string? _issueType;
    private string? _remarks;
    private string? _templateDir;
    private Issue _issue = Issue.None;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string code = "";
        bool skipCode = false;
        bool showPrompts = false;
        string commentTitle = "";
        bool verbose = false;
        bool overwrite = false;
        bool noOverwriteError = false;
        string? defaultsFname = null;
        bool customDefaults = false;
        string? @out = null;

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
                    case "-d" or "--defaults":
                        defaultsFname = NextArg(args, ref i, a, stderr);
                        customDefaults = true;
                        break;
                    case "-p" or "--prompt":
                        showPrompts = true;
                        break;
                    case "-#" or "--code":
                        skipCode = true;
                        code = NextArg(args, ref i, a, stderr);
                        break;
                    case "-o" or "--origname":
                        _enterpriseName = NextArg(args, ref i, a, stderr);
                        break;
                    case "-c" or "--security":
                        _securityClassification = Take(NextArg(args, ref i, a, stderr), 2);
                        break;
                    case "-L" or "--language":
                        _languageIsoCode = Take(NextArg(args, ref i, a, stderr), 3);
                        break;
                    case "-C" or "--country":
                        _countryIsoCode = Take(NextArg(args, ref i, a, stderr), 2);
                        break;
                    case "-P" or "--priority":
                        _commentPriorityCode = Take(NextArg(args, ref i, a, stderr), 4);
                        break;
                    case "-t" or "--title":
                        commentTitle = NextArg(args, ref i, a, stderr);
                        break;
                    case "-r" or "--response":
                        _responseType = Take(NextArg(args, ref i, a, stderr), 4);
                        break;
                    case "-b" or "--brex":
                        _brexDmcode = NextArg(args, ref i, a, stderr);
                        break;
                    case "-I" or "--date":
                        _issueDate = NextArg(args, ref i, a, stderr);
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
                        @out = NextArg(args, ref i, a, stderr);
                        break;
                    case "-%" or "--templates":
                        _templateDir = NextArg(args, ref i, a, stderr);
                        break;
                    case "-q" or "--quiet":
                        noOverwriteError = true;
                        break;
                    case "-m" or "--remarks":
                        _remarks = NextArg(args, ref i, a, stderr);
                        break;
                    case "-~" or "--dump-templates":
                        DumpTemplate(NextArg(args, ref i, a, stderr), stderr);
                        return 0;
                    case "-z" or "--issue-type":
                        _issueType = NextArg(args, ref i, a, stderr);
                        break;
                    default:
                        // The C tool ignores positional args; unknown options
                        // are reported (getopt would). Be lenient and ignore
                        // bare tokens to stay close to "options only" usage.
                        if (a.StartsWith('-') && a.Length > 1 && a != "-")
                        {
                            stderr.WriteLine($"s1kd-{Name}: ERROR: Unknown option: {a}");
                            return ExitBadCode;
                        }
                        break;
                }
            }

            // .defaults handling.
            string defaultsPath;
            if (customDefaults && defaultsFname != null)
            {
                defaultsPath = defaultsFname;
            }
            else
            {
                Csdb.FindConfig(Csdb.DefaultsFileName, out defaultsPath);
            }
            ReadDefaults(defaultsPath);

            XmlDocument commentDoc = XmlSkeleton(stderr);

            // Parse -# code (COM- prefix optional).
            if (code.Length != 0)
            {
                string c = code.StartsWith("COM-", StringComparison.Ordinal) ? code[4..] : code;
                string[] parts = c.Split('-');
                if (parts.Length != 5 ||
                    parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0 ||
                    parts[3].Length == 0 || parts[4].Length == 0)
                {
                    stderr.WriteLine($"s1kd-{Name}: ERROR: Invalid comment code: '{code}'");
                    throw new ExitException(ExitBadCode);
                }
                _modelIdentCode = parts[0];
                _senderIdent = parts[1];
                _yearOfDataIssue = parts[2];
                _seqNumber = parts[3];
                _commentType = parts[4];
            }

            if (showPrompts)
            {
                if (!skipCode)
                {
                    _modelIdentCode = Prompt("Model ident code", _modelIdentCode, stdout);
                    _senderIdent = Prompt("Sender ident", _senderIdent, stdout);
                    _yearOfDataIssue = Prompt("Year of data issue", _yearOfDataIssue, stdout);
                    _seqNumber = Prompt("Sequence numer", _seqNumber, stdout);
                    _commentType = Prompt("Comment type", _commentType, stdout);
                }
                _languageIsoCode = Prompt("Language ISO code", _languageIsoCode, stdout);
                _countryIsoCode = Prompt("Country ISO code", _countryIsoCode, stdout);
                _enterpriseName = Prompt("Originator enterprise name", _enterpriseName, stdout);
                _addressCity = Prompt("Originator city", _addressCity, stdout);
                _addressCountry = Prompt("Originator country", _addressCountry, stdout);
                _securityClassification = Prompt("Security classification", _securityClassification, stdout);
                _commentPriorityCode = Prompt("Comment priority code", _commentPriorityCode, stdout);
            }

            if (_modelIdentCode.Length == 0 || _senderIdent.Length == 0 ||
                _yearOfDataIssue.Length == 0 || _seqNumber.Length == 0 ||
                _commentType.Length == 0)
            {
                stderr.WriteLine(
                    $"s1kd-{Name}: ERROR: Missing required comment code components: " +
                    $"COM-{Or(_modelIdentCode)}-{Or(_senderIdent)}-{Or(_yearOfDataIssue)}-" +
                    $"{Or(_seqNumber)}-{Or(_commentType)}");
                throw new ExitException(ExitBadCode);
            }

            if (_issue == Issue.None) _issue = DefaultIssue;
            if (_securityClassification.Length == 0) _securityClassification = "01";
            if (_responseType.Length == 0) _responseType = "rt02";
            if (_commentPriorityCode.Length == 0) _commentPriorityCode = "cp01";

            SetEnvLang();
            _languageIsoCode = _languageIsoCode.ToLowerInvariant();
            _countryIsoCode = _countryIsoCode.ToUpperInvariant();
            _commentType = _commentType.ToLowerInvariant();

            // Locate the template nodes (the skeleton is un-namespaced).
            XmlElement comment = commentDoc.DocumentElement!;
            XmlElement identAndStatusSection = FindChild(comment, "identAndStatusSection")!;
            XmlElement commentAddress = FindChild(identAndStatusSection, "commentAddress")!;
            XmlElement commentIdent = FindChild(commentAddress, "commentIdent")!;
            XmlElement commentCode = FindChild(commentIdent, "commentCode")!;
            XmlElement language = FindChild(commentIdent, "language")!;
            XmlElement commentAddressItems = FindChild(commentAddress, "commentAddressItems")!;
            XmlElement issueDate = FindChild(commentAddressItems, "issueDate")!;
            XmlElement commentOriginator = FindChild(commentAddressItems, "commentOriginator")!;
            XmlElement dispatchAddress = FindChild(commentOriginator, "dispatchAddress")!;
            XmlElement enterprise = FindChild(dispatchAddress, "enterprise")!;
            XmlElement enterpriseName = FindChild(enterprise, "enterpriseName")!;
            XmlElement address = FindChild(dispatchAddress, "address")!;
            XmlElement city = FindChild(address, "city")!;
            XmlElement country = FindChild(address, "country")!;
            XmlElement commentStatus = FindChild(identAndStatusSection, "commentStatus")!;
            XmlElement security = FindChild(commentStatus, "security")!;
            XmlElement commentPriority = FindChild(commentStatus, "commentPriority")!;
            XmlElement commentResponse = FindChild(commentStatus, "commentResponse")!;

            commentCode.SetAttribute("modelIdentCode", _modelIdentCode);
            commentCode.SetAttribute("senderIdent", _senderIdent);
            commentCode.SetAttribute("yearOfDataIssue", _yearOfDataIssue);
            commentCode.SetAttribute("seqNumber", _seqNumber);
            commentCode.SetAttribute("commentType", _commentType);

            language.SetAttribute("languageIsoCode", _languageIsoCode);
            language.SetAttribute("countryIsoCode", _countryIsoCode);

            if (commentTitle.Length != 0)
            {
                XmlElement commentTitleNode = commentDoc.CreateElement("commentTitle");
                commentTitleNode.InnerText = commentTitle;
                commentAddressItems.InsertBefore(commentTitleNode, issueDate);
            }

            SetIssueDate(issueDate, stderr);

            if (_issueType != null) commentStatus.SetAttribute("issueType", _issueType);

            enterpriseName.InnerText = _enterpriseName;
            city.InnerText = _addressCity;
            country.InnerText = _addressCountry;

            security.SetAttribute("securityClassification", _securityClassification);
            commentPriority.SetAttribute("commentPriorityCode", _commentPriorityCode);
            commentResponse.SetAttribute("responseType", _responseType);

            if (_brexDmcode.Length != 0)
            {
                SetBrex(commentDoc, _brexDmcode, stderr);
            }

            SetRemarks(commentDoc, _remarks);

            // Filename language component is upper-cased; commentType too.
            string languageFname = _languageIsoCode.ToUpperInvariant();
            _commentType = _commentType.ToUpperInvariant();

            if (_issue < Issue.Iss6)
            {
                if (_brexDmcode.Length == 0 && DefaultBrex.TryGetValue(_issue, out string? brex))
                {
                    SetBrex(commentDoc, brex, stderr);
                }
                // NOTE: the C tool here also down-converts the document with the
                // common/to<NN>.xsl stylesheet. Those stylesheets are not
                // embedded in this port, so the element-renaming conversion is
                // skipped. See the class remarks.
            }

            // Resolve output path / directory.
            string? outdir = null;
            string? outFile = @out;
            if (outFile != null && Directory.Exists(outFile))
            {
                outdir = outFile;
                outFile = null;
            }

            outFile ??= $"COM-{_modelIdentCode}-{_senderIdent}-{_yearOfDataIssue}-" +
                        $"{_seqNumber}-{_commentType}_{languageFname}-{_countryIsoCode}.XML";

            string fullPath = outdir != null ? Path.Combine(outdir, outFile) : outFile;

            if (!overwrite && File.Exists(fullPath))
            {
                if (noOverwriteError) return 0;
                stderr.WriteLine($"s1kd-{Name}: ERROR: {fullPath} already exists. Use -f to overwrite.");
                throw new ExitException(ExitCommentExists);
            }

            try
            {
                XmlUtils.SaveDoc(commentDoc, fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not write {fullPath}: {ex.Message}");
                throw new ExitException(ExitOsError);
            }

            if (verbose)
            {
                stdout.WriteLine(fullPath);
            }
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }

        return 0;
    }

    /* ----- helpers mirroring the C static functions ----- */

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: {opt} requires an argument");
            throw new ExitException(ExitBadCode);
        }
        return args[i];
    }

    private static string Take(string s, int n) => s.Length <= n ? s : s[..n];

    private static string Or(string s) => s.Length == 0 ? "???" : s;

    /// <summary>Load the built-in template, or one from a template directory.</summary>
    private XmlDocument XmlSkeleton(TextWriter stderr)
    {
        if (_templateDir != null)
        {
            string src = Path.Combine(_templateDir, "comment.xml");
            if (!File.Exists(src))
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: No schema comment in template directory \"{_templateDir}\".");
                throw new ExitException(ExitBadTemplate);
            }
            return XmlUtils.ReadDoc(src);
        }
        return EmbeddedResources.LoadXml("newcom/comment.xml");
    }

    private Issue GetIssue(string iss, TextWriter stderr)
    {
        return iss switch
        {
            "6" => Issue.Iss6,
            "5.0" => Issue.Iss50,
            "4.2" => Issue.Iss42,
            "4.1" => Issue.Iss41,
            "4.0" => Issue.Iss40,
            "3.0" => Issue.Iss30,
            "2.3" => Issue.Iss23,
            "2.2" => Issue.Iss22,
            "2.1" => Issue.Iss21,
            "2.0" => Issue.Iss20,
            _ => BadIssue(iss, stderr),
        };
    }

    private Issue BadIssue(string iss, TextWriter stderr)
    {
        stderr.WriteLine($"s1kd-{Name}: ERROR: Unsupported issue: {iss}");
        throw new ExitException(ExitBadIssue);
    }

    private static XmlElement? FindChild(XmlNode parent, string name)
    {
        for (XmlNode? cur = parent.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur is XmlElement el && el.Name == name)
            {
                return el;
            }
        }
        return null;
    }

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
            // sscanf("%4s-%2s-%2s") requires three '-'-separated fields.
            string[] parts = _issueDate.Split('-');
            if (parts.Length < 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
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

    private void SetBrex(XmlDocument doc, string code, TextWriter stderr)
    {
        var dmCode = XmlUtils.XPathFirstNode(doc, null, "//brexDmRef/dmRef/dmRefIdent/dmCode") as XmlElement;
        if (dmCode == null)
        {
            return;
        }

        // Mirror the C sscanf:
        //   %14[^-]-%4[^-]-%3[^-]-%c%c-%4[^-]-%2s%3[^-]-%3s%c-%c-%3s%1s
        // i.e. fields separated by '-', with subSystem/subSubSystem packed in
        // one group, and assy/disassyVariant + infoVariant packed similarly.
        // Required n==11 (no learn) or n==13 (with learn code).
        string[] g = code.Split('-');
        // Expected groups:
        // 0: modelIdentCode
        // 1: systemDiffCode
        // 2: systemCode
        // 3: subSystemCode(1) + subSubSystemCode(1)
        // 4: assyCode
        // 5: disassyCode(2) + disassyCodeVariant(rest)
        // 6: infoCode(3) + infoCodeVariant(1)
        // 7: itemLocationCode(1)
        // 8 (optional): learnCode(3)
        // 9 (optional): learnEventCode(1)
        if (g.Length != 8 && g.Length != 10)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Bad BREX data module code.");
            throw new ExitException(ExitBadBrexDmc);
        }

        string sub = g[3];
        string disassy = g[5];
        string info = g[6];

        if (sub.Length < 2 || disassy.Length < 3 || info.Length < 4 || g[7].Length < 1)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Bad BREX data module code.");
            throw new ExitException(ExitBadBrexDmc);
        }

        string modelIdentCode = g[0];
        string systemDiffCode = g[1];
        string systemCode = g[2];
        string subSystemCode = sub[..1];
        string subSubSystemCode = sub.Substring(1, 1);
        string assyCode = g[4];
        string disassyCode = disassy[..2];
        string disassyCodeVariant = disassy[2..];
        string infoCode = info[..3];
        string infoCodeVariant = info.Substring(3, 1);
        string itemLocationCode = g[7][..1];
        string learnCode = g.Length == 10 ? g[8] : "";
        string learnEventCode = g.Length == 10 ? g[9] : "";

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

    private void SetRemarks(XmlDocument doc, string? text)
    {
        var remarks = XmlUtils.XPathFirstNode(doc, null, "//remarks") as XmlElement;
        if (remarks == null)
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

    /// <summary>
    /// Derive language/country ISO codes from the <c>LANG</c> environment
    /// variable, falling back to the defaults. Mirrors <c>set_env_lang</c>.
    /// </summary>
    private void SetEnvLang()
    {
        string? env = Environment.GetEnvironmentVariable("LANG");
        if (string.IsNullOrEmpty(env))
        {
            if (_languageIsoCode.Length == 0) _languageIsoCode = DefaultLanguageIsoCode;
            if (_countryIsoCode.Length == 0) _countryIsoCode = DefaultCountryIsoCode;
            return;
        }

        // strtok(lang, "_") then strtok(NULL, ".").
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

    /// <summary>
    /// Read the <c>.defaults</c> file (XML or whitespace-delimited text) and copy
    /// any value not already set on the command line. Mirrors the defaults loop
    /// plus <c>copy_default_value</c>.
    /// </summary>
    private void ReadDefaults(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // Try XML first (read_xml_doc); fall back to the text format.
        XmlDocument? xml = null;
        try
        {
            xml = XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is XmlException or IOException)
        {
            xml = null;
        }

        if (xml?.DocumentElement != null)
        {
            for (XmlNode? cur = xml.DocumentElement.FirstChild; cur != null; cur = cur.NextSibling)
            {
                if (cur is not XmlElement el) continue;
                if (!el.HasAttribute("ident") || !el.HasAttribute("value")) continue;
                CopyDefaultValue(el.GetAttribute("ident"), el.GetAttribute("value"));
            }
            return;
        }

        foreach (string line in File.ReadLines(path))
        {
            // sscanf("%31s %255[^\n]"): key = first token, value = rest of line.
            string trimmed = line.TrimStart();
            int sp = trimmed.IndexOfAny(new[] { ' ', '\t' });
            if (sp <= 0) continue;
            string key = trimmed[..sp];
            string value = trimmed[(sp + 1)..].TrimEnd('\r', '\n');
            if (value.Length == 0) continue;
            CopyDefaultValue(key, value);
        }
    }

    private void CopyDefaultValue(string key, string value)
    {
        switch (key)
        {
            case "modelIdentCode" when _modelIdentCode.Length == 0: _modelIdentCode = value; break;
            case "senderIdent" when _senderIdent.Length == 0: _senderIdent = value; break;
            case "yearOfDataIssue" when _yearOfDataIssue.Length == 0: _yearOfDataIssue = value; break;
            case "seqNumber" when _seqNumber.Length == 0: _seqNumber = value; break;
            case "commentType" when _commentType.Length == 0: _commentType = value; break;
            case "languageIsoCode" when _languageIsoCode.Length == 0: _languageIsoCode = value; break;
            case "countryIsoCode" when _countryIsoCode.Length == 0: _countryIsoCode = value; break;
            case "originator" when _enterpriseName.Length == 0: _enterpriseName = value; break;
            case "city" when _addressCity.Length == 0: _addressCity = value; break;
            case "country" when _addressCountry.Length == 0: _addressCountry = value; break;
            case "securityClassification" when _securityClassification.Length == 0: _securityClassification = value; break;
            case "commentPriorityCode" when _commentPriorityCode.Length == 0: _commentPriorityCode = value; break;
            case "brex" when _brexDmcode.Length == 0: _brexDmcode = value; break;
            case "templates" when _templateDir == null: _templateDir = value; break;
            case "remarks" when _remarks == null: _remarks = value; break;
            case "issue" when _issue == Issue.None: _issue = GetIssueLenient(value); break;
            case "issueType" when _issueType == null: _issueType = value; break;
        }
    }

    /// <summary>
    /// Resolve an issue string from a defaults file. The C tool calls
    /// <c>get_issue</c> which exits on an unknown value; here an unknown value is
    /// left as <see cref="Issue.None"/> (later defaulted) to avoid aborting on a
    /// malformed config — the command line is the authoritative source.
    /// </summary>
    private Issue GetIssueLenient(string iss) => iss switch
    {
        "6" => Issue.Iss6,
        "5.0" => Issue.Iss50,
        "4.2" => Issue.Iss42,
        "4.1" => Issue.Iss41,
        "4.0" => Issue.Iss40,
        "3.0" => Issue.Iss30,
        "2.3" => Issue.Iss23,
        "2.2" => Issue.Iss22,
        "2.1" => Issue.Iss21,
        "2.0" => Issue.Iss20,
        _ => Issue.None,
    };

    private void DumpTemplate(string path, TextWriter stderr)
    {
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Cannot dump template in directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }
        try
        {
            byte[] bytes = EmbeddedResources.ReadBytes("newcom/comment.xml");
            File.WriteAllBytes(Path.Combine(path, "comment.xml"), bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Cannot dump template in directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }
    }

    /// <summary>
    /// Interactive prompt. Reads from <see cref="Console.In"/>; if no console
    /// input is available the existing value is kept (tests run non-interactive).
    /// </summary>
    private static string Prompt(string label, string current, TextWriter stdout)
    {
        if (current.Length == 0)
        {
            stdout.Write($"{label}: ");
        }
        else
        {
            stdout.Write($"{label} [{current}]: ");
        }

        string? input = Console.In.ReadLine();
        if (string.IsNullOrEmpty(input))
        {
            return current;
        }
        return input;
    }

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -$, --issue <issue>         Specify which S1000D issue to use.");
        stdout.WriteLine("  -@, --out <path>            Output to specified file or directory.");
        stdout.WriteLine("  -%, --templates <dir>       Use templates in specified directory.");
        stdout.WriteLine("  -~, --dump-templates <dir>  Dump built-in XML template to directory.");
        stdout.WriteLine("  -d, --defaults <file>       Specify the .defaults file name.");
        stdout.WriteLine("  -f, --overwrite             Overwrite existing file.");
        stdout.WriteLine("  -p, --prompt                Prompt the user for each value.");
        stdout.WriteLine("  -q, --quiet                 Don't report an error if file exists.");
        stdout.WriteLine("  -v, --verbose               Print file name of comment.");
        stdout.WriteLine("  --version                   Show version information.");
        stdout.WriteLine();
        stdout.WriteLine("In addition, the following pieces of meta data can be set:");
        stdout.WriteLine("  -#, --code <code>           Comment code");
        stdout.WriteLine("  -b, --brex <BREX>           BREX data module code");
        stdout.WriteLine("  -C, --country <country>     Country ISO code");
        stdout.WriteLine("  -c, --security <sec>        Security classification");
        stdout.WriteLine("  -I, --date <date>           Issue date");
        stdout.WriteLine("  -m, --remarks <remarks>     Remarks");
        stdout.WriteLine("  -L, --language <lang>       Language ISO code");
        stdout.WriteLine("  -o, --origname <orig>       Originator");
        stdout.WriteLine("  -P, --priority <code>       Priority code");
        stdout.WriteLine("  -r, --response <type>       Response type");
        stdout.WriteLine("  -t, --title <title>         Comment title");
        stdout.WriteLine("  -z, --issue-type <type>     Issue type");
    }
}
