using System.Globalization;
using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-newimf</c>: create a new ICN metadata file (IMF) for one or
/// more ICNs.
/// </summary>
/// <remarks>
/// Mirrors <c>reference/tools/s1kd-newimf/s1kd-newimf.c</c> (VERSION 3.0.1).
/// <para>
/// For each ICN argument of the form <c>ICN-&lt;ident&gt;.&lt;ext&gt;</c> the tool
/// derives the ICN identifier (<c>imfIdentIcn</c>) and populates a skeleton
/// <c>icnMetadataFile</c> document with issue/inwork numbers, title, issue date,
/// security classification, responsible partner company, originator, BREX data
/// module code and remarks, before writing it to
/// <c>IMF-&lt;ident&gt;_&lt;issue&gt;-&lt;inwork&gt;.XML</c> (or <c>IMF-&lt;ident&gt;.XML</c>
/// with <c>-N</c>).
/// </para>
/// <para>
/// Deviation from the C: downgrading the issued document to S1000D issue 4.2 or
/// 5.0 is implemented in the C via the EXSLT-based <c>to42.xsl</c>/<c>to50.xsl</c>
/// stylesheets. Those transforms are not ported here (EXSLT is not supported by
/// <see cref="System.Xml.Xsl.XslCompiledTransform"/>), so when <c>-$ 4.2</c> or
/// <c>-$ 5.0</c> is requested the issue-specific default BREX is still applied but
/// the schema-version downgrade transform is skipped. Issue 6 (the default) is
/// fully supported.
/// </para>
/// </remarks>
public sealed class NewimfTool : ITool
{
    public string Name => "newimf";
    public string Description => "Create a new ICN metadata file (IMF).";
    public string Version => "3.0.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitImfExists = 1;
    private const int ExitBadBrexDmc = 2;
    private const int ExitBadDate = 3;
    private const int ExitBadTemplate = 4;
    private const int ExitBadTemplDir = 5;
    private const int ExitEncodingError = 6;
    private const int ExitOsError = 7;
    private const int ExitBadIssue = 8;

    private const string Iss42DefaultBrex = "S1000D-F-04-10-0301-00A-022A-D";
    private const string Iss50DefaultBrex = "S1000D-G-04-10-0301-00A-022A-D";

    private enum Issue { None, Iss42, Iss50, Iss6 }

    private const Issue DefaultS1000DIssue = Issue.Iss6;

    /* Per-run metadata state (mirrors the C static globals). */
    private string _issueNumber = "";
    private string _inWork = "";
    private string _securityClassification = "";
    private string _responsiblePartnerCompany = "";
    private string _responsiblePartnerCompanyCode = "";
    private string _originator = "";
    private string _originatorCode = "";
    private string _icnTitle = "";
    private string _brexDmcode = "";
    private string _issueDate = "";
    private string? _templateDir;
    private string? _remarks;
    private Issue _issue = Issue.None;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool showPrompts = false;
        bool noIssue = false;
        bool verbose = false;
        bool overwrite = false;
        bool noOverwriteError = false;

        string? defaultsFname = null;
        bool customDefaults = false;

        string? @out = null;
        string? outdir = null;

