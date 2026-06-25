using System.Globalization;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-upissue</c>: create a new issue of a CSDB object by
/// incrementing its issue or inwork number, optionally renaming the file to
/// match. Mirrors the C tool's option set, exit codes, issue/inwork
/// incrementing, official/inwork transitions, RFU/change-mark handling and
/// file-renaming behaviour.
/// </summary>
public sealed class UpissueTool : ITool
{
    public string Name => "upissue";
    public string Description => "Create a new issue of a CSDB object.";
    public string Version => "5.0.1";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitNoFile = 1;
    private const int ExitNoOverwrite = 2;
    private const int ExitBadFilename = 3;
    private const int ExitBadDate = 4;
    private const int ExitIcnInwork = 5;
    private const int ExitIssueTooLarge = 6;

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2 }

    /* Per-run option state. */
    private Verbosity _verbosity = Verbosity.Normal;
    private bool _printFnames;
    private bool _newissue;
    private bool _overwrite;
    private string? _status;
    private bool _noIssue;
    private bool _keepRfus;
    private bool _setDate = true;
    private bool _onlyAssocRfus;
    private bool _resetQa = true;
    private bool _dryRun;
    private string? _firstver;
    private string? _secondver;
    private bool _lock;
    private bool _remdel;
    private bool _remold;
    private bool _onlyMod;
    private bool _cleanRfus;
    private string? _issdate;
    private bool _removeMarks;
    private bool _setUnverif;

    /// <summary>Container element holding the queued RFUs (mirrors the C "rfus" node).</summary>
    private XmlElement _rfus = null!;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool islist = false;
        var files = new List<string>();

        // Owning document for the queued RFU nodes.
        var rfuDoc = XmlUtils.NewDocument();
        _rfus = rfuDoc.CreateElement("rfus");
        rfuDoc.AppendChild(_rfus);

        try
        {
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];

                // Combined short options (e.g. -ife) are not split here; the C
                // getopt does, but in practice the tool is driven one flag per
                // token. Each recognised token is handled explicitly.
                switch (a)
                {
                    case "-h" or "-?" or "--help":
                        ShowHelp(stdout);
                        return 0;
                    case "--version":
                        ShowVersion(stdout);
                        return 0;
                    case "-0" or "--unverified":
                        _setUnverif = true;
                        break;
                    case "-1" or "--first-ver":
                        _firstver = NextArg(args, ref i, "-1", stderr);
                        break;
                    case "-2" or "--second-ver":
                        _secondver = NextArg(args, ref i, "-2", stderr);
                        break;
                    case "-4" or "--remove-marks":
                        _removeMarks = true;
                        break;
                    case "-5" or "--print":
                        _printFnames = true;
                        break;
                    case "-c" or "--reason":
                    {
                        string reason = NextArg(args, ref i, "-c", stderr);
                        var rfu = rfuDoc.CreateElement("reasonForUpdate");
                        rfu.InnerText = reason;
                        _rfus.AppendChild(rfu);
                        break;
                    }
                    case "-d" or "--dry-run":
                        _dryRun = true;
                        break;
                    case "-e" or "--erase":
                        _remold = true;
                        break;
                    case "-f" or "--overwrite":
                        _overwrite = true;
                        break;
                    case "-I" or "--date":
                        _issdate = NextArg(args, ref i, "-I", stderr);
                        break;
                    case "-i" or "--official":
                        _newissue = true;
                        break;
                    case "-l" or "--list":
                        islist = true;
                        break;
                    case "-m" or "--modify":
                        _onlyMod = true;
                        _noIssue = true;
                        break;
                    case "-N" or "--omit-issue":
                        _noIssue = true;
                        _overwrite = true;
                        break;
                    case "-Q" or "--keep-qa":
                        _resetQa = false;
                        break;
                    case "-q" or "--quiet":
                        _verbosity--;
                        break;
                    case "-R" or "--keep-unassoc-marks":
                        _onlyAssocRfus = true;
                        break;
                    case "-r" or "--keep-changes" or "--remove-changes":
                        _keepRfus = true;
                        break;
                    case "-s" or "--keep-date" or "--change-date":
                        _setDate = false;
                        break;
                    case "-t" or "--type":
                    {
                        string urt = NextArg(args, ref i, "-t", stderr);
                        if (_rfus.LastChild is XmlElement last)
                        {
                            last.SetAttribute("updateReasonType", urt);
                        }
                        break;
                    }
                    case "-u" or "--clean-rfus":
                        _cleanRfus = true;
                        break;
                    case "-H" or "--highlight":
                        if (_rfus.LastChild is XmlElement lastH)
                        {
                            lastH.SetAttribute("updateHighlight", "1");
                        }
                        break;
                    case "-v" or "--verbose":
                        _verbosity++;
                        break;
                    case "-w" or "--lock":
                        _lock = true;
                        break;
                    case "-z" or "--issue-type":
                        _status = NextArg(args, ref i, "-z", stderr);
                        if (!(_status == "changed" || _status == "rinstate-changed"))
                        {
                            _removeMarks = true;
                        }
                        break;
                    case "-^" or "--remove-deleted":
                        _remdel = true;
                        break;
                    default:
                        if (a.StartsWith('-') && a.Length > 1 && a != "-")
                        {
                            stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                            return 2;
                        }
                        files.Add(a);
                        break;
                }
            }

            if (files.Count > 0)
            {
                foreach (string f in files)
                {
                    if (islist)
                    {
                        UpissueList(f, stdout, stderr);
                    }
                    else
                    {
                        Upissue(f, stdout, stderr);
                    }
                }
            }
            else if (islist)
            {
                UpissueList(null, stdout, stderr);
            }
            else
            {
                _noIssue = true;
                _overwrite = true;
                Upissue("-", stdout, stderr);
            }
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }

        return 0;
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

    private void UpissueList(string? path, TextWriter stdout, TextWriter stderr)
    {
        TextReader reader;
        if (path != null)
        {
            try
            {
                reader = new StreamReader(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{Name}: ERROR: Could not read list: {path}");
                }
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
                // strtok(line, "\t\r\n") -> take up to first tab/cr/nl.
                int cut = line.IndexOfAny(new[] { '\t', '\r', '\n' });
                string entry = cut < 0 ? line : line[..cut];
                if (entry.Length == 0)
                {
                    continue;
                }
                Upissue(entry, stdout, stderr);
            }
        }
        finally
        {
            if (path != null)
            {
                reader.Dispose();
            }
        }
    }

    private void Upissue(string path, TextWriter stdout, TextWriter stderr)
    {
        string dmfile = path;

        if (dmfile != "-" && !File.Exists(dmfile))
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{Name}: ERROR: Could not read file {dmfile}.");
            }
            throw new ExitException(ExitNoFile);
        }

        XmlDocument? dmdoc;
        try
        {
            dmdoc = dmfile == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(dmfile);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            // libxml2 returns NULL for non-XML input; mirror that by treating
            // the file as a non-XML object to be copied/renamed.
            dmdoc = null;
        }

        XmlElement? issueInfo = dmdoc != null
            ? XmlUtils.XPathFirstNode(dmdoc, null, "//issueInfo|//issno") as XmlElement
            : null;

        if (issueInfo == null && _noIssue)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{Name}: ERROR: Cannot use -m, -N or read from stdin when file does not contain issue info metadata.");
            }
            throw new ExitException(ExitNoOverwrite);
        }

        /* -m: apply modifications without upissuing. */
        if (_onlyMod)
        {
            bool iss30m = issueInfo!.Name != "issueInfo";

            if (_cleanRfus && !iss30m)
            {
                RemUnassocRfus(dmdoc!);
            }
            if (_remdel)
            {
                XmlUtils.RemoveDeleteElements(dmdoc!);
            }
            if (_setUnverif)
            {
                SetUnverified(dmdoc!, iss30m);
            }

            AddRfus(dmdoc!, iss30m);
            SetQa(dmdoc!, _firstver, _secondver, iss30m);

            if (_status != null)
            {
                SetStatus(dmdoc!, _status, iss30m, issueInfo);
            }

            // In -m mode, -s sets the date and -r removes RFUs (opposite sense).
            if (!_setDate)
            {
                SetIssDate(dmdoc!, _issdate);
            }
            if (_keepRfus)
            {
                DelRfus(dmdoc!, _onlyAssocRfus, iss30m);
            }
            else if (_removeMarks)
            {
                DelMarks(dmdoc!, iss30m);
            }

            if (_overwrite)
            {
                XmlUtils.SaveDoc(dmdoc!, dmfile);
            }
            else
            {
                stdout.Write(XmlUtils.ToXmlString(dmdoc!));
                stdout.Write('\n');
            }
            return;
        }

        string? issueNumber;
        string? inWork;
        bool iss30 = false;

        int p = dmfile.IndexOf('_'); // position of the first '_'
        // Mirrors the C variable `p`: when >= 0 the filename carries the
        // issue/inwork field and may be spliced. The ICN branch reassigns it to
        // the position of a '-'.
        int i = p + 1; // index in dmfile where issue digits begin (for renaming)

        if (issueInfo != null)
        {
            iss30 = issueInfo.Name != "issueInfo";

            string issnoName = iss30 ? "issno" : "issueNumber";
            string inworkName = iss30 ? "inwork" : "inWork";

            issueNumber = GetPropOrNull(issueInfo, issnoName);
            inWork = GetPropOrNull(issueInfo, inworkName) ?? "00";
        }
        else if (p >= 0)
        {
            i = p + 1;

            if (i > dmfile.Length - 6)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{Name}: ERROR: Filename does not contain issue info and -N not specified.");
                }
                throw new ExitException(ExitBadFilename);
            }

            issueNumber = dmfile.Substring(i, 3);
            inWork = dmfile.Substring(i + 4, 2);
        }
        else if ((p = dmfile.IndexOf('-')) >= 0)
        {
            // ICN: derive issue from the field after the second-to-last '-'.
            if (!_newissue)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{Name}: ERROR: ICNs cannot have inwork issues.");
                }
                throw new ExitException(ExitIcnInwork);
            }

            int l = dmfile.Length;
            int n;
            int c = 0;
            for (n = l; n >= 0; --n)
            {
                if (n < l && dmfile[n] == '-')
                {
                    if (c == 1)
                    {
                        break;
                    }
                    ++c;
                }
            }

            i = n + 1;

            if (n == -1 || i > l - 6)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{Name}: ERROR: Filename does not contain issue info and -N not specified.");
                }
                throw new ExitException(ExitBadFilename);
            }

            issueNumber = dmfile.Substring(i, 3);
            inWork = null;
        }
        else
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{Name}: ERROR: Filename does not contain issue info and -N not specified.");
            }
            throw new ExitException(ExitBadFilename);
        }

        int issueNumberInt = Atoi(issueNumber);
        if (issueNumberInt >= 999)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{Name}: ERROR: {dmfile} is at the max issue number.");
            }
            throw new ExitException(ExitIssueTooLarge);
        }

        int inWorkInt = 0;
        if (inWork != null)
        {
            inWorkInt = Atoi(inWork);
            if (inWorkInt >= 99)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{Name}: ERROR: {dmfile} is at the max inwork number.");
                }
                throw new ExitException(ExitIssueTooLarge);
            }
        }

        int upIssueNumberInt;
        int upInWorkInt = 0;
        if (_newissue)
        {
            upIssueNumberInt = issueNumberInt + 1;
            // up_inWork_int stays 0 when inWork present.
        }
        else
        {
            upIssueNumberInt = issueNumberInt;
            if (inWork != null)
            {
                upInWorkInt = inWorkInt + 1;
            }
        }

        string upissuedIssueNumber = upIssueNumberInt.ToString("D3", CultureInfo.InvariantCulture);
        string upissuedInWork = upInWorkInt.ToString("D2", CultureInfo.InvariantCulture);

        if (issueInfo != null)
        {
            string issnoName = iss30 ? "issno" : "issueNumber";
            string inworkName = iss30 ? "inwork" : "inWork";

            issueInfo.SetAttribute(issnoName, upissuedIssueNumber);
            issueInfo.SetAttribute(inworkName, upissuedInWork);

            if (_cleanRfus && !iss30)
            {
                RemUnassocRfus(dmdoc!);
            }

            // Upissuing an official module to its first inwork issue.
            if (inWork == "00")
            {
                if (!_keepRfus)
                {
                    DelRfus(dmdoc!, _onlyAssocRfus, iss30);
                }
                if (_resetQa)
                {
                    SetUnverified(dmdoc!, iss30);
                }
            }
            else
            {
                if (_remdel)
                {
                    XmlUtils.RemoveDeleteElements(dmdoc!);
                }
                if (_setUnverif)
                {
                    SetUnverified(dmdoc!, iss30);
                }
            }

            if (_setDate)
            {
                SetIssDate(dmdoc!, _issdate);
            }

            SetQa(dmdoc!, _firstver, _secondver, iss30);
            AddRfus(dmdoc!, iss30);

            if (_status != null)
            {
                SetStatus(dmdoc!, _status, iss30, issueInfo);
            }
            else if (inWorkInt == 0)
            {
                SetStatus(dmdoc!, "status", iss30, issueInfo);
            }

            if (_removeMarks)
            {
                DelMarks(dmdoc!, iss30);
            }
        }

        string cpfile = dmfile; // preserve non-XML filename for copying

        if (!_noIssue)
        {
            if (!_dryRun)
            {
                if (_remold && dmdoc != null)
                {
                    TryDelete(dmfile);
                }
                else if (_lock)
                {
                    MkReadonly(dmfile);
                }
            }

            // Mirrors C `if (p)`: splice only when an issue/inwork field exists
            // in the filename (the issueInfo branch leaves p at the '_'; the ICN
            // branch reassigns it to a '-'; otherwise p is -1 = no splice).
            if (p >= 0 && i >= 0 && i + 3 <= dmfile.Length)
            {
                dmfile = ReplaceAt(dmfile, i, upissuedIssueNumber);
                if (inWork != null && i + 6 <= dmfile.Length)
                {
                    dmfile = ReplaceAt(dmfile, i + 4, upissuedInWork);
                }
            }
        }

        if (!_dryRun)
        {
            if (!_overwrite && File.Exists(dmfile))
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{Name}: ERROR: {dmfile} already exists. Use -f to overwrite.");
                }
                throw new ExitException(ExitNoOverwrite);
            }

            if (dmdoc != null)
            {
                XmlUtils.SaveDoc(dmdoc, dmfile);
            }
            else
            {
                Copy(cpfile, dmfile);
                if (_remold)
                {
                    TryDelete(cpfile);
                }
            }

            if (_lock && _newissue)
            {
                MkReadonly(dmfile);
            }
        }

        if (_verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"{Name}: INFO: Upissued {path} to {dmfile}");
        }

        if (_printFnames)
        {
            stdout.WriteLine(dmfile);
        }
    }

    /* ----- helpers mirroring the C static functions ----- */

    private static string ReplaceAt(string s, int index, string repl)
    {
        int len = Math.Min(repl.Length, s.Length - index);
        return string.Concat(s.AsSpan(0, index), repl.AsSpan(0, len), s.AsSpan(index + len));
    }

    private static string? GetPropOrNull(XmlElement el, string name) =>
        el.HasAttribute(name) ? el.GetAttribute(name) : null;

    /// <summary>Mirror C <c>atoi</c>: parse leading digits, default 0.</summary>
    private static int Atoi(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return 0;
        }
        int idx = 0;
        bool neg = false;
        if (s[0] is '+' or '-')
        {
            neg = s[0] == '-';
            idx = 1;
        }
        long val = 0;
        for (; idx < s.Length && char.IsAsciiDigit(s[idx]); idx++)
        {
            val = val * 10 + (s[idx] - '0');
        }
        return (int)(neg ? -val : val);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void MkReadonly(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            File.SetAttributes(path, attr | FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void Copy(string from, string to)
    {
        if (!File.Exists(from))
        {
            return;
        }
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.Ordinal))
        {
            return;
        }
        File.Copy(from, to, overwrite: true);
    }

    private static void SetUnverified(XmlDocument doc, bool iss30)
    {
        var qa = XmlUtils.XPathFirstNode(doc, null, iss30 ? "//qa" : "//qualityAssurance");
        if (qa == null)
        {
            return;
        }

        while (qa.FirstChild != null)
        {
            qa.RemoveChild(qa.FirstChild);
        }

        var unverif = doc.CreateElement(iss30 ? "unverif" : "unverified");
        qa.AppendChild(unverif);
    }

    private static void SetQa(XmlDocument doc, string? firstver, string? secondver, bool iss30)
    {
        if (firstver == null && secondver == null)
        {
            return;
        }

        var qa = XmlUtils.XPathFirstNode(doc, null, iss30 ? "//qa" : "//qualityAssurance");
        if (qa == null)
        {
            return;
        }

        string verTypeAttr = iss30 ? "type" : "verificationType";
        string ver1Name = iss30 ? "firstver" : "firstVerification";
        string ver2Name = iss30 ? "secver" : "secondVerification";

        var unverif = XmlUtils.XPathFirstNode(doc, null, iss30 ? "//unverif" : "//unverified");
        unverif?.ParentNode?.RemoveChild(unverif);

        var ver1 = XmlUtils.XPathFirstNode(doc, null, "//" + ver1Name) as XmlElement;
        var ver2 = XmlUtils.XPathFirstNode(doc, null, "//" + ver2Name) as XmlElement;

        if (firstver != null)
        {
            if (secondver == null)
            {
                // Drop any existing second verification when only -1 is given.
                var existingVer2 = XmlUtils.XPathFirstNode(doc, null, "//" + ver2Name);
                existingVer2?.ParentNode?.RemoveChild(existingVer2);
                ver2 = null;
            }

            ver1?.ParentNode?.RemoveChild(ver1);

            ver1 = doc.CreateElement(ver1Name);
            ver1.SetAttribute(verTypeAttr, firstver);
            qa.AppendChild(ver1);
        }

        if (secondver != null)
        {
            if (ver1 == null)
            {
                ver1 = doc.CreateElement(ver1Name);
                ver1.SetAttribute(verTypeAttr, secondver);
                qa.AppendChild(ver1);
            }

            ver2?.ParentNode?.RemoveChild(ver2);

            ver2 = doc.CreateElement(ver2Name);
            ver2.SetAttribute(verTypeAttr, secondver);
            qa.AppendChild(ver2);
        }
    }

    private const string Iss30RfuPath =
        "(//qa|//sbc|//fic|//idstatus//ein|//skill|//rfu)[last()]";
    private const string Iss4XRfuPath =
        "(//qualityAssurance|//systemBreakdownCode|//functionalItemCode|//identAndStatusSection//functionalItemRef|//skillLevel|//reasonForUpdate)[last()]";

    private void AddRfus(XmlDocument doc, bool iss30)
    {
        if (_rfus.FirstChild == null)
        {
            return;
        }

        var node = XmlUtils.XPathFirstNode(doc, null, iss30 ? Iss30RfuPath : Iss4XRfuPath);
        if (node == null)
        {
            return;
        }

        // Skip past any existing trailing RFUs.
        XmlNode? next = NextElementSibling(node);
        while (next != null && (next.Name == "rfu" || next.Name == "reasonForUpdate"))
        {
            node = next;
            next = NextElementSibling(node);
        }

        // Insert copies after node, preserving original order (iterate reverse,
        // inserting each immediately after node).
        for (XmlNode? cur = _rfus.LastChild; cur != null; cur = cur.PreviousSibling)
        {
            var imported = (XmlElement)doc.ImportNode(cur, true);

            if (iss30)
            {
                var rfu = doc.CreateElement("rfu");
                rfu.InnerText = imported.InnerText;
                node.ParentNode!.InsertAfter(rfu, node);
            }
            else
            {
                // 4.x: wrap text content in <simplePara>, keep attributes.
                string content = imported.InnerText;
                while (imported.FirstChild != null)
                {
                    imported.RemoveChild(imported.FirstChild);
                }
                var sp = doc.CreateElement("simplePara");
                sp.InnerText = content;
                imported.AppendChild(sp);
                node.ParentNode!.InsertAfter(imported, node);
            }
        }
    }

    private static XmlNode? NextElementSibling(XmlNode node)
    {
        XmlNode? n = node.NextSibling;
        while (n != null && n.NodeType != XmlNodeType.Element)
        {
            n = n.NextSibling;
        }
        return n;
    }

    private static void SetIssDate(XmlDocument doc, string? issdate)
    {
        var issueDate = XmlUtils.XPathFirstNode(doc, null, "//issueDate|//issdate") as XmlElement;
        if (issueDate == null)
        {
            return;
        }

        string yearS, monthS, dayS;
        if (issdate != null)
        {
            // sscanf("%4s-%2s-%2s") — split on '-' taking bounded fields.
            string[] parts = issdate.Split('-');
            yearS = parts.Length > 0 ? Take(parts[0], 4) : "";
            monthS = parts.Length > 1 ? Take(parts[1], 2) : "";
            dayS = parts.Length > 2 ? Take(parts[2], 2) : "";
        }
        else
        {
            DateTime now = DateTime.Now;
            yearS = now.Year.ToString(CultureInfo.InvariantCulture);
            monthS = now.Month.ToString("D2", CultureInfo.InvariantCulture);
            dayS = now.Day.ToString("D2", CultureInfo.InvariantCulture);
        }

        issueDate.SetAttribute("year", yearS);
        issueDate.SetAttribute("month", monthS);
        issueDate.SetAttribute("day", dayS);
    }

    private static string Take(string s, int n) => s.Length <= n ? s : s[..n];

    private static void SetStatus(XmlDocument doc, string status, bool iss30, XmlElement issueInfo)
    {
        if (iss30)
        {
            issueInfo.SetAttribute("type", status);
        }
        else
        {
            if (XmlUtils.XPathFirstNode(doc, null, "//dmStatus|//pmStatus") is XmlElement dmStatus)
            {
                dmStatus.SetAttribute("issueType", status);
            }
        }
    }

    /* ----- RFU / change-mark deletion ----- */

    private static void RemUnassocRfus(XmlDocument doc)
    {
        var rfus = doc.SelectNodes("//reasonForUpdate");
        if (rfus == null)
        {
            return;
        }
        foreach (XmlNode rfu in rfus)
        {
            if (!RfuUsed(doc, (XmlElement)rfu))
            {
                rfu.ParentNode?.RemoveChild(rfu);
            }
        }
    }

    private static bool RfuUsed(XmlDocument doc, XmlElement rfu)
    {
        string rfuid = rfu.GetAttribute("id");
        var refs = doc.SelectNodes("//@reasonForUpdateRefIds");
        if (refs == null)
        {
            return false;
        }
        foreach (XmlNode attr in refs)
        {
            foreach (string id in SplitIds(attr.Value))
            {
                if (id == rfuid)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static IEnumerable<string> SplitIds(string? ids) =>
        string.IsNullOrEmpty(ids)
            ? Array.Empty<string>()
            : ids.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static void DelAssocRfuAttrs(XmlDocument doc, XmlElement rfu)
    {
        string rfuid = rfu.GetAttribute("id");
        var nodes = doc.SelectNodes("//*[@reasonForUpdateRefIds]");
        if (nodes == null)
        {
            return;
        }
        foreach (XmlNode node in nodes)
        {
            var el = (XmlElement)node;
            bool used = false;
            foreach (string id in SplitIds(el.GetAttribute("reasonForUpdateRefIds")))
            {
                if (id == rfuid)
                {
                    used = true;
                    break;
                }
            }
            if (used)
            {
                el.RemoveAttribute("changeType");
                el.RemoveAttribute("changeMark");
                el.RemoveAttribute("reasonForUpdateRefIds");
            }
        }
    }

    private static void DelRfuAttrs(XmlDocument doc, bool iss30)
    {
        string change, mark, rfc;
        XmlNodeList? nodes;

        if (iss30)
        {
            change = "change";
            mark = "mark";
            rfc = "rfc";
            nodes = doc.SelectNodes("//*[@change or @mark or @rfc or @level]");
        }
        else
        {
            change = "changeType";
            mark = "changeMark";
            rfc = "reasonForUpdateRefIds";
            nodes = doc.SelectNodes("//*[@changeType or @changeMark or @reasonForUpdateRefIds]");
        }

        if (nodes == null)
        {
            return;
        }

        foreach (XmlNode node in nodes)
        {
            var el = (XmlElement)node;
            string type = el.GetAttribute(change);
            if (type == "delete")
            {
                el.ParentNode?.RemoveChild(el);
            }
            else
            {
                el.RemoveAttribute(change);
                el.RemoveAttribute(mark);
                el.RemoveAttribute(rfc);
                if (iss30)
                {
                    el.RemoveAttribute("level");
                }
            }
        }
    }

    private static void DelChangeInline(XmlNode node, bool iss30)
    {
        bool remove;
        if (iss30)
        {
            remove = node.Name == "change" &&
                     node is XmlElement ce &&
                     !(ce.HasAttribute("mark") || ce.HasAttribute("change") || ce.HasAttribute("rfc"));
        }
        else
        {
            remove = node.Name == "changeInline" &&
                     node is XmlElement ie &&
                     !(ie.HasAttribute("changeMark") || ie.HasAttribute("changeType") || ie.HasAttribute("reasonForUpdateRefIds"));
        }

        // Recurse into children first (to handle nested changeInlines).
        XmlNode? cur = node.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            DelChangeInline(cur, iss30);
            cur = next;
        }

        // Merge children into the parent, then unlink the node itself.
        if (remove && node.ParentNode != null)
        {
            XmlNode parent = node.ParentNode;
            XmlNode anchor = node;
            for (XmlNode? child = node.LastChild; child != null; child = node.LastChild)
            {
                node.RemoveChild(child);
                parent.InsertAfter(child, node);
            }
            parent.RemoveChild(node);
        }
    }

    private static void DelRfus(XmlDocument doc, bool onlyAssoc, bool iss30)
    {
        var rfus = doc.SelectNodes("//reasonForUpdate");
        if (rfus != null && rfus.Count > 0)
        {
            if (onlyAssoc)
            {
                foreach (XmlNode rfu in rfus)
                {
                    DelAssocRfuAttrs(doc, (XmlElement)rfu);
                    rfu.ParentNode?.RemoveChild(rfu);
                }
            }
            else
            {
                foreach (XmlNode rfu in rfus)
                {
                    rfu.ParentNode?.RemoveChild(rfu);
                }
                DelRfuAttrs(doc, iss30);
            }
        }

        if (doc.DocumentElement != null)
        {
            DelChangeInline(doc.DocumentElement, iss30);
        }
    }

    private static void DelMarks(XmlDocument doc, bool iss30)
    {
        DelRfuAttrs(doc, iss30);
        if (doc.DocumentElement != null)
        {
            DelChangeInline(doc.DocumentElement, iss30);
        }
    }

    /* ----- help / version ----- */

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-045defHilmNQqRrsuvw^] [-1 <type>] [-2 <type>] [-c <reason>] [-I <date>] [-t <urt>] [-z <type>] [<file>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -0, --unverified             Set the quality assurance to unverified.");
        stdout.WriteLine("  -1, --first-ver <type>       Set first verification type.");
        stdout.WriteLine("  -2, --second-ver <type>      Set second verification type.");
        stdout.WriteLine("  -4, --remove-marks           Remove change marks (but not RFUs).");
        stdout.WriteLine("  -5, --print                  Print filenames of upissued objects.");
        stdout.WriteLine("  -c, --reason <reason>        Add an RFU to the upissued object.");
        stdout.WriteLine("  -d, --dry-run                Do not create or modify any files.");
        stdout.WriteLine("  -e, --erase                  Remove old issue.");
        stdout.WriteLine("  -f, --overwrite              Overwrite existing upissued object.");
        stdout.WriteLine("  -H, --highlight              Highlight the last RFU.");
        stdout.WriteLine("  -h, -?, --help               Show usage message.");
        stdout.WriteLine("  -I, --date <date>            The issue date to use for the upissued objects.");
        stdout.WriteLine("  -i, --official               Increase issue number instead of inwork.");
        stdout.WriteLine("  -l, --list                   Treat input as list of objects.");
        stdout.WriteLine("  -m, --modify                 Modify metadata without upissuing.");
        stdout.WriteLine("  -N, --omit-issue             Omit issue/inwork numbers from filename.");
        stdout.WriteLine("  -Q, --keep-qa                Keep quality assurance from old issue.");
        stdout.WriteLine("  -q, --quiet                  Quiet mode.");
        stdout.WriteLine("  -R, --keep-unassoc-marks     Only delete change marks associated with an RFU.");
        stdout.WriteLine("  -r, --(keep|remove)-changes  Keep RFUs and change marks from old issue. In -m mode, remove them.");
        stdout.WriteLine("  -s, --(keep|change)-date     Do not change issue date. In -m mode, change issue date.");
        stdout.WriteLine("  -t, --type <urt>             Set the updateReasonType of the last RFU.");
        stdout.WriteLine("  -u, --clean-rfus             Remove unassociated RFUs.");
        stdout.WriteLine("  -w, --lock                   Make old and official issues read-only.");
        stdout.WriteLine("  -z, --issue-type <type>      Set the issue type of the new issue.");
        stdout.WriteLine("  -^, --remove-deleted         Remove \"delete\"d elements.");
        stdout.WriteLine("      --version                Show version information");
    }
}
