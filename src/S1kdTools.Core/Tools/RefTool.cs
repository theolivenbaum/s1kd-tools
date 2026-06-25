using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-ref</c>: generate the XML for S1000D reference elements from
/// a code or filename, optionally inserting the reference into a source data
/// module, transforming textual references in CSDB objects (<c>-T</c>), or
/// resolving objects in a list (<c>-L</c>).
///
/// The C tool is the spec (reference/tools/s1kd-ref/s1kd-ref.c, VERSION 3.8.1).
/// The downgrade stylesheets (toNN.xsl) and ref.xsl are pure XSLT 1.0, so they
/// run under <see cref="XslCompiledTransform"/> with no EXSLT shim required.
/// </summary>
public sealed class RefTool : ITool
{
    public string Name => "ref";
    public string Description => "Generate XML to reference CSDB objects.";
    public string Version => "3.8.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitMissingFile = 1;
    private const int ExitBadInput = 2;
    private const int ExitBadIssue = 3;
    private const int ExitBadXpath = 4;

    /* Option bit flags (mirror the OPT_* defines). */
    [Flags]
    private enum Opt
    {
        None = 0,
        Title = 0x001,
        Issue = 0x002,
        Lang = 0x004,
        Date = 0x008,
        SrcId = 0x010,
        CirId = 0x020,
        Ins = 0x040,
        Url = 0x080,
        Content = 0x100,
        NonStrict = 0x200,
    }

    private enum Verbosity { Quiet, Normal, Verbose, Debug }

    private enum Issue { Iss20, Iss21, Iss22, Iss23, Iss30, Iss40, Iss41, Iss42, Iss50 }

    private const Issue DefaultIssue = Issue.Iss50;

    /* Thrown to mirror the C tool's exit() calls without aborting the process. */
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    /* ----- regular expressions (mirror the *_REGEX defines) ----- */

    private const string IssNo = "(_[0-9]{3}-[0-9]{2})?";
    private const string Lang = "(_[A-Z]{2}-[A-Z]{2})?";

    private const string DmeRegex = "(DME-)?[0-9A-Z]+-[0-9A-Z]+-[0-9A-Z]{2,14}-[0-9A-Z]{1,4}-[0-9A-Z]{2,3}-[0-9A-Z]{2}-[0-9A-Z]{2,4}-[0-9A-Z]{3,5}-[0-9A-Z]{4}-[ABCDT](-[0-9A-Z]{4})?" + IssNo + Lang;
    private const string DmcRegex = "(DMC-)?[0-9A-Z]{2,14}-[0-9A-Z]{1,4}-[0-9A-Z]{2,3}-[0-9A-Z]{2}-[0-9A-Z]{2,4}-[0-9A-Z]{3,5}-[0-9A-Z]{4}-[ABCDT](-[0-9A-Z]{4})?" + IssNo + Lang;
    private const string CsnRegex = "(CSN-)?[0-9A-Z]{2,14}-[0-9A-Z]{1,4}-[0-9A-Z]{2,3}-[0-9A-Z]{2}-[0-9A-Z]{2,4}-[0-9A-Z]{3,5}-[0-9A-Z]{4}-[ABCDT]";
    private const string PmeRegex = "(PME-)?[0-9A-Z]+-[0-9A-Z]+-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang;
    private const string PmcRegex = "(PMC-)?[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang;
    private const string SmeRegex = "(SME-)?[0-9A-Z]+-[0-9A-Z]+-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang;
    private const string SmcRegex = "(SMC-)?[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang;
    private const string ComRegex = "(COM-)?[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9]{4}-[0-9]{5}-[QIR]" + Lang;
    private const string DmlRegex = "(DML-)?[0-9A-Z]{2,14}-[0-9A-Z]{5}-[CPS]-[0-9]{4}-[0-9]{5}" + IssNo;

    private const string DmeRegexStrict = "DME-[0-9A-Z]+-[0-9A-Z]+-[0-9A-Z]{2,14}-[0-9A-Z]{1,4}-[0-9A-Z]{2,3}-[0-9A-Z]{2}-[0-9A-Z]{2,4}-[0-9A-Z]{3,5}-[0-9A-Z]{4}-[ABCDT](-[0-9A-Z]{4})?" + IssNo + Lang;
    private const string DmcRegexStrict = "DMC-[0-9A-Z]{2,14}-[0-9A-Z]{1,4}-[0-9A-Z]{2,3}-[0-9A-Z]{2}-[0-9A-Z]{2,4}-[0-9A-Z]{3,5}-[0-9A-Z]{4}-[ABCDT](-[0-9A-Z]{4})?" + IssNo + Lang;
    private const string CsnRegexStrict = "CSN-[0-9A-Z]{2,14}-[0-9A-Z]{1,4}-[0-9A-Z]{2,3}-[0-9A-Z]{2}-[0-9A-Z]{2,4}-[0-9A-Z]{3,5}-[0-9A-Z]{4}-[ABCDT]";
    private const string PmeRegexStrict = "PME-[0-9A-Z]+-[0-9A-Z]+-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang;
    private const string PmcRegexStrict = "PMC-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang;
    private const string SmeRegexStrict = "SME-[0-9A-Z]+-[0-9A-Z]+-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang;
    private const string SmcRegexStrict = "SMC-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang;
    private const string ComRegexStrict = "COM-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9]{4}-[0-9]{5}-[QIR]" + Lang;
    private const string DmlRegexStrict = "DML-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[CPS]-[0-9]{4}-[0-9]{5}" + IssNo;
    private const string IcnRegex = "(ICN-[A-Z0-9]{5}-[A-Z0-9]{5,10}-[0-9]{3}-[0-9]{2})|(ICN-[A-Z0-9]{2,14}-[A-Z0-9]{1,4}-[A-Z0-9]{6,9}-[A-Z0-9]{1}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z]{1}-[0-9]{2,3}-[0-9]{1,2})";

    private const string DmeRegexNopre = "^[0-9A-Z]+-[0-9A-Z]+-[0-9A-Z]{2,14}-[0-9A-Z]{1,4}-[0-9A-Z]{2,3}-[0-9A-Z]{2}-[0-9A-Z]{2,4}-[0-9A-Z]{3,5}-[0-9A-Z]{4}-[ABCDT](-[0-9A-Z]{4})?" + IssNo + Lang + "$";
    private const string DmcRegexNopre = "^[0-9A-Z]{2,14}-[0-9A-Z]{1,4}-[0-9A-Z]{2,3}-[0-9A-Z]{2}-[0-9A-Z]{2,4}-[0-9A-Z]{3,5}-[0-9A-Z]{4}-[ABCDT](-[0-9A-Z]{4})?" + IssNo + Lang + "$";
    private const string PmeRegexNopre = "^[0-9A-Z]+-[0-9A-Z]+-[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang + "$";
    private const string PmcRegexNopre = "^[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9]{2}" + IssNo + Lang + "$";
    private const string ComRegexNopre = "^[0-9A-Z]{2,14}-[0-9A-Z]{5}-[0-9]{4}-[0-9]{5}-[QIR]" + Lang + "$";
    private const string DmlRegexNopre = "^[0-9A-Z]{2,14}-[0-9A-Z]{5}-[CPS]-[0-9]{4}-[0-9]{5}" + IssNo + "$";

    private Verbosity _verbosity = Verbosity.Normal;
    private TextWriter _stderr = TextWriter.Null;

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        _stderr = stderr;
        _verbosity = Verbosity.Normal;

        var opts = Opt.None;
        string src = "-";
        string dst = "-";
        bool overwrite = false;
        var iss = DefaultIssue;
        string extpubsFname = "";
        string? transform = null;
        string? transformXpath = null;
        bool isList = false;
        var files = new List<string>();