        var icns = new List<string>();

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
                    case "-p" or "--prompts":
                        showPrompts = true;
                        break;
                    case "-d" or "--defaults":
                        defaultsFname = NextArg(args, ref i, a, stderr);
                        customDefaults = true;
                        break;
                    case "-n" or "--issno":
                        _issueNumber = Truncate(NextArg(args, ref i, a, stderr), 6);
                        break;
                    case "-w" or "--inwork":
                        _inWork = Truncate(NextArg(args, ref i, a, stderr), 3);
                        break;
                    case "-c" or "--security":
                        _securityClassification = Truncate(NextArg(args, ref i, a, stderr), 2);
                        break;
                    case "-r" or "--rpcname":
                        _responsiblePartnerCompany = Truncate(NextArg(args, ref i, a, stderr), 255);
                        break;
                    case "-R" or "--rpccode":
                        _responsiblePartnerCompanyCode = Truncate(NextArg(args, ref i, a, stderr), 5);
                        break;
                    case "-o" or "--origname":
                        _originator = Truncate(NextArg(args, ref i, a, stderr), 255);
                        break;
                    case "-O" or "--origcode":
                        _originatorCode = Truncate(NextArg(args, ref i, a, stderr), 5);
                        break;
                    case "-N" or "--omit-issue":
                        noIssue = true;
                        break;
                    case "-t" or "--title":
                        _icnTitle = Truncate(NextArg(args, ref i, a, stderr), 255);
                        break;
                    case "-b" or "--brex":
                        _brexDmcode = Truncate(NextArg(args, ref i, a, stderr), 255);
                        break;
                    case "-I" or "--date":
                        _issueDate = Truncate(NextArg(args, ref i, a, stderr), 15);
                        break;
                    case "-v" or "--verbose":
                        verbose = true;
                        break;
                    case "-f" or "--overwrite":
                        overwrite = true;
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
                    case "-@" or "--out":
                        @out = NextArg(args, ref i, a, stderr);
                        break;
                    case "-$" or "--issue":
                        _issue = GetIssue(NextArg(args, ref i, a, stderr), stderr);
                        break;
                    default:
                        if (a.StartsWith('-') && a.Length > 1 && a != "-")
                        {
                            stderr.WriteLine($"s1kd-{Name}: ERROR: Unknown option: {a}");
                            return 2;
                        }
                        icns.Add(a);
                        break;
                }
            }

            // Locate the .defaults file unless one was supplied with -d.
            if (!customDefaults)
            {
                Csdb.FindConfig(Csdb.DefaultsFileName, out string found);
                defaultsFname = found;
            }

            ReadDefaults(defaultsFname ?? "", stderr);

            if (showPrompts)
            {
                _issueNumber = Prompt("Issue number", _issueNumber, stdout);
                _inWork = Prompt("In-work issue", _inWork, stdout);
                _securityClassification = Prompt("Security classification", _securityClassification, stdout);
                _responsiblePartnerCompany = Prompt("Responsible partner company", _responsiblePartnerCompany, stdout);
                _originator = Prompt("Originator", _originator, stdout);
                _icnTitle = Prompt("ICN title", _icnTitle, stdout);
            }

            if (_issue == Issue.None) _issue = DefaultS1000DIssue;
            if (_issueNumber.Length == 0) _issueNumber = "000";
            if (_inWork.Length == 0) _inWork = "01";
            if (_securityClassification.Length == 0) _securityClassification = "01";

            if (@out != null && Directory.Exists(@out))
            {
                outdir = @out;
                @out = null;
            }

            string baseDir = outdir ?? Directory.GetCurrentDirectory();
            if (outdir != null && !Directory.Exists(outdir))
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Could not change to directory {outdir}.");
                throw new ExitException(ExitOsError);
            }

            foreach (string arg in icns)
            {
                // n = sscanf(argv[i], "ICN-%255[^.].%*s", icn): require "ICN-",
                // then take chars up to first '.', and require a '.' after.
                string? icn = ParseIcn(arg);
                if (icn == null)
                {
                    continue;
                }

                XmlDocument template = XmlSkeleton(stderr);

                var node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfIdent/imfCode")!;
                node.SetAttribute("imfIdentIcn", icn);

                node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfIdent/issueInfo")!;
                node.SetAttribute("issueNumber", _issueNumber);
                node.SetAttribute("inWork", _inWork);

                node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfAddressItems/icnTitle")!;
                node.InnerText = _icnTitle;

                node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfAddressItems/issueDate")!;
                SetIssueDate(node, stderr);

                node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfStatus/security")!;
                node.SetAttribute("securityClassification", _securityClassification);

                if (_responsiblePartnerCompanyCode.Length != 0)
                {
                    node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfStatus/responsiblePartnerCompany")!;
                    node.SetAttribute("enterpriseCode", _responsiblePartnerCompanyCode);
                }

                if (_responsiblePartnerCompany.Length != 0)
                {
                    node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfStatus/responsiblePartnerCompany")!;
                    var child = template.CreateElement("enterpriseName");
                    child.InnerText = _responsiblePartnerCompany;
                    node.AppendChild(child);
                }

                if (_originatorCode.Length != 0)
                {
                    node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfStatus/originator")!;
                    node.SetAttribute("enterpriseCode", _originatorCode);
                }

                if (_originator.Length != 0)
                {
                    node = (XmlElement)XmlUtils.XPathFirstNode(template, null, "//imfStatus/originator")!;
                    var child = template.CreateElement("enterpriseName");
                    child.InnerText = _originator;
                    node.AppendChild(child);
                }

                if (_brexDmcode.Length != 0)
                {
                    SetBrex(template, _brexDmcode, stderr);
                }

                SetRemarks(template, _remarks);

                if (_issue < Issue.Iss6)
                {
                    if (_brexDmcode.Length == 0)
                    {
                        switch (_issue)
                        {
                            case Issue.Iss42:
                                SetBrex(template, Iss42DefaultBrex, stderr);
                                break;
                            case Issue.Iss50:
                                SetBrex(template, Iss50DefaultBrex, stderr);
                                break;
                        }
                    }

                    // NOTE: the C tool runs to42.xsl/to50.xsl here to downgrade the
                    // schema version. That EXSLT transform is not ported (see class
                    // remarks); the skeleton structure is otherwise compatible.
                    ToIssue(template, _issue);
                }

                string fname;
                if (@out != null)
                {
                    fname = @out;
                }
                else if (noIssue)
                {
                    fname = $"IMF-{icn}.XML";
                }
                else
                {
                    fname = $"IMF-{icn}_{_issueNumber}-{_inWork}.XML";
                }

                string fpath = Path.Combine(baseDir, fname);

                if (!overwrite && File.Exists(fpath))
                {
                    if (noOverwriteError)
                    {
                        return 0;
                    }
                    if (outdir != null)
                    {
                        stderr.WriteLine($"s1kd-{Name}: ERROR: {outdir}/{fname} already exists. Use -f to overwrite.");
                    }
                    else
                    {
                        stderr.WriteLine($"s1kd-{Name}: ERROR: {fname} already exists. Use -f to overwrite.");
                    }
                    throw new ExitException(ExitImfExists);
                }

                XmlUtils.SaveDoc(template, fpath);

                if (verbose)
                {
                    stdout.WriteLine(outdir != null ? $"{outdir}/{fname}" : fname);
                }
            }
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }

        return 0;
    }

    /* ----- helpers ----- */

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: {opt} requires an argument.");
            throw new ExitException(2);
        }
        return args[i];
    }

    private static string Truncate(string s, int maxLen) => s.Length <= maxLen ? s : s[..maxLen];

    /// <summary>
    /// Parse an ICN argument: requires the literal "ICN-" prefix, then captures
    /// up to the first '.', requiring at least one extension char after.
    /// Mirrors <c>sscanf(arg, "ICN-%255[^.].%*s", icn)</c>.
    /// </summary>
    private static string? ParseIcn(string arg)
    {
        if (!arg.StartsWith("ICN-", StringComparison.Ordinal))
        {
            return null;
        }
        string rest = arg["ICN-".Length..];
        int dot = rest.IndexOf('.');
        if (dot <= 0 || dot >= rest.Length - 1)
        {
            // need at least one ident char before '.' and one ext char after.
            return null;
        }
        return Truncate(rest[..dot], 255);
    }

    private Issue GetIssue(string iss, TextWriter stderr)
    {
        return iss switch
        {
            "6" => Issue.Iss6,
            "5.0" => Issue.Iss50,
            "4.2" => Issue.Iss42,
            _ => throw IssueError(iss, stderr),
        };
    }

    private ExitException IssueError(string iss, TextWriter stderr)
    {
        stderr.WriteLine($"s1kd-{Name}: ERROR: Unsupported issue: {iss}");
        return new ExitException(ExitBadIssue);
    }

    private XmlDocument XmlSkeleton(TextWriter stderr)
    {
        if (_templateDir != null)
        {
            string src = Path.Combine(_templateDir, "icnmetadata.xml");
            if (!File.Exists(src))
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: No schema icnmetadata in template directory \"{_templateDir}\".");
                throw new ExitException(ExitBadTemplate);
            }
            return XmlUtils.ReadDoc(src);
        }
        return EmbeddedResources.LoadXml("newimf/icnmetadata.xml");
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
            string content = EmbeddedResources.ReadText("newimf/icnmetadata.xml");
            File.WriteAllText(Path.Combine(path, "icnmetadata.xml"), content, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Cannot dump template in directory: {path}");
            throw new ExitException(ExitBadTemplDir);
        }
    }

    /// <summary>
    /// Read the .defaults file (XML or whitespace-delimited text) and copy values
    /// into any metadata field that has not yet been set. Mirrors the defaults
    /// loop in <c>main</c> plus <c>copy_default_value</c>.
    /// </summary>
    private void ReadDefaults(string defaultsFname, TextWriter stderr)
    {
        if (string.IsNullOrEmpty(defaultsFname) || !File.Exists(defaultsFname))
        {
            return;
        }

        XmlDocument? defaultsXml = null;
        try
        {
            defaultsXml = XmlUtils.ReadDoc(defaultsFname);
        }
        catch (Exception ex) when (ex is XmlException or IOException)
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
                CopyDefaultValue(el.GetAttribute("ident"), el.GetAttribute("value"), stderr);
            }
        }
        else
        {
            // Whitespace-delimited text: "<key> <value...>" per line.
            foreach (string line in File.ReadLines(defaultsFname))
            {
                int sp = -1;
                for (int j = 0; j < line.Length; j++)
                {
                    if (char.IsWhiteSpace(line[j])) { sp = j; break; }
                }
                if (sp <= 0)
                {
                    continue;
                }
                string key = line[..sp];
                string val = line[(sp + 1)..].TrimStart().TrimEnd('\r', '\n');
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
        CopyDefVal(ref _issueNumber, "issueNumber", key, val);
        CopyDefVal(ref _inWork, "inWork", key, val);
        CopyDefVal(ref _securityClassification, "securityClassification", key, val);
        CopyDefVal(ref _responsiblePartnerCompany, "responsiblePartnerCompany", key, val);
        CopyDefVal(ref _responsiblePartnerCompanyCode, "responsiblePartnerCompanyCode", key, val);
        CopyDefVal(ref _originator, "originator", key, val);
        CopyDefVal(ref _originatorCode, "originatorCode", key, val);
        CopyDefVal(ref _brexDmcode, "brex", key, val);

        if (key == "templates" && _templateDir == null)
        {
            _templateDir = val;
        }
        if (key == "remarks" && _remarks == null)
        {
            _remarks = val;
        }
        if (key == "issue" && _issue == Issue.None)
        {
            _issue = GetIssue(val, stderr);
        }
    }

    private static void CopyDefVal(ref string dst, string target, string key, string val)
    {
        if (key == target && dst.Length == 0)
        {
            dst = val;
        }
    }

    private string Prompt(string label, string current, TextWriter stdout)
    {
        if (current.Length == 0)
        {
            stdout.Write($"{label}: ");
        }
        else
        {
            stdout.Write($"{label} [{current}]: ");
        }

        string? line = Console.In.ReadLine();
        if (string.IsNullOrEmpty(line))
        {
            return current;
        }
        return line;
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
            // sscanf("%4s-%2s-%2s"): require exactly three '-'-delimited fields.
            string[] parts = _issueDate.Split('-');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
            {
                stderr.WriteLine($"s1kd-{Name}: ERROR: Bad issue date: {_issueDate}");
                throw new ExitException(ExitBadDate);
            }
            yearS = Truncate(parts[0], 4);
            monthS = Truncate(parts[1], 2);
            dayS = Truncate(parts[2], 2);
        }

        issueDate.SetAttribute("year", yearS);
        issueDate.SetAttribute("month", monthS);
        issueDate.SetAttribute("day", dayS);
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
            var sp = doc.CreateElement("simplePara");
            sp.InnerText = text;
            remarks.AppendChild(sp);
        }
        else
        {
            remarks.ParentNode?.RemoveChild(remarks);
        }
    }

    /// <summary>
    /// Parse a BREX data module code and set the attributes on the BREX dmCode
    /// element. Mirrors <c>set_brex</c> including its sscanf format and the
    /// requirement that exactly 11 or 13 fields parse.
    /// </summary>
    private void SetBrex(XmlDocument doc, string code, TextWriter stderr)
    {
        var dmCode = XmlUtils.XPathFirstNode(doc, null, "//brexDmRef/dmRef/dmRefIdent/dmCode") as XmlElement;
        if (dmCode == null)
        {
            return;
        }

        if (!TryParseBrex(code, out var f))
        {
            stderr.WriteLine($"s1kd-{Name}: ERROR: Bad BREX data module code.");
            throw new ExitException(ExitBadBrexDmc);
        }

        dmCode.SetAttribute("modelIdentCode", f.ModelIdentCode);
        dmCode.SetAttribute("systemDiffCode", f.SystemDiffCode);
        dmCode.SetAttribute("systemCode", f.SystemCode);
        dmCode.SetAttribute("subSystemCode", f.SubSystemCode);
        dmCode.SetAttribute("subSubSystemCode", f.SubSubSystemCode);
        dmCode.SetAttribute("assyCode", f.AssyCode);
        dmCode.SetAttribute("disassyCode", f.DisassyCode);
        dmCode.SetAttribute("disassyCodeVariant", f.DisassyCodeVariant);
        dmCode.SetAttribute("infoCode", f.InfoCode);
        dmCode.SetAttribute("infoCodeVariant", f.InfoCodeVariant);
        dmCode.SetAttribute("itemLocationCode", f.ItemLocationCode);

        if (f.LearnCode.Length != 0) dmCode.SetAttribute("learnCode", f.LearnCode);
        if (f.LearnEventCode.Length != 0) dmCode.SetAttribute("learnEventCode", f.LearnEventCode);
    }

    private readonly record struct BrexFields(
        string ModelIdentCode, string SystemDiffCode, string SystemCode,
        string SubSystemCode, string SubSubSystemCode, string AssyCode,
        string DisassyCode, string DisassyCodeVariant, string InfoCode,
        string InfoCodeVariant, string ItemLocationCode, string LearnCode,
        string LearnEventCode);

    /// <summary>
    /// Reproduce the C sscanf field-by-field:
    /// <c>"%14[^-]-%4[^-]-%3[^-]-%c%c-%4[^-]-%2s%3[^-]-%3s%c-%c-%3s%1s"</c>.
    /// Returns true only when exactly 11 fields (no learn data) or 13 fields
    /// (with learn data) match, mirroring the C <c>n != 11 &amp;&amp; n != 13</c>
    /// check. The trailing <c>%3s%1s</c> learn fields are optional; everything up
    /// to and including the item location code is mandatory.
    /// </summary>
    private static bool TryParseBrex(string code, out BrexFields fields)
    {
        fields = default;
        var sc = new Scanner(code);

        // The C sscanf returns the count of successfully assigned fields. We track
        // it the same way: each successful conversion increments n; the first
        // failure stops scanning.
        int n = 0;

        if (!sc.NotDash(14, out string modelIdentCode)) return Check(n); n++;
        if (!sc.Lit('-')) return Check(n);
        if (!sc.NotDash(4, out string systemDiffCode)) return Check(n); n++;
        if (!sc.Lit('-')) return Check(n);
        if (!sc.NotDash(3, out string systemCode)) return Check(n); n++;
        if (!sc.Lit('-')) return Check(n);
        if (!sc.Chars(1, out string subSystemCode)) return Check(n); n++;
        if (!sc.Chars(1, out string subSubSystemCode)) return Check(n); n++;
        if (!sc.Lit('-')) return Check(n);
        if (!sc.NotDash(4, out string assyCode)) return Check(n); n++;
        if (!sc.Lit('-')) return Check(n);
        if (!sc.Str(2, out string disassyCode)) return Check(n); n++;
        if (!sc.NotDash(3, out string disassyCodeVariant)) return Check(n); n++;
        if (!sc.Lit('-')) return Check(n);
        if (!sc.Str(3, out string infoCode)) return Check(n); n++;
        if (!sc.Chars(1, out string infoCodeVariant)) return Check(n); n++;
        if (!sc.Lit('-')) return Check(n);
        if (!sc.Chars(1, out string itemLocationCode)) return Check(n); n++; // n == 11

        string learnCode = "";
        string learnEventCode = "";

        if (sc.Lit('-') && sc.Str(3, out learnCode))
        {
            n++;
            if (sc.Str(1, out learnEventCode))
            {
                n++; // n == 13
            }
        }

        if (n != 11 && n != 13)
        {
            return false;
        }

        fields = new BrexFields(
            modelIdentCode, systemDiffCode, systemCode, subSystemCode,
            subSubSystemCode, assyCode, disassyCode, disassyCodeVariant,
            infoCode, infoCodeVariant, itemLocationCode, learnCode, learnEventCode);
        return true;
    }

    private static bool Check(int n) => n is 11 or 13;

    /// <summary>
    /// Minimal sscanf-style cursor over a string, supporting the conversions used
    /// by the BREX format: bounded "not '-'" character classes (<c>%N[^-]</c>),
    /// bounded plain strings (<c>%Ns</c>, which here also stop at '-' since the
    /// codes contain no whitespace and are always '-'-delimited), single
    /// characters (<c>%c</c>) and literal characters.
    /// </summary>
    private sealed class Scanner(string input)
    {
        private readonly string _s = input;
        private int _pos;

        public bool Lit(char c)
        {
            if (_pos < _s.Length && _s[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        public bool Chars(int count, out string value)
        {
            if (_pos + count <= _s.Length)
            {
                value = _s.Substring(_pos, count);
                _pos += count;
                return true;
            }
            value = "";
            return false;
        }

        /// <summary>%N[^-]: consume up to N chars, stopping at '-'. Needs >=1 char.</summary>
        public bool NotDash(int max, out string value)
        {
            int start = _pos;
            while (_pos < _s.Length && _pos - start < max && _s[_pos] != '-')
            {
                _pos++;
            }
            value = _s[start.._pos];
            return value.Length > 0;
        }

        /// <summary>%Ns: consume up to N chars, stopping at '-'. Needs >=1 char.</summary>
        public bool Str(int max, out string value) => NotDash(max, out value);
    }

    /// <summary>
    /// Adjust the document for issue 4.2/5.0. The full schema downgrade is done in
    /// the C via EXSLT stylesheets that are not ported; here we only update the
    /// schema location reference so the produced file at least targets the right
    /// issue directory. See class remarks.
    /// </summary>
    private static void ToIssue(XmlDocument doc, Issue iss)
    {
        if (doc.DocumentElement == null)
        {
            return;
        }
        string? issueDir = iss switch
        {
            Issue.Iss42 => "S1000D_4-2",
            Issue.Iss50 => "S1000D_5-0",
            _ => null,
        };
        if (issueDir == null)
        {
            return;
        }

        const string xsiNs = "http://www.w3.org/2001/XMLSchema-instance";
        string? loc = doc.DocumentElement.GetAttribute("noNamespaceSchemaLocation", xsiNs);
        if (!string.IsNullOrEmpty(loc))
        {
            string updated = loc.Replace("S1000D_6", issueDir, StringComparison.Ordinal);
            doc.DocumentElement.SetAttribute("noNamespaceSchemaLocation", xsiNs, updated);
        }
    }

    /* ----- help / version ----- */

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] <icns>...");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -@, --out <path>            Output to specified file or directory.");
        stdout.WriteLine("  -%, --templates <dir>       Use template in specified directory.");
        stdout.WriteLine("  -~, --dump-templates <dir>  Dump built-in template to directory.");
        stdout.WriteLine("  -d, --defaults <file>       Specify .defaults file path.");
        stdout.WriteLine("  -f, --overwrite             Overwrite existing file.");
        stdout.WriteLine("  -N, --omit-issue            Omit issue/inwork numbers from filename.");
        stdout.WriteLine("  -p, --prompt                Show prompts.");
        stdout.WriteLine("  -q, --quiet                 Don't report an error if file exists.");
        stdout.WriteLine("  -v, --verbose               Print file name of IMF.");
        stdout.WriteLine("  --version                   Show version information.");
        stdout.WriteLine("  <icns>                      1 or more ICNs to generate a metadata file for.");
        stdout.WriteLine();
        stdout.WriteLine("In addition, the following metadata can be set:");
        stdout.WriteLine("  -b, --brex <BREX>           BREX data module code");
        stdout.WriteLine("  -c, --security <sec>        Security classification");
        stdout.WriteLine("  -I, --date <date>           Issue date");
        stdout.WriteLine("  -m, --remarks <remarks>     Remarks");
        stdout.WriteLine("  -n, --issno <iss>           Issue number");
        stdout.WriteLine("  -O, --origcode <CAGE>       Originator CAGE code");
        stdout.WriteLine("  -o, --origname <orig>       Originator");
        stdout.WriteLine("  -R, --rpccode <CAGE>        Responsible partner company CAGE code");
        stdout.WriteLine("  -r, --rpcname <RPC>         Responsible partner company");
        stdout.WriteLine("  -t, --title <title>         ICN title");
        stdout.WriteLine("  -w, --inwork <inwork>       Inwork issue");
    }
}