        try
        {
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--version":
                        stdout.WriteLine($"{Name} ({Version})");
                        return 0;
                    case "-h" or "-?" or "--help":
                        ShowHelp(stdout);
                        return 0;
                    case "-3" or "--externalpubs":
                        extpubsFname = NextArg(args, ref i, a);
                        break;
                    case "-c" or "--content":
                        opts |= Opt.Content;
                        break;
                    case "-f" or "--overwrite":
                        overwrite = true;
                        break;
                    case "-g" or "--guess-prefix":
                        opts |= Opt.NonStrict;
                        break;
                    case "-i" or "--include-issue":
                        opts |= Opt.Issue;
                        break;
                    case "-L" or "--list":
                        isList = true;
                        break;
                    case "-l" or "--include-lang":
                        opts |= Opt.Lang;
                        break;
                    case "-o" or "--out":
                        dst = NextArg(args, ref i, a);
                        break;
                    case "-q" or "--quiet":
                        if (_verbosity > Verbosity.Quiet) _verbosity--;
                        break;
                    case "-r" or "--add":
                        opts |= Opt.Ins;
                        break;
                    case "-R" or "--repository-id":
                        // C falls through R -> S, so -R also sets the source-id flags.
                        opts |= Opt.CirId;
                        opts |= Opt.SrcId | Opt.Issue | Opt.Lang;
                        break;
                    case "-S" or "--source-id":
                        opts |= Opt.SrcId | Opt.Issue | Opt.Lang;
                        break;
                    case "-s" or "--source":
                        src = NextArg(args, ref i, a);
                        break;
                    case "-T" or "--transform":
                        {
                            string t = NextArg(args, ref i, a);
                            transform = t == "all" ? "CDEGLPSY" : t;
                            break;
                        }
                    case "-t" or "--include-title":
                        opts |= Opt.Title;
                        break;
                    case "-v" or "--verbose":
                        if (_verbosity < Verbosity.Debug) _verbosity++;
                        break;
                    case "-d" or "--include-date":
                        opts |= Opt.Date;
                        break;
                    case "-$" or "--issue":
                        iss = SpecIssue(NextArg(args, ref i, a));
                        break;
                    case "-u" or "--include-url":
                        opts |= Opt.Url;
                        break;
                    case "-x" or "--xpath":
                        transformXpath = NextArg(args, ref i, a);
                        break;
                    default:
                        if (a.Length > 1 && a[0] == '-' && a != "-")
                        {
                            // Support bundled short options (e.g. -il, -dilt).
                            if (a[1] != '-' && TryParseBundled(a, ref opts, ref overwrite, ref isList))
                            {
                                break;
                            }
                            Error($"Unknown option: {a}");
                            return ExitBadInput;
                        }
                        files.Add(a);
                        break;
                }
            }

            XmlDocument? extpubs = null;
            if (extpubsFname != "")
            {
                extpubs = ReadXmlDoc(extpubsFname);
            }
            else if (Csdb.FindConfig(Csdb.ExternalPubsFileName, out string cfg))
            {
                extpubs = ReadXmlDoc(cfg);
            }

            if (files.Count > 0)
            {
                foreach (string file in files)
                {
                    if (transform != null)
                    {
                        if (isList)
                        {
                            TransformRefsInList(file, transform, transformXpath, extpubs, overwrite, opts, stdout);
                        }
                        else
                        {
                            TransformRefsInFile(file, transform, transformXpath, extpubs, overwrite, opts, stdout);
                        }
                    }
                    else
                    {
                        string baseName = file.StartsWith("URN:S1000D:", StringComparison.Ordinal)
                            ? file["URN:S1000D:".Length..]
                            : Path.GetFileName(file);
                        PrintRef(src, dst, baseName, file, opts, overwrite, iss, extpubs, stdout);
                    }
                }
            }
            else if (transform != null)
            {
                if (isList)
                {
                    TransformRefsInList(null, transform, transformXpath, extpubs, overwrite, opts, stdout);
                }
                else
                {
                    TransformRefsInFile("-", transform, transformXpath, extpubs, overwrite, opts, stdout);
                }
            }
            else
            {
                string? line;
                using var stdin = new StreamReader(Console.OpenStandardInput());
                while ((line = stdin.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    PrintRef(src, dst, trimmed, null, opts, overwrite, iss, extpubs, stdout);
                }
            }
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }

        return 0;
    }

    private static string NextArg(IReadOnlyList<string> args, ref int i, string opt)
    {
        if (++i >= args.Count)
        {
            throw new ExitException(ExitBadInput);
        }
        return args[i];
    }

    /// <summary>Handle bundled no-argument short flags (e.g. -ilt). Returns false if any char is unknown.</summary>
    private static bool TryParseBundled(string a, ref Opt opts, ref bool overwrite, ref bool isList)
    {
        var local = opts;
        bool ow = overwrite, list = isList;
        for (int k = 1; k < a.Length; k++)
        {
            switch (a[k])
            {
                case 'c': local |= Opt.Content; break;
                case 'f': ow = true; break;
                case 'g': local |= Opt.NonStrict; break;
                case 'i': local |= Opt.Issue; break;
                case 'L': list = true; break;
                case 'l': local |= Opt.Lang; break;
                case 'r': local |= Opt.Ins; break;
                case 'R': local |= Opt.CirId | Opt.SrcId | Opt.Issue | Opt.Lang; break;
                case 'S': local |= Opt.SrcId | Opt.Issue | Opt.Lang; break;
                case 't': local |= Opt.Title; break;
                case 'd': local |= Opt.Date; break;
                case 'u': local |= Opt.Url; break;
                default: return false;
            }
        }
        opts = local;
        overwrite = ow;
        isList = list;
        return true;
    }

    private static bool IsSet(Opt opts, Opt flag) => (opts & flag) != 0;

    private void Error(string message)
    {
        if (_verbosity > Verbosity.Quiet)
        {
            _stderr.WriteLine($"{Name}: ERROR: {message}");
        }
    }

    private void Warning(string message)
    {
        if (_verbosity > Verbosity.Quiet)
        {
            _stderr.WriteLine($"{Name}: WARNING: {message}");
        }
    }

    private void Info(string message, Verbosity level)
    {
        if (_verbosity >= level)
        {
            _stderr.WriteLine($"{Name}: INFO: {message}");
        }
    }

    private Issue SpecIssue(string s)
    {
        return s switch
        {
            "2.0" => Issue.Iss20,
            "2.1" => Issue.Iss21,
            "2.2" => Issue.Iss22,
            "2.3" => Issue.Iss23,
            "3.0" => Issue.Iss30,
            "4.0" => Issue.Iss40,
            "4.1" => Issue.Iss41,
            "4.2" => Issue.Iss42,
            "5.0" => Issue.Iss50,
            _ => ThrowBadIssue(s),
        };
    }

    private Issue ThrowBadIssue(string s)
    {
        Error($"Unsupported issue: {s}");
        throw new ExitException(ExitBadIssue);
    }

    private static XmlDocument? ReadXmlDoc(string path)
    {
        try
        {
            if (path == "-")
            {
                return XmlUtils.ReadStream(Console.OpenStandardInput());
            }
            return XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /* ----- type classification ----- */

    private static bool IsSmcRef(string r) => r.StartsWith("SMC-", StringComparison.Ordinal) || r.StartsWith("SME-", StringComparison.Ordinal);
    private static bool IsPmRef(string r) => r.StartsWith("PMC-", StringComparison.Ordinal) || r.StartsWith("PME-", StringComparison.Ordinal);
    private static bool IsDmRef(string r) => r.StartsWith("DMC-", StringComparison.Ordinal) || r.StartsWith("DME-", StringComparison.Ordinal);
    private static bool IsComRef(string r) => r.StartsWith("COM-", StringComparison.Ordinal);
    private static bool IsDmlRef(string r) => r.StartsWith("DML-", StringComparison.Ordinal);
    private static bool IsIcnRef(string r) => r.StartsWith("ICN-", StringComparison.Ordinal);
    private static bool IsCsnRef(string r) => r.StartsWith("CSN-", StringComparison.Ordinal);

    /* ----- small DOM helpers ----- */

    private static XmlNode? FindChild(XmlNode? parent, string name)
    {
        if (parent == null) return null;
        for (XmlNode? cur = parent.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.LocalName == name || cur.Name == name) return cur;
        }
        return null;
    }

    private static XmlElement NewElement(XmlDocument doc, string name) => doc.CreateElement(name);

    private static void SetXlink(XmlElement node, string href)
    {
        XmlAttribute attr = node.OwnerDocument.CreateAttribute("xlink", "href", "http://www.w3.org/1999/xlink");
        attr.Value = href;
        node.SetAttributeNode(attr);
    }

    private XmlElement? NewIssueInfo(XmlDocument doc, string s)
    {
        // s starts with '_': "_NNN-WW".
        var m = Regex.Match(s, @"^_([^-]{1,3})-([^_]{2})");
        if (!m.Success) return null;
        var e = NewElement(doc, "issueInfo");
        e.SetAttribute("issueNumber", m.Groups[1].Value);
        e.SetAttribute("inWork", m.Groups[2].Value);
        return e;
    }

    private XmlElement? NewLanguage(XmlDocument doc, string s)
    {
        var m = Regex.Match(s, @"^_([^-]{1,3})-([^_]{2})");
        if (!m.Success) return null;
        var e = NewElement(doc, "language");
        e.SetAttribute("languageIsoCode", m.Groups[1].Value.ToLowerInvariant());
        e.SetAttribute("countryIsoCode", m.Groups[2].Value);
        return e;
    }

    /* ----- reference builders ----- */

    private delegate XmlNode? NewRef(XmlDocument doc, string reff, string? fname, Opt opts);

    private XmlNode NewDmRef(XmlDocument doc, string reff, string? fname, Opt opts)
    {
        bool isExtended = reff.StartsWith("DME-", StringComparison.Ordinal);
        string[] parts = SplitCode(reff);

        string extProducer = "", extCode = "";
        int idx;
        if (isExtended)
        {
            // DME-<producer>-<code>-<mic>-... ; base fields span idx..idx+7 (8 fields).
            if (parts.Length < 11) { BadInput($"Data module extended code invalid: {reff}"); }
            extProducer = parts[1];
            extCode = parts[2];
            idx = 3;
        }
        else
        {
            if (parts.Length < 9) { BadInput($"Data module code invalid: {reff}"); }
            idx = 1;
        }

        // From idx: mic, sysDiff, sysCode, subSys+subSubSys, assy, disassy+variant, info+variant, itemLoc, [learn+event]
        string modelIdentCode = parts[idx + 0];
        string systemDiffCode = parts[idx + 1];
        string systemCode = parts[idx + 2];
        string subSysCombined = parts[idx + 3];     // e.g. "00" -> subSystem + subSubSystem
        string assyCode = parts[idx + 4];
        string disassyCombined = parts[idx + 5];    // disassyCode(2) + variant
        string infoCombined = parts[idx + 6];       // infoCode(3) + variant
        string itemLocationCode = parts[idx + 7];
        bool hasLearn = parts.Length > idx + 9;
        string learnCode = hasLearn ? parts[idx + 8] : "";
        string learnEventCode = hasLearn ? parts[idx + 9] : "";

        string subSystemCode = subSysCombined.Length > 0 ? subSysCombined[..1] : "";
        string subSubSystemCode = subSysCombined.Length > 1 ? subSysCombined[1..] : "";
        string disassyCode = disassyCombined.Length >= 2 ? disassyCombined[..2] : disassyCombined;
        string disassyCodeVariant = disassyCombined.Length > 2 ? disassyCombined[2..] : "";
        string infoCode = infoCombined.Length >= 3 ? infoCombined[..3] : infoCombined;
        string infoCodeVariant = infoCombined.Length > 3 ? infoCombined[3..] : "";

        var dmRef = NewElement(doc, "dmRef");
        var dmRefIdent = (XmlElement)dmRef.AppendChild(NewElement(doc, "dmRefIdent"))!;

        if (isExtended)
        {
            var identExtension = (XmlElement)dmRefIdent.AppendChild(NewElement(doc, "identExtension"))!;
            identExtension.SetAttribute("extensionProducer", extProducer);
            identExtension.SetAttribute("extensionCode", extCode);
        }

        var dmCode = (XmlElement)dmRefIdent.AppendChild(NewElement(doc, "dmCode"))!;
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
        if (hasLearn)
        {
            dmCode.SetAttribute("learnCode", learnCode);
            dmCode.SetAttribute("learnEventCode", learnEventCode);
        }

        XmlElement? issueInfo = null;
        XmlElement? language = null;

        if (opts != Opt.None)
        {
            XmlDocument? refDoc = fname != null ? ReadXmlDoc(fname) : null;
            XmlNode? refDmIdent = null, refDmTitle = null, refDmIssueDate = null;

            if (refDoc != null)
            {
                XmlNode? addr = XPathFirst(refDoc, "//dmAddress|//dmaddres");
                if (addr != null && addr.LocalName == "dmaddres")
                {
                    refDmIdent = addr;
                    refDmTitle = FindChild(addr, "dmtitle");
                    refDmIssueDate = FindChild(addr, "issdate");
                }
                else if (addr != null)
                {
                    refDmIdent = FindChild(addr, "dmIdent");
                    XmlNode? items = FindChild(addr, "dmAddressItems");
                    refDmTitle = FindChild(items, "dmTitle");
                    refDmIssueDate = FindChild(items, "issueDate");
                }
            }

            int us = reff.IndexOf('_');
            string? s = us >= 0 ? reff[us..] : null;

            if (IsSet(opts, Opt.Issue))
            {
                issueInfo = BuildIssueInfo(doc, refDoc, refDmIdent, s, "data module", reff);
                if (issueInfo != null) dmRefIdent.AppendChild(issueInfo);
            }

            if (IsSet(opts, Opt.Lang))
            {
                language = BuildLanguage(doc, refDoc, refDmIdent, s, "data module", reff);
                if (language != null) dmRefIdent.AppendChild(language);
            }

            if (IsSet(opts, Opt.Title) || IsSet(opts, Opt.Date))
            {
                XmlElement? addrItems = null;
                if (refDoc != null)
                {
                    addrItems = (XmlElement)dmRef.AppendChild(NewElement(doc, "dmRefAddressItems"))!;
                }

                if (IsSet(opts, Opt.Title))
                {
                    XmlElement? dmTitle = null;
                    if (refDoc != null && refDmTitle != null)
                    {
                        string? tech = XPathFirstValue(refDmTitle, "techName|techname");
                        string? info = XPathFirstValue(refDmTitle, "infoName|infoname");
                        string? infv = XPathFirstValue(refDmTitle, "infoNameVariant");

                        dmTitle = NewElement(doc, "dmTitle");
                        AppendTextChild(dmTitle, "techName", tech ?? "");
                        if (info != null) AppendTextChild(dmTitle, "infoName", info);
                        if (infv != null) AppendTextChild(dmTitle, "infoNameVariant", infv);
                    }

                    if (dmTitle != null) addrItems!.AppendChild(dmTitle);
                    else Warning($"Could not read title from data module: {reff}");
                }

                if (IsSet(opts, Opt.Date))
                {
                    XmlElement? issueDate = null;
                    if (refDoc != null && refDmIssueDate != null)
                    {
                        issueDate = (XmlElement)doc.ImportNode(refDmIssueDate, true);
                        issueDate = RenameElement(issueDate, "issueDate");
                    }
                    if (issueDate != null) addrItems!.AppendChild(issueDate);
                    else Warning($"Could not read issue date from data module: {reff}");
                }
            }

            if (IsSet(opts, Opt.SrcId))
            {
                var srcName = IsSet(opts, Opt.CirId) ? "repositorySourceDmIdent" : "sourceDmIdent";
                var srcNode = NewElement(doc, srcName);
                srcNode.AppendChild(dmCode.CloneNode(true));
                if (language != null) srcNode.AppendChild(language.CloneNode(true));
                if (issueInfo != null) srcNode.AppendChild(issueInfo.CloneNode(true));
                if (IsSet(opts, Opt.Url)) SetXlink(srcNode, fname ?? "");
                return srcNode;
            }

            if (IsSet(opts, Opt.Url)) SetXlink(dmRef, fname ?? "");
        }

        return dmRef;
    }

    private XmlNode NewPmRef(XmlDocument doc, string reff, string? fname, Opt opts)
    {
        bool isExtended = reff.StartsWith("PME-", StringComparison.Ordinal);
        string[] parts = SplitCode(reff);

        string extProducer = "", extCode = "";
        int idx;
        if (isExtended)
        {
            if (parts.Length < 7) { BadInput($"Publication module extended code invalid: {reff}"); }
            extProducer = parts[1];
            extCode = parts[2];
            idx = 3;
        }
        else
        {
            if (parts.Length < 5) { BadInput($"Publication module code invalid: {reff}"); }
            idx = 1;
        }

        string modelIdentCode = parts[idx + 0];
        string pmIssuer = parts[idx + 1];
        string pmNumber = parts[idx + 2];
        string pmVolume = parts[idx + 3].Length >= 2 ? parts[idx + 3][..2] : parts[idx + 3];

        var pmRef = NewElement(doc, "pmRef");
        var pmRefIdent = (XmlElement)pmRef.AppendChild(NewElement(doc, "pmRefIdent"))!;

        if (isExtended)
        {
            var identExtension = (XmlElement)pmRefIdent.AppendChild(NewElement(doc, "identExtension"))!;
            identExtension.SetAttribute("extensionProducer", extProducer);
            identExtension.SetAttribute("extensionCode", extCode);
        }

        var pmCode = (XmlElement)pmRefIdent.AppendChild(NewElement(doc, "pmCode"))!;
        pmCode.SetAttribute("modelIdentCode", modelIdentCode);
        pmCode.SetAttribute("pmIssuer", pmIssuer);
        pmCode.SetAttribute("pmNumber", pmNumber);
        pmCode.SetAttribute("pmVolume", pmVolume);

        XmlElement? issueInfo = null;
        XmlElement? language = null;

        if (opts != Opt.None)
        {
            XmlDocument? refDoc = fname != null ? ReadXmlDoc(fname) : null;
            XmlNode? refPmIdent = null, refPmTitle = null, refPmIssueDate = null;

            if (refDoc != null)
            {
                XmlNode? addr = XPathFirst(refDoc, "//pmAddress|//pmaddres");
                if (addr != null && addr.LocalName == "pmaddres")
                {
                    refPmIdent = addr;
                    refPmTitle = FindChild(addr, "pmtitle");
                    refPmIssueDate = FindChild(addr, "issdate");
                }
                else if (addr != null)
                {
                    refPmIdent = FindChild(addr, "pmIdent");
                    XmlNode? items = FindChild(addr, "pmAddressItems");
                    refPmTitle = FindChild(items, "pmTitle");
                    refPmIssueDate = FindChild(items, "issueDate");
                }
            }

            int us = reff.IndexOf('_');
            string? s = us >= 0 ? reff[us..] : null;

            if (IsSet(opts, Opt.Issue))
            {
                issueInfo = BuildIssueInfo(doc, refDoc, refPmIdent, s, "publication module", reff);
                if (issueInfo != null) pmRefIdent.AppendChild(issueInfo);
            }

            if (IsSet(opts, Opt.Lang))
            {
                language = BuildLanguage(doc, refDoc, refPmIdent, s, "publication module", reff);
                if (language != null) pmRefIdent.AppendChild(language);
            }

            if (IsSet(opts, Opt.Title) || IsSet(opts, Opt.Date))
            {
                XmlElement? addrItems = null;
                if (refDoc != null)
                {
                    addrItems = (XmlElement)pmRef.AppendChild(NewElement(doc, "pmRefAddressItems"))!;
                }

                if (IsSet(opts, Opt.Title))
                {
                    if (refDoc != null && refPmTitle != null)
                    {
                        var t = RenameElement((XmlElement)doc.ImportNode(refPmTitle, true), "pmTitle");
                        addrItems!.AppendChild(t);
                    }
                    else Warning($"Could not read title from publication module: {reff}");
                }
                if (IsSet(opts, Opt.Date))
                {
                    if (refDoc != null && refPmIssueDate != null)
                    {
                        var d = RenameElement((XmlElement)doc.ImportNode(refPmIssueDate, true), "issueDate");
                        addrItems!.AppendChild(d);
                    }
                    else Warning($"Could not read date from publication module: {reff}");
                }
            }

            if (IsSet(opts, Opt.SrcId))
            {
                var srcNode = NewElement(doc, "sourcePmIdent");
                srcNode.AppendChild(pmCode.CloneNode(true));
                if (language != null) srcNode.AppendChild(language.CloneNode(true));
                if (issueInfo != null) srcNode.AppendChild(issueInfo.CloneNode(true));
                if (IsSet(opts, Opt.Url)) SetXlink(srcNode, fname ?? "");
                return srcNode;
            }

            if (IsSet(opts, Opt.Url)) SetXlink(pmRef, fname ?? "");
        }

        return pmRef;
    }

    private XmlNode NewSmcRef(XmlDocument doc, string reff, string? fname, Opt opts)
    {
        bool isExtended = reff.StartsWith("SME-", StringComparison.Ordinal);
        string[] parts = SplitCode(reff);

        string extProducer = "", extCode = "";
        int idx;
        if (isExtended)
        {
            if (parts.Length < 7) { BadInput($"SCORM content package extended code invalid: {reff}"); }
            extProducer = parts[1];
            extCode = parts[2];
            idx = 3;
        }
        else
        {
            if (parts.Length < 5) { BadInput($"SCORM content package code invalid: {reff}"); }
            idx = 1;
        }

        string modelIdentCode = parts[idx + 0];
        string smcIssuer = parts[idx + 1];
        string smcNumber = parts[idx + 2];
        string smcVolume = parts[idx + 3].Length >= 2 ? parts[idx + 3][..2] : parts[idx + 3];

        var smcRef = NewElement(doc, "scormContentPackageRef");
        var smcRefIdent = (XmlElement)smcRef.AppendChild(NewElement(doc, "scormContentPackageRefIdent"))!;

        if (isExtended)
        {
            var identExtension = (XmlElement)smcRefIdent.AppendChild(NewElement(doc, "identExtension"))!;
            identExtension.SetAttribute("extensionProducer", extProducer);
            identExtension.SetAttribute("extensionCode", extCode);
        }

        var smcCode = (XmlElement)smcRefIdent.AppendChild(NewElement(doc, "scormContentPackageCode"))!;
        smcCode.SetAttribute("modelIdentCode", modelIdentCode);
        smcCode.SetAttribute("scormContentPackageIssuer", smcIssuer);
        smcCode.SetAttribute("scormContentPackageNumber", smcNumber);
        smcCode.SetAttribute("scormContentPackageVolume", smcVolume);

        XmlElement? issueInfo = null;
        XmlElement? language = null;

        if (opts != Opt.None)
        {
            XmlDocument? refDoc = fname != null ? ReadXmlDoc(fname) : null;
            XmlNode? refSmcIdent = null, refSmcTitle = null, refSmcIssueDate = null;

            if (refDoc != null)
            {
                XmlNode? addr = XPathFirst(refDoc, "//scormContentPackageAddress");
                refSmcIdent = FindChild(addr, "scormContentPackageIdent");
                XmlNode? items = FindChild(addr, "scormContentPackageAddressItems");
                refSmcTitle = FindChild(items, "scormContentPackageTitle");
                refSmcIssueDate = FindChild(items, "issueDate");
            }

            int us = reff.IndexOf('_');
            string? s = us >= 0 ? reff[us..] : null;

            if (IsSet(opts, Opt.Issue))
            {
                if (refDoc != null)
                {
                    XmlNode? ii = FindChild(refSmcIdent, "issueInfo");
                    issueInfo = ii != null ? (XmlElement)doc.ImportNode(ii, true) : null;
                }
                else if (s != null && s.Length > 1 && char.IsDigit(s[1]))
                {
                    issueInfo = NewIssueInfo(doc, s);
                }
                else
                {
                    Warning($"Could not read issue info from SCORM content package: {reff}");
                }
                if (issueInfo != null) smcRefIdent.AppendChild(issueInfo);
            }

            if (IsSet(opts, Opt.Lang))
            {
                if (refDoc != null)
                {
                    XmlNode? l = FindChild(refSmcIdent, "language");
                    language = l != null ? (XmlElement)doc.ImportNode(l, true) : null;
                }
                else
                {
                    string? s2 = SecondUnderscore(reff);
                    language = s2 != null ? NewLanguage(doc, s2) : null;
                    if (language == null) Warning($"Could not read language from SCORM content package: {reff}");
                }
                if (language != null) smcRefIdent.AppendChild(language);
            }

            if (IsSet(opts, Opt.Title) || IsSet(opts, Opt.Date))
            {
                XmlElement? addrItems = null;
                if (refDoc != null)
                {
                    addrItems = (XmlElement)smcRef.AppendChild(NewElement(doc, "scormContentPackageRefAddressItems"))!;
                }
                if (IsSet(opts, Opt.Title))
                {
                    if (refDoc != null && refSmcTitle != null) addrItems!.AppendChild(doc.ImportNode(refSmcTitle, true));
                    else Warning($"Could not read title from SCORM content package: {reff}");
                }
                if (IsSet(opts, Opt.Date))
                {
                    if (refDoc != null && refSmcIssueDate != null) addrItems!.AppendChild(doc.ImportNode(refSmcIssueDate, true));
                    else Warning($"Could not read date from SCORM content package: {reff}");
                }
            }

            if (IsSet(opts, Opt.SrcId))
            {
                var srcNode = NewElement(doc, "sourceScormContentPackageIdent");
                srcNode.AppendChild(smcCode.CloneNode(true));
                if (language != null) srcNode.AppendChild(language.CloneNode(true));
                if (issueInfo != null) srcNode.AppendChild(issueInfo.CloneNode(true));
                if (IsSet(opts, Opt.Url)) SetXlink(srcNode, fname ?? "");
                return srcNode;
            }

            if (IsSet(opts, Opt.Url)) SetXlink(smcRef, fname ?? "");
        }

        return smcRef;
    }

    private XmlNode NewComRef(XmlDocument doc, string reff, string? fname, Opt opts)
    {
        string[] parts = SplitCode(reff);
        if (parts.Length < 6) { BadInput($"Comment code invalid: {reff}"); }

        string modelIdentCode = parts[1];
        string senderIdent = parts[2];
        string yearOfDataIssue = parts[3];
        string seqNumber = parts[4];
        string commentType = parts[5].Length >= 1 ? parts[5][..1].ToLowerInvariant() : "";

        var commentRef = NewElement(doc, "commentRef");
        var commentRefIdent = (XmlElement)commentRef.AppendChild(NewElement(doc, "commentRefIdent"))!;
        var commentCode = (XmlElement)commentRefIdent.AppendChild(NewElement(doc, "commentCode"))!;
        commentCode.SetAttribute("modelIdentCode", modelIdentCode);
        commentCode.SetAttribute("senderIdent", senderIdent);
        commentCode.SetAttribute("yearOfDataIssue", yearOfDataIssue);
        commentCode.SetAttribute("seqNumber", seqNumber);
        commentCode.SetAttribute("commentType", commentType);

        if (opts != Opt.None)
        {
            XmlDocument? refDoc = fname != null ? ReadXmlDoc(fname) : null;
            XmlNode? refCommentIdent = null;
            if (refDoc != null)
            {
                XmlNode? addr = XPathFirst(refDoc, "//commentAddress");
                refCommentIdent = FindChild(addr, "commentIdent");
            }

            if (IsSet(opts, Opt.Lang))
            {
                XmlElement? language = null;
                if (refDoc != null)
                {
                    XmlNode? l = FindChild(refCommentIdent, "language");
                    language = l != null ? (XmlElement)doc.ImportNode(l, true) : null;
                }
                else
                {
                    string? s2 = SecondUnderscore(reff);
                    language = s2 != null ? NewLanguage(doc, s2) : null;
                    if (language == null) Warning($"Could not read language from comment: {reff}");
                }
                if (language != null) commentRefIdent.AppendChild(language);
            }

            if (IsSet(opts, Opt.Url)) SetXlink(commentRef, fname ?? "");
        }

        return commentRef;
    }

    private XmlNode NewDmlRef(XmlDocument doc, string reff, string? fname, Opt opts)
    {
        string[] parts = SplitCode(reff);
        if (parts.Length < 6) { BadInput($"DML code invalid: {reff}"); }

        string modelIdentCode = parts[1];
        string senderIdent = parts[2];
        string dmlType = parts[3].Length >= 1 ? parts[3][..1].ToLowerInvariant() : "";
        string yearOfDataIssue = parts[4];
        string seqNumber = parts[5];

        var dmlRef = NewElement(doc, "dmlRef");
        var dmlRefIdent = (XmlElement)dmlRef.AppendChild(NewElement(doc, "dmlRefIdent"))!;
        var dmlCode = (XmlElement)dmlRefIdent.AppendChild(NewElement(doc, "dmlCode"))!;
        dmlCode.SetAttribute("modelIdentCode", modelIdentCode);
        dmlCode.SetAttribute("senderIdent", senderIdent);
        dmlCode.SetAttribute("dmlType", dmlType);
        dmlCode.SetAttribute("yearOfDataIssue", yearOfDataIssue);
        dmlCode.SetAttribute("seqNumber", seqNumber);

        if (opts != Opt.None)
        {
            XmlDocument? refDoc = fname != null ? ReadXmlDoc(fname) : null;
            XmlNode? refDmlIdent = null;
            if (refDoc != null)
            {
                XmlNode? addr = XPathFirst(refDoc, "//dmlAddress");
                refDmlIdent = FindChild(addr, "dmlIdent");
            }

            int us = reff.IndexOf('_');
            string? s = us >= 0 ? reff[us..] : null;

            if (IsSet(opts, Opt.Issue))
            {
                XmlElement? issueInfo = null;
                if (refDoc != null)
                {
                    XmlNode? ii = FindChild(refDmlIdent, "issueInfo");
                    issueInfo = ii != null ? (XmlElement)doc.ImportNode(ii, true) : null;
                }
                else if (s != null && s.Length > 1 && char.IsDigit(s[1]))
                {
                    issueInfo = NewIssueInfo(doc, s);
                }
                else
                {
                    Warning($"Could not read issue info from DML: {reff}");
                }
                if (issueInfo != null) dmlRefIdent.AppendChild(issueInfo);
            }

            if (IsSet(opts, Opt.Url)) SetXlink(dmlRef, fname ?? "");
        }

        return dmlRef;
    }

    private XmlNode NewIcnRef(XmlDocument doc, string reff, string? fname, Opt opts)
    {
        var infoEntityRef = NewElement(doc, "infoEntityRef");
        infoEntityRef.SetAttribute("infoEntityRefIdent", reff);
        return infoEntityRef;
    }

    private XmlNode NewCsnRef(XmlDocument doc, string reff, string? fname, Opt opts)
    {
        // CSN-%14[^-]-%4[^-]-%3[^-]-%1s%1s-%4[^-]-%2s%3[^-]-%3s%1s-%1s
        string[] parts = SplitCode(reff);
        if (parts.Length < 9) { BadInput($"CSN invalid: {reff}"); }

        string modelIdentCode = parts[1];
        string systemDiffCode = parts[2];
        string systemCode = parts[3];
        string subSysCombined = parts[4];
        string assyCode = parts[5];
        string figureCombined = parts[6];   // figureNumber(2) + variant
        string itemCombined = parts[7];      // item(3) + variant
        string itemLocationCode = parts[8];

        string subSystemCode = subSysCombined.Length > 0 ? subSysCombined[..1] : "";
        string subSubSystemCode = subSysCombined.Length > 1 ? subSysCombined[1..] : "";
        string figureNumber = figureCombined.Length >= 2 ? figureCombined[..2] : figureCombined;
        string figureNumberVariant = figureCombined.Length > 2 ? figureCombined[2..] : "";
        string item = itemCombined.Length >= 3 ? itemCombined[..3] : itemCombined;
        string itemVariant = itemCombined.Length > 3 ? itemCombined[3..] : "";

        var csnRef = NewElement(doc, "catalogSeqNumberRef");
        csnRef.SetAttribute("modelIdentCode", modelIdentCode);
        csnRef.SetAttribute("systemDiffCode", systemDiffCode);
        csnRef.SetAttribute("systemCode", systemCode);
        csnRef.SetAttribute("subSystemCode", subSystemCode);
        csnRef.SetAttribute("subSubSystemCode", subSubSystemCode);
        csnRef.SetAttribute("assyCode", assyCode);
        csnRef.SetAttribute("figureNumber", figureNumber);
        if (figureNumberVariant != "*" && figureNumberVariant != "")
            csnRef.SetAttribute("figureNumberVariant", figureNumberVariant);
        csnRef.SetAttribute("item", item);
        if (itemVariant != "*" && itemVariant != "")
            csnRef.SetAttribute("itemVariant", itemVariant);
        csnRef.SetAttribute("itemLocationCode", itemLocationCode);

        if (IsSet(opts, Opt.Url)) SetXlink(csnRef, fname ?? "");

        return csnRef;
    }

    private XmlNode NewExtPub(XmlDocument doc, string reff, string? fname, Opt opts)
    {
        var epr = NewElement(doc, "externalPubRef");
        var eprIdent = (XmlElement)epr.AppendChild(NewElement(doc, "externalPubRefIdent"))!;
        if (IsSet(opts, Opt.Title))
        {
            AppendTextChild(eprIdent, "externalPubTitle", reff);
        }
        else
        {
            AppendTextChild(eprIdent, "externalPubCode", reff);
        }
        if (IsSet(opts, Opt.Url)) SetXlink(epr, fname ?? "");
        return epr;
    }

    /* ----- shared issue/lang extraction ----- */

    private XmlElement? BuildIssueInfo(XmlDocument doc, XmlDocument? refDoc, XmlNode? refIdent, string? s, string kind, string reff)
    {
        if (refDoc != null)
        {
            XmlNode? node = XPathFirst(refIdent, "issueInfo|issno");
            string? issno = node != null ? XPathFirstValue(node, "@issueNumber|@issno") : null;
            string? inwork = node != null ? XPathFirstValue(node, "@inWork|@inwork") : null;
            inwork ??= "00";
            var e = NewElement(doc, "issueInfo");
            e.SetAttribute("issueNumber", issno ?? "");
            e.SetAttribute("inWork", inwork);
            return e;
        }
        if (s != null && s.Length > 1 && char.IsDigit(s[1]))
        {
            return NewIssueInfo(doc, s);
        }
        Warning($"Could not read issue info from {kind}: {reff}");
        return null;
    }

    private XmlElement? BuildLanguage(XmlDocument doc, XmlDocument? refDoc, XmlNode? refIdent, string? s, string kind, string reff)
    {
        if (refDoc != null)
        {
            XmlNode? node = FindChild(refIdent, "language");
            string? l = node != null ? XPathFirstValue(node, "@languageIsoCode|@language") : null;
            string? c = node != null ? XPathFirstValue(node, "@countryIsoCode|@country") : null;
            var e = NewElement(doc, "language");
            e.SetAttribute("languageIsoCode", l ?? "");
            e.SetAttribute("countryIsoCode", c ?? "");
            return e;
        }
        string? s2 = s != null ? SecondUnderscoreFrom(s) : null;
        if (s2 != null)
        {
            return NewLanguage(doc, s2);
        }
        Warning($"Could not read language from {kind}: {reff}");
        return null;
    }

    /* ----- parsing helpers ----- */

    /// <summary>Strip the issue/language suffix and split a code into '-'-delimited fields.</summary>
    private static string[] SplitCode(string reff)
    {
        // The issue info (_NNN-WW) and language (_XX-XX) start at the first '_'.
        int us = reff.IndexOf('_');
        string codePart = us >= 0 ? reff[..us] : reff;
        return codePart.Split('-');
    }

    /// <summary>Return the substring beginning at the second '_' in the ref (the language segment), or null.</summary>
    private static string? SecondUnderscore(string reff)
    {
        int first = reff.IndexOf('_');
        if (first < 0) return null;
        int second = reff.IndexOf('_', first + 1);
        return second < 0 ? null : reff[second..];
    }

    /// <summary>Given a string already starting at '_', return the substring at the next '_'.</summary>
    private static string? SecondUnderscoreFrom(string s)
    {
        if (s.Length == 0 || s[0] != '_') return null;
        int next = s.IndexOf('_', 1);
        return next < 0 ? null : s[next..];
    }

    private static void AppendTextChild(XmlElement parent, string name, string text)
    {
        var child = parent.OwnerDocument.CreateElement(name);
        child.AppendChild(parent.OwnerDocument.CreateTextNode(text));
        parent.AppendChild(child);
    }

    private static XmlElement RenameElement(XmlElement old, string newName)
    {
        XmlDocument doc = old.OwnerDocument;
        var renamed = doc.CreateElement(newName);
        foreach (XmlAttribute attr in old.Attributes)
        {
            renamed.SetAttribute(attr.Name, attr.Value);
        }
        foreach (XmlNode child in old.ChildNodes)
        {
            renamed.AppendChild(child.CloneNode(true));
        }
        return renamed;
    }

    private void BadInput(string message)
    {
        Error(message);
        throw new ExitException(ExitBadInput);
    }

    private static XmlNode? XPathFirst(XmlNode? context, string xpath) => context?.SelectSingleNode(xpath);

    private static string? XPathFirstValue(XmlNode? context, string xpath)
    {
        XmlNode? n = context?.SelectSingleNode(xpath);
        return n?.Value ?? n?.InnerText;
    }

    /* ----- print/insert a single ref ----- */

    private void PrintRef(string src, string dst, string reff, string? fname, Opt opts,
        bool overwrite, Issue iss, XmlDocument? extpubs, TextWriter stdout)
    {
        var doc = XmlUtils.NewDocument();
        string fullref = IsSet(opts, Opt.NonStrict) ? AddPrefix(reff) : reff;

        NewRef? f = null;
        XmlNode? node = null;

        if (IsDmRef(fullref)) f = NewDmRef;
        else if (IsPmRef(fullref)) f = NewPmRef;
        else if (IsSmcRef(fullref)) f = NewSmcRef;
        else if (IsComRef(fullref)) f = NewComRef;
        else if (IsDmlRef(fullref)) f = NewDmlRef;
        else if (IsIcnRef(fullref)) f = NewIcnRef;
        else if (IsCsnRef(fullref)) f = NewCsnRef;
        else if (extpubs != null && (node = FindExtPub(doc, extpubs, fullref)) != null) f = null;
        else if (fname != null && (node = FindRefType(doc, fname, opts)) != null) f = null;
        else f = NewExtPub;

        if (f != null)
        {
            node = f(doc, fullref, fname, opts);
        }

        if (node == null)
        {
            return;
        }

        if ((int)iss < (int)DefaultIssue)
        {
            node = TransformIssue(doc, node, iss);
        }

        if (IsSet(opts, Opt.Ins))
        {
            Info($"Adding reference {fullref} to {src}...", Verbosity.Verbose);
            if (overwrite)
            {
                AddRef(src, src, node, opts);
            }
            else
            {
                AddRef(src, dst, node, opts, stdout);
            }
        }
        else
        {
            DumpNode(node, dst, stdout);
        }
    }

    private void DumpNode(XmlNode node, string dst, TextWriter stdout)
    {
        // xmlNodeDump: raw node serialization, no XML declaration, single line.
        string xml = node.OuterXml;
        if (dst == "-")
        {
            stdout.WriteLine(xml);
        }
        else
        {
            File.WriteAllText(dst, xml, new UTF8Encoding(false));
        }
    }

    /* ----- prefix guessing ----- */

    private static string AddPrefix(string reff)
    {
        if (Regex.IsMatch(reff, DmeRegexNopre)) return "DME-" + reff;
        if (Regex.IsMatch(reff, DmcRegexNopre)) return "DMC-" + reff;
        if (Regex.IsMatch(reff, PmeRegexNopre)) return "PME-" + reff;
        if (Regex.IsMatch(reff, PmcRegexNopre)) return "PMC-" + reff;
        if (Regex.IsMatch(reff, ComRegexNopre)) return "COM-" + reff;
        if (Regex.IsMatch(reff, DmlRegexNopre)) return "DML-" + reff;
        return reff;
    }

    /* ----- external pub resolution ----- */

    private XmlNode? FindExtPub(XmlDocument doc, XmlDocument extpubs, string reff)
    {
        string escaped = reff.Replace("'", "&apos;");
        XmlNode? node = extpubs.SelectSingleNode($"//externalPubRef[externalPubRefIdent/externalPubCode='{escaped}']");
        node ??= extpubs.SelectSingleNode($"//externalPubRef[starts-with('{escaped}', externalPubRefIdent/externalPubCode)]");
        return node == null ? null : doc.ImportNode(node, true);
    }

    /* ----- resolve type via ref.xsl ----- */

    private XmlNode? FindRefType(XmlDocument doc, string fname, Opt opts)
    {
        XmlDocument? refDoc = ReadXmlDoc(fname);
        if (refDoc == null) return null;

        string text;
        try
        {
            text = ApplyTextXslt("ref/ref.xsl", refDoc).Trim();
        }
        catch (Exception ex) when (ex is XsltException or XmlException)
        {
            return null;
        }

        if (text.Length == 0) return null;

        NewRef? f = null;
        if (IsDmRef(text)) f = NewDmRef;
        else if (IsPmRef(text)) f = NewPmRef;
        else if (IsSmcRef(text)) f = NewSmcRef;
        else if (IsComRef(text)) f = NewComRef;
        else if (IsDmlRef(text)) f = NewDmlRef;
        else if (IsIcnRef(text)) f = NewIcnRef;

        return f?.Invoke(doc, text, fname, opts);
    }

    private static string ApplyTextXslt(string resource, XmlDocument input)
    {
        var transform = new XslCompiledTransform();
        using (Stream s = EmbeddedResources.Open(resource)!)
        using (var reader = XmlReader.Create(s))
        {
            transform.Load(reader);
        }
        using var sw = new StringWriter();
        transform.Transform(input, null, sw);
        return sw.ToString();
    }

    /* ----- issue downgrade ----- */

    private XmlNode TransformIssue(XmlDocument doc, XmlNode node, Issue iss)
    {
        string? xsl = iss switch
        {
            Issue.Iss20 => "newdm/common/to20.xsl",
            Issue.Iss21 => "newdm/common/to21.xsl",
            Issue.Iss22 => "newdm/common/to22.xsl",
            Issue.Iss23 => "newdm/common/to23.xsl",
            Issue.Iss30 => "newdm/common/to30.xsl",
            Issue.Iss40 => "newdm/common/to40.xsl",
            Issue.Iss41 => "newdm/common/to41.xsl",
            Issue.Iss42 => "newdm/common/to42.xsl",
            _ => null,
        };
        if (xsl == null) return node;

        var fragDoc = XmlUtils.NewDocument();
        fragDoc.AppendChild(fragDoc.ImportNode(node, true));

        var transform = new XslCompiledTransform();
        using (Stream s = EmbeddedResources.Open(xsl)!)
        using (var reader = XmlReader.Create(s))
        {
            transform.Load(reader);
        }

        var result = XmlUtils.NewDocument();
        using (var sw = new StringWriter())
        {
            using (var xw = XmlWriter.Create(sw, transform.OutputSettings ?? new XmlWriterSettings()))
            {
                transform.Transform(fragDoc, xw);
            }
            result.LoadXml(sw.ToString());
        }

        return doc.ImportNode(result.DocumentElement!, true);
    }

    /* ----- insertion into source DM ----- */

    private void AddRef(string src, string dst, XmlNode reff, Opt opts, TextWriter? stdout = null)
    {
        XmlDocument? doc = ReadXmlDoc(src);
        if (doc == null)
        {
            Error($"Could not read source data module: {src}");
            throw new ExitException(ExitMissingFile);
        }

        XmlNode imported = doc.ImportNode(reff, true);

        if (IsSet(opts, Opt.SrcId))
        {
            XmlNode? existing = doc.SelectSingleNode("//dmStatus/sourceDmIdent|//pmStatus/sourcePmIdent|//status/srcdmaddres");
            XmlNode? anchor = doc.SelectSingleNode("(//dmStatus/repositorySourceDmIdent|//dmStatus/security|//pmStatus/security|//status/security)[1]");
            if (anchor != null)
            {
                if (existing != null)
                {
                    existing.ParentNode?.RemoveChild(existing);
                }
                anchor.ParentNode!.InsertBefore(imported, anchor);
            }
        }
        else
        {
            XmlNode refs = FindOrCreateRefs(doc);
            refs.AppendChild(imported);
        }

        if (dst == "-")
        {
            (stdout ?? Console.Out).Write(XmlUtils.ToXmlString(doc));
            (stdout ?? Console.Out).Write('\n');
        }
        else
        {
            XmlUtils.SaveDoc(doc, dst);
        }
    }

    private static XmlNode FindOrCreateRefs(XmlDocument doc)
    {
        XmlNode? refs = doc.SelectSingleNode("//content//refs");
        if (refs != null) return refs;

        XmlNode? content = doc.SelectSingleNode("//content");
        var newRefs = doc.CreateElement("refs");
        XmlNode? child = content?.FirstChild;
        while (child != null && child.NodeType != XmlNodeType.Element) child = child.NextSibling;
        if (child != null)
        {
            content!.InsertBefore(newRefs, child);
        }
        else
        {
            content?.AppendChild(newRefs);
        }
        return newRefs;
    }

    /* ----- transform textual references (-T) ----- */

    private void TransformRefsInFile(string path, string transform, string? xpath,
        XmlDocument? extpubs, bool overwrite, Opt opts, TextWriter stdout)
    {
        XmlDocument? doc = ReadXmlDoc(path);
        if (doc == null)
        {
            Error($"Could not read object: {path}");
            throw new ExitException(ExitMissingFile);
        }

        Info($"Transforming textual references in {path}...", Verbosity.Verbose);

        bool nonstrict = IsSet(opts, Opt.NonStrict);

        foreach (char c in transform)
        {
            switch (c)
            {
                case 'C':
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? ComRegex : ComRegexStrict, "COM-", NewComRef, opts);
                    break;
                case 'D':
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? DmeRegex : DmeRegexStrict, "DME-", NewDmRef, opts);
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? DmcRegex : DmcRegexStrict, "DMC-", NewDmRef, opts);
                    break;
                case 'E':
                    if (extpubs != null) TransformExtPubRefsInDoc(doc, path, xpath, extpubs, opts);
                    break;
                case 'G':
                    TransformRefsInDoc(doc, path, xpath, IcnRegex, null, NewIcnRef, opts);
                    break;
                case 'L':
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? DmlRegex : DmlRegexStrict, "DML-", NewDmlRef, opts);
                    break;
                case 'P':
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? PmeRegex : PmeRegexStrict, "PME-", NewPmRef, opts);
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? PmcRegex : PmcRegexStrict, "PMC-", NewPmRef, opts);
                    break;
                case 'S':
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? SmeRegex : SmeRegexStrict, "SME-", NewSmcRef, opts);
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? SmcRegex : SmcRegexStrict, "SMC-", NewSmcRef, opts);
                    break;
                case 'Y':
                    TransformRefsInDoc(doc, path, xpath, nonstrict ? CsnRegex : CsnRegexStrict, "CSN-", NewCsnRef, opts);
                    break;
                default:
                    Warning($"Unknown reference type: {c}");
                    break;
            }
        }

        if (overwrite)
        {
            XmlUtils.SaveDoc(doc, path);
        }
        else
        {
            stdout.Write(XmlUtils.ToXmlString(doc));
            stdout.Write('\n');
        }
    }

    private void TransformRefsInList(string? path, string transform, string? xpath,
        XmlDocument? extpubs, bool overwrite, Opt opts, TextWriter stdout)
    {
        IEnumerable<string> lines;
        if (path != null)
        {
            if (!File.Exists(path))
            {
                Error($"Could not read list: {path}");
                return;
            }
            lines = File.ReadLines(path);
        }
        else
        {
            lines = ReadStdinLines();
        }

        foreach (string raw in lines)
        {
            string line = raw.Split('\t', '\r', '\n')[0];
            if (line.Length == 0) continue;
            TransformRefsInFile(line, transform, xpath, extpubs, overwrite, opts, stdout);
        }
    }

    private static IEnumerable<string> ReadStdinLines()
    {
        using var reader = new StreamReader(Console.OpenStandardInput());
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            yield return line;
        }
    }

    private void TransformExtPubRefsInDoc(XmlDocument doc, string path, string? xpath, XmlDocument extpubs, Opt opts)
    {
        var codes = extpubs.SelectNodes("//externalPubCode");
        if (codes == null) return;
        foreach (XmlNode code in codes)
        {
            string esc = RegexEsc(code.InnerText);
            TransformRefsInDoc(doc, path, xpath, esc, null, NewExtPub, opts);
        }
    }

    private void TransformRefsInDoc(XmlDocument doc, string path, string? xpath, string regex,
        string? prefix, NewRef f, Opt opts)
    {
        XmlNode contextRoot;
        if (IsSet(opts, Opt.Content))
        {
            XmlNode? content = doc.SelectSingleNode("//content");
            if (content == null) return;
            contextRoot = content;
        }
        else
        {
            if (doc.DocumentElement == null) return;
            contextRoot = doc.DocumentElement;
        }

        XmlNodeList? nodes;
        string effectiveXpath = xpath ?? BuiltinXpath(f);
        try
        {
            nodes = contextRoot.SelectNodes(effectiveXpath);
        }
        catch (System.Xml.XPath.XPathException)
        {
            Error($"Invalid XPath expression: {effectiveXpath}");
            throw new ExitException(ExitBadXpath);
        }

        if (nodes == null) return;

        var re = new Regex(regex);

        // Snapshot since we mutate the tree while iterating.
        var snapshot = new List<XmlNode>();
        foreach (XmlNode n in nodes) snapshot.Add(n);

        foreach (XmlNode textNode in snapshot)
        {
            TransformRefsInTextNode(doc, textNode, re, prefix, f, opts);
        }
    }

    private string BuiltinXpath(NewRef f)
    {
        string? resource = null;
        if (f == NewDmRef) resource = "ref/elems/dmc.txt";
        else if (f == NewPmRef) resource = "ref/elems/pmc.txt";
        else if (f == NewCsnRef) resource = "ref/elems/csn.txt";
        else if (f == NewDmlRef) resource = "ref/elems/dml.txt";
        else if (f == NewIcnRef) resource = "ref/elems/icn.txt";
        else if (f == NewSmcRef) resource = "ref/elems/smc.txt";
        else if (f == NewExtPub) resource = "ref/elems/epr.txt";

        if (resource == null)
        {
            return "descendant-or-self::*/text()";
        }
        // The elems files are written with line breaks; collapse to a single expression.
        return EmbeddedResources.ReadText(resource).Replace("\r", "").Replace("\n", "").Trim();
    }

    /// <summary>
    /// Replace all textual references matched by <paramref name="re"/> inside a single
    /// text node, splicing in generated reference elements. Mirrors
    /// transform_refs_in_node / transform_ref.
    /// </summary>
    private void TransformRefsInTextNode(XmlDocument doc, XmlNode textNode, Regex re,
        string? prefix, NewRef f, Opt opts)
    {
        if (textNode.NodeType != XmlNodeType.Text && textNode.NodeType != XmlNodeType.Whitespace
            && textNode.NodeType != XmlNodeType.SignificantWhitespace)
        {
            return;
        }

        string content = textNode.Value ?? "";
        XmlNode parent = textNode.ParentNode!;
        XmlNode anchor = textNode; // node we insert after
        XmlNode current = textNode;

        while (true)
        {
            Match m = re.Match(content);
            if (!m.Success) break;

            int so = m.Index;
            int eo = m.Index + m.Length;

            // Conflicting-prefix skip (only in non-strict mode).
            if (IsSet(opts, Opt.NonStrict))
            {
                string? conflict = null;
                if (f == NewDmRef) conflict = "CSN-";
                else if (f == NewCsnRef) conflict = "DMC-";
                else if (f == NewPmRef) conflict = "SMC-";
                else if (f == NewSmcRef) conflict = "PMC-";

                if (conflict != null && so >= 4 &&
                    content.AsSpan(so - 4, 4).SequenceEqual(conflict))
                {
                    // Split: keep up to eo in current node, continue with remainder.
                    string keep = content[..eo];
                    string rest = content[eo..];
                    current.Value = keep;
                    var restNode = doc.CreateTextNode(rest);
                    parent.InsertAfter(restNode, current);
                    current = restNode;
                    content = rest;
                    continue;
                }
            }

            string matched = content.Substring(so, m.Length);
            string r;
            if (prefix != null && !matched.StartsWith(prefix, StringComparison.Ordinal))
            {
                r = prefix + matched;
            }
            else
            {
                r = matched;
            }

            string before = content[..so];
            string after = content[eo..];

            current.Value = before;

            XmlNode? refNode = f(doc, r, null, opts);
            XmlNode afterNode = doc.CreateTextNode(after);

            if (refNode != null)
            {
                parent.InsertAfter(refNode, current);
                parent.InsertAfter(afterNode, refNode);
            }
            else
            {
                parent.InsertAfter(afterNode, current);
            }

            current = afterNode;
            content = after;
        }

        _ = anchor;
    }

    private static string RegexEsc(string s)
    {
        var sb = new StringBuilder(s.Length * 2);
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '.':
                case '[':
                case '{':
                case '}':
                case '(':
                case ')':
                case '\\':
                case '*':
                case '+':
                case '?':
                case '|':
                case '^':
                case '$':
                    sb.Append('\\');
                    break;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-cdfgiLlqRrStuvh?] [-$ <issue>] [-s <src>] [-T <opts>] [-o <dst>] [-x <xpath>] [-3 <file>] [<code>|<file> ...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -$, --issue <issue>        Output XML for the specified issue of S1000D.");
        stdout.WriteLine("  -c, --content              Only transform textual references in the content section.");
        stdout.WriteLine("  -d, --include-date         Include issue date (target must be file).");
        stdout.WriteLine("  -f, --overwrite            Overwrite source data module instead of writing to stdout.");
        stdout.WriteLine("  -h, -?, --help             Show this help message.");
        stdout.WriteLine("  -i, --include-issue        Include issue info.");
        stdout.WriteLine("  -L, --list                 Treat input as a list of CSDB objects.");
        stdout.WriteLine("  -l, --include-lang         Include language.");
        stdout.WriteLine("  -o, --out <dst>            Output to <dst> instead of stdout.");
        stdout.WriteLine("  -g, --guess-prefix         Accept references without a prefix.");
        stdout.WriteLine("  -q, --quiet                Quiet mode. Do not print errors.");
        stdout.WriteLine("  -R, --repository-id        Generate a <repositorySourceDmIdent>.");
        stdout.WriteLine("  -r, --add                  Add reference to data module's <refs> table.");
        stdout.WriteLine("  -S, --source-id            Generate a <sourceDmIdent> or <sourcePmIdent>.");
        stdout.WriteLine("  -s, --source <src>         Source data module to add references to.");
        stdout.WriteLine("  -T, --transform <opts>     Transform textual references to XML in objects.");
        stdout.WriteLine("  -t, --include-title        Include title (target must be file)");
        stdout.WriteLine("  -u, --include-url          Include xlink:href to the full URL/filename.");
        stdout.WriteLine("  -v, --verbose              Verbose output.");
        stdout.WriteLine("  -x, --xpath <xpath>        Transform textual references using <xpath>.");
        stdout.WriteLine("  -3, --externalpubs <file>  Use a custom .externalpubs file.");
        stdout.WriteLine("  --version                  Show version information.");
        stdout.WriteLine("  <code>                     The code of the reference (must include prefix DMC/PMC/etc.).");
        stdout.WriteLine("  <file>                     A file to reference, or transform references in (-T).");
    }
}
