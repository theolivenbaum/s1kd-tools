using System.Text.RegularExpressions;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-metadata</c>: view and edit S1000D metadata on CSDB objects.
/// Mirrors the C tool's full option set (-0, -c, -d, -E, -e, -F, -f, -H, -l, -m,
/// -n, -q, -T, -t, -v, -w, -W), exit codes and output formatting.
/// </summary>
public sealed class MetadataTool : ITool
{
    public string Name => "metadata";
    public string Description => "View and edit S1000D metadata on CSDB objects.";
    public string Version => "4.7.0";

    private const string ErrPrefix = "s1kd-metadata: ERROR: ";

    private const int KeyColumnWidth = 31;

    // EXIT_* codes from the C tool.
    private const int ExitSuccess = 0;
    private const int ExitInvalidMetadata = 1;
    private const int ExitInvalidValue = 2;
    private const int ExitNoWrite = 3;
    private const int ExitMissingMetadata = 4;
    private const int ExitNoEdit = 5;
    private const int ExitInvalidCreate = 6;
    private const int ExitNoFile = 7;
    private const int ExitConditionUnmet = 8;

    // endl sentinel: the C uses int with -1 meaning "no delimiter" (-F mode).
    private const int NoEndl = -1;

    private sealed class KeyReq
    {
        public string Name = "";
        public string? Value;
    }

    private sealed class Cond
    {
        public string Key = "";
        public string Op = "=";
        public string? Val;
        public bool Regex;
    }

    private sealed class Opts
    {
        public List<KeyReq> Keys = new();
        public List<Cond> Conds = new();
        public int Endl = '\n';
        public string? ExecStr;
        public string? FmtStr;
        public bool FormatAll = true;
        public string? MetadataFname; // -c
        public bool OnlyEditable;
        public bool Overwrite;
        public string TimeFmt = Metadata.DefaultDateFormat;
        public bool Quiet;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var opts = new Opts();
        var files = new List<string>();
        bool listKeys = false;
        bool isList = false;

        // Tracks whether the most recent -n / -w / -W set the keys or conds list,
        // so that -v / -m attach to the correct one (mirrors the C `last`).
        int last = 0; // 0 = none, 1 = keys, 2 = conds

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];

            string? Arg()
            {
                if (++i >= args.Count)
                {
                    return null;
                }
                return args[i];
            }

            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return ExitSuccess;
                case "--version":
                    stdout.WriteLine($"s1kd-metadata (s1kd-tools) {Version}");
                    return ExitSuccess;
                case "-0" or "--null":
                    opts.Endl = '\0';
                    break;
                case "-c" or "--set":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    opts.MetadataFname = v;
                    break;
                }
                case "-d" or "--date-format":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    opts.TimeFmt = v;
                    break;
                }
                case "-E" or "--editable":
                    opts.OnlyEditable = true;
                    break;
                case "-e" or "--exec":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    opts.ExecStr = v;
                    break;
                }
                case "-F" or "--format":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    opts.FmtStr = v;
                    opts.Endl = NoEndl;
                    break;
                }
                case "-f" or "--overwrite":
                    opts.Overwrite = true;
                    break;
                case "-H" or "--info":
                    listKeys = true;
                    break;
                case "-l" or "--list":
                    isList = true;
                    break;
                case "-m" or "--matches":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    if (last == 2 && opts.Conds.Count > 0)
                    {
                        Cond c = opts.Conds[^1];
                        c.Regex = true;
                        c.Val = v;
                    }
                    break;
                }
                case "-n" or "--name":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    opts.Keys.Add(new KeyReq { Name = v });
                    last = 1;
                    break;
                }
                case "-T" or "--raw":
                    opts.FormatAll = false;
                    break;
                case "-t" or "--tab":
                    opts.Endl = '\t';
                    break;
                case "-v" or "--value":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    if (last == 1 && opts.Keys.Count > 0)
                    {
                        opts.Keys[^1].Value = v;
                    }
                    else if (last == 2 && opts.Conds.Count > 0)
                    {
                        Cond c = opts.Conds[^1];
                        c.Regex = false;
                        c.Val = v;
                    }
                    break;
                }
                case "-q" or "--quiet":
                    opts.Quiet = true;
                    break;
                case "-w" or "--where":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    opts.Conds.Add(new Cond { Key = v, Op = "=" });
                    last = 2;
                    break;
                }
                case "-W" or "--where-not":
                {
                    string? v = Arg();
                    if (v == null) { return ArgError(stderr, a); }
                    opts.Conds.Add(new Cond { Key = v, Op = "~" });
                    last = 2;
                    break;
                }
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        ShowHelp(stdout);
                        return ExitSuccess;
                    }
                    files.Add(a);
                    break;
            }
        }

        if (listKeys)
        {
            ListMetadataKeys(opts, stdout);
            return ExitSuccess;
        }

        int err = 0;
        if (files.Count > 0)
        {
            foreach (string f in files)
            {
                err += isList
                    ? ShowOrEditMetadataList(f, opts, stdout, stderr)
                    : ShowOrEditMetadata(f, opts, stdout, stderr);
            }
        }
        else if (isList)
        {
            err = ShowOrEditMetadataList(null, opts, stdout, stderr);
        }
        else
        {
            err = ShowOrEditMetadata("-", opts, stdout, stderr);
        }

        return err;
    }

    private int ArgError(TextWriter stderr, string opt)
    {
        stderr.WriteLine($"{ErrPrefix}{opt} requires an argument");
        return ExitInvalidValue;
    }

    private int ShowOrEditMetadataList(string? fname, Opts opts, TextWriter stdout, TextWriter stderr)
    {
        TextReader reader;
        if (fname != null)
        {
            try
            {
                reader = new StreamReader(File.OpenRead(fname));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"{ErrPrefix}Could not read list file '{fname}'.");
                return ExitNoFile;
            }
        }
        else
        {
            reader = new StreamReader(Console.OpenStandardInput());
        }

        int err = 0;
        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // strtok(path, "\t\r\n"): take the first field up to a tab/CR/LF.
                int cut = line.IndexOfAny(new[] { '\t', '\r' });
                string path = cut >= 0 ? line[..cut] : line;
                if (path.Length == 0)
                {
                    continue;
                }
                err += ShowOrEditMetadata(path, opts, stdout, stderr);
            }
        }
        finally
        {
            if (fname != null)
            {
                reader.Dispose();
            }
        }

        return err;
    }

    private int ShowOrEditMetadata(string fname, Opts opts, TextWriter stdout, TextWriter stderr)
    {
        string bname = Path.GetFileName(fname);
        bool isIcn = bname.StartsWith("ICN-", StringComparison.Ordinal);

        XmlDocument doc;
        try
        {
            doc = fname == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(fname);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            // ICN metadata is derived from the file name; the C read_xml_doc may
            // return NULL for a non-XML ICN file without failing. Mirror that by
            // continuing with an empty document.
            if (isIcn)
            {
                doc = XmlUtils.NewDocument();
            }
            else
            {
                stderr.WriteLine($"{ErrPrefix}Could not read {fname}: {ex.Message}");
                return ExitNoFile;
            }
        }

        int err = 0;
        bool edit = false;

        // Evaluate -w / -W conditions.
        foreach (Cond cond in opts.Conds)
        {
            if (!ConditionMet(doc, cond, fname, bname, isIcn, opts))
            {
                err = ExitConditionUnmet;
            }
        }

        if (err == 0)
        {
            if (opts.ExecStr != null)
            {
                err = ExecFile(opts.ExecStr, fname, stderr);
            }
            else if (opts.FmtStr != null)
            {
                err = ShowMetadataFmtStr(opts.FmtStr, fname, bname, isIcn, doc, opts, stdout, stderr);
            }
            else if (opts.Keys.Count > 0)
            {
                foreach (KeyReq kr in opts.Keys)
                {
                    string key = kr.Name;
                    string? val = kr.Value;

                    if (val != null)
                    {
                        edit = true;
                        err = EditMetadata(doc, key, val);
                    }
                    else if (key == "path")
                    {
                        stdout.Write(fname);
                        err = 0;
                    }
                    else if (key == "format")
                    {
                        stdout.Write(GetFormat(bname));
                        err = 0;
                    }
                    else if (key == "modified")
                    {
                        stdout.Write(GetModTime(fname, opts.TimeFmt));
                        err = 0;
                    }
                    else if (isIcn)
                    {
                        err = ShowIcnMetadata(bname, key, opts, stdout);
                    }
                    else
                    {
                        err = ShowMetadata(doc, key, opts, stdout);
                    }

                    if (!edit)
                    {
                        WriteEndl(stdout, opts.Endl);
                    }

                    ShowErr(err, key, val, fname, opts, stderr);
                }
            }
            else if (opts.MetadataFname != null)
            {
                edit = true;
                err = EditAllMetadata(opts.MetadataFname, doc, stderr);
            }
            else if (isIcn)
            {
                err = ShowAllIcnMetadata(bname, opts, stdout);
            }
            else
            {
                err = ShowAllMetadata(doc, opts, stdout);
            }
        }

        if (edit && err == 0)
        {
            if (opts.Overwrite && fname != "-")
            {
                if (!File.Exists(fname) || HasWriteAccess(fname))
                {
                    XmlUtils.SaveDoc(doc, fname);
                }
                else
                {
                    stderr.WriteLine($"{ErrPrefix}{fname} does not have write permission.");
                    return ExitNoWrite;
                }
            }
            else
            {
                stdout.Write(XmlUtils.ToXmlString(doc));
                stdout.Write('\n');
            }
        }
        else if (opts.Endl != '\n' && err != ExitConditionUnmet)
        {
            stdout.Write('\n');
        }

        return err;
    }

    private static bool HasWriteAccess(string fname)
    {
        try
        {
            using var fs = new FileStream(fname, FileMode.Open, FileAccess.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteEndl(TextWriter stdout, int endl)
    {
        if (endl > NoEndl)
        {
            stdout.Write((char)endl);
        }
    }

    // ----- show / edit dispatch -----

    private int ShowMetadata(XmlDocument doc, string key, Opts opts, TextWriter stdout)
    {
        if (!Metadata.IsKnown(key))
        {
            WriteEndl(stdout, opts.Endl);
            return ExitInvalidMetadata;
        }
        if (!Metadata.HasNode(doc, key))
        {
            WriteEndl(stdout, opts.Endl);
            return ExitMissingMetadata;
        }
        string? v = Metadata.Get(doc, key, opts.TimeFmt);
        if (v != null)
        {
            stdout.Write(v);
        }
        return ExitSuccess;
    }

    private static int EditMetadata(XmlDocument doc, string key, string val)
    {
        if (!Metadata.IsKnown(key))
        {
            return ExitInvalidMetadata;
        }
        // Distinguish missing-node-no-create (NO_EDIT) from invalid value, to
        // mirror the C edit_metadata / edit_* return codes. The library Set
        // returns a bool, so we re-derive the precise code here.
        bool present = Metadata.HasNode(doc, key);
        bool ok = Metadata.Set(doc, key, val);
        if (ok)
        {
            return ExitSuccess;
        }
        if (!present)
        {
            return ExitNoEdit; // no node and no create handler (or create failed)
        }
        // Node present but edit failed: INVALID_VALUE for validated code/date
        // keys, otherwise NO_EDIT (no edit handler).
        return IsValueValidatedKey(key) ? ExitInvalidValue : ExitNoEdit;
    }

    private static bool IsValueValidatedKey(string key) => key is
        "act" or "brex" or "code" or "dmCode" or "pmCode" or "issueDate" or
        "issue" or "sourceDmCode" or "sourcePmCode";

    private int ShowIcnMetadata(string bname, string key, Opts opts, TextWriter stdout)
    {
        string? v = Metadata.GetIcn(bname, key);
        if (v != null)
        {
            stdout.Write(v);
            return ExitSuccess;
        }
        WriteEndl(stdout, opts.Endl);
        return ExitInvalidMetadata;
    }

    private int ShowAllMetadata(XmlDocument doc, Opts opts, TextWriter stdout)
    {
        foreach (MetadataKey key in Metadata.Keys)
        {
            if (opts.OnlyEditable && !key.Editable)
            {
                continue;
            }
            if (!Metadata.HasNode(doc, key.Name))
            {
                continue;
            }

            if (opts.Endl == '\n')
            {
                stdout.Write(key.Name);
                if (opts.FormatAll)
                {
                    int n = KeyColumnWidth - key.Name.Length;
                    for (int j = 0; j < n; j++)
                    {
                        stdout.Write(' ');
                    }
                }
                else
                {
                    stdout.Write('\t');
                }
            }

            string? v = Metadata.Get(doc, key.Name, opts.TimeFmt);
            if (v != null)
            {
                stdout.Write(v);
            }
            WriteEndl(stdout, opts.Endl);
        }
        return ExitSuccess;
    }

    private int ShowAllIcnMetadata(string bname, Opts opts, TextWriter stdout)
    {
        foreach (string key in Metadata.IcnKeys)
        {
            if (opts.Endl == '\n')
            {
                stdout.Write(key);
                if (opts.FormatAll)
                {
                    int n = KeyColumnWidth - key.Length;
                    for (int j = 0; j < n; j++)
                    {
                        stdout.Write(' ');
                    }
                }
                else
                {
                    stdout.Write('\t');
                }
            }
            string? v = Metadata.GetIcn(bname, key);
            if (v != null)
            {
                stdout.Write(v);
            }
            WriteEndl(stdout, opts.Endl);
        }
        return ExitSuccess;
    }

    private int EditAllMetadata(string metadataFname, XmlDocument doc, TextWriter stderr)
    {
        TextReader reader;
        if (metadataFname == "-")
        {
            reader = new StreamReader(Console.OpenStandardInput());
        }
        else
        {
            try
            {
                reader = new StreamReader(File.OpenRead(metadataFname));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"{ErrPrefix}Could not read {metadataFname}: {ex.Message}");
                return ExitNoFile;
            }
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // fscanf("%255s %255[^\n]", key, val): first whitespace-delimited
                // token is the key, the rest of the line is the value. Lines
                // without a value are ignored (scanf returns < 2).
                line = line.TrimStart();
                int sp = IndexOfWhitespace(line);
                if (sp < 0)
                {
                    continue;
                }
                string key = line[..sp];
                string rest = line[(sp + 1)..].TrimStart();
                if (rest.Length == 0)
                {
                    continue;
                }
                EditMetadata(doc, key, rest);
            }
        }
        finally
        {
            if (metadataFname != "-")
            {
                reader.Dispose();
            }
        }

        return ExitSuccess;
    }

    private static int IndexOfWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                return i;
            }
        }
        return -1;
    }

    // ----- conditions (-w / -W / -v / -m) -----

    private bool ConditionMet(XmlDocument doc, Cond cond, string fname, string bname, bool isIcn, Opts opts)
    {
        string? content;
        if (isIcn)
        {
            if (!Metadata.IcnKeys.Contains(cond.Key))
            {
                return false;
            }
            content = Metadata.GetIcn(bname, cond.Key);
        }
        else
        {
            if (cond.Key == "path")
            {
                content = fname;
            }
            else if (cond.Key == "format")
            {
                content = GetFormat(bname);
            }
            else if (cond.Key == "modified")
            {
                content = GetModTime(fname, opts.TimeFmt);
            }
            else if (!Metadata.IsKnown(cond.Key))
            {
                return false;
            }
            else
            {
                content = Metadata.GetConditionContent(doc, cond.Key, opts.TimeFmt);
            }
        }

        return ConditionCmp(cond.Op, content, cond.Val, cond.Regex);
    }

    private static bool ConditionCmp(string op, string? content, string? val, bool regex)
    {
        if (regex)
        {
            return op[0] switch
            {
                '=' => val == null ? content != null : content != null && MatchPattern(content, val),
                '~' => val == null ? content == null : !(content != null && MatchPattern(content, val)),
                _ => false,
            };
        }
        return op[0] switch
        {
            '=' => val == null ? content != null : string.Equals(content, val, StringComparison.Ordinal),
            '~' => val == null ? content == null : !string.Equals(content, val, StringComparison.Ordinal),
            _ => false,
        };
    }

    // Mirrors common/match_pattern: libxml2 xmlRegexp anchors the whole string.
    private static bool MatchPattern(string value, string pattern)
    {
        try
        {
            var re = new Regex("^(?:" + pattern + ")$");
            return re.IsMatch(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ----- format string (-F) -----

    private int ShowMetadataFmtStr(string fmt, string fname, string bname, bool isIcn,
        XmlDocument doc, Opts opts, TextWriter stdout, TextWriter stderr)
    {
        for (int i = 0; i < fmt.Length; i++)
        {
            char ch = fmt[i];
            if (ch == '%')
            {
                if (i + 1 < fmt.Length && fmt[i + 1] == '%')
                {
                    stdout.Write('%');
                    i++;
                }
                else
                {
                    int e = fmt.IndexOf('%', i + 1);
                    if (e < 0)
                    {
                        break;
                    }
                    string key = fmt[(i + 1)..e];

                    if (key == "path")
                    {
                        stdout.Write(fname);
                    }
                    else if (key == "format")
                    {
                        stdout.Write(GetFormat(bname));
                    }
                    else if (key == "modified")
                    {
                        stdout.Write(GetModTime(fname, opts.TimeFmt));
                    }
                    else if (isIcn)
                    {
                        ShowIcnFmtKey(bname, key, opts, stdout, stderr);
                    }
                    else
                    {
                        ShowFmtKey(doc, key, opts, stdout, stderr);
                    }

                    i = e;
                }
            }
            else if (ch == '\\')
            {
                if (i + 1 < fmt.Length)
                {
                    switch (fmt[i + 1])
                    {
                        case 'n': stdout.Write('\n'); i++; break;
                        case 't': stdout.Write('\t'); i++; break;
                        case '0': stdout.Write('\0'); i++; break;
                        default: stdout.Write(ch); break;
                    }
                }
                else
                {
                    stdout.Write(ch);
                }
            }
            else
            {
                stdout.Write(ch);
            }
        }
        return ExitSuccess;
    }

    private void ShowFmtKey(XmlDocument doc, string key, Opts opts, TextWriter stdout, TextWriter stderr)
    {
        if (!Metadata.IsKnown(key))
        {
            ShowErr(ExitInvalidMetadata, key, null, null, opts, stderr);
            return;
        }
        if (!Metadata.HasNode(doc, key))
        {
            ShowErr(ExitMissingMetadata, key, null, null, opts, stderr);
            return;
        }
        string? v = Metadata.Get(doc, key, opts.TimeFmt);
        if (v != null)
        {
            stdout.Write(v);
        }
    }

    private void ShowIcnFmtKey(string bname, string key, Opts opts, TextWriter stdout, TextWriter stderr)
    {
        string? v = Metadata.GetIcn(bname, key);
        if (v != null)
        {
            stdout.Write(v);
        }
        else
        {
            ShowErr(ExitInvalidMetadata, key, null, null, opts, stderr);
        }
    }

    // ----- file-derived metadata -----

    private static string GetFormat(string bname)
    {
        int dot = bname.IndexOf('.');
        return dot >= 0 ? bname[(dot + 1)..] : string.Empty;
    }

    private static string GetModTime(string fname, string timeFmt)
    {
        try
        {
            DateTime t = File.GetLastWriteTime(fname);
            return Metadata.Strftime(timeFmt, t);
        }
        catch
        {
            return string.Empty;
        }
    }

    // ----- exec (-e) -----

    private int ExecFile(string execStr, string fname, TextWriter stderr)
    {
        // execfile interpolates {} with the file name, then runs the command via
        // the shell. Replace the {} placeholder before invoking.
        string cmd = execStr.Replace("{}", fname);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
            psi.ArgumentList.Add(cmd);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                return 1;
            }
            proc.WaitForExit();
            return proc.ExitCode != 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"{ErrPrefix}Could not execute command: {ex.Message}");
            return 1;
        }
    }

    // ----- error reporting -----

    private void ShowErr(int err, string key, string? val, string? fname, Opts opts, TextWriter stderr)
    {
        if (opts.Quiet)
        {
            return;
        }
        switch (err)
        {
            case ExitInvalidMetadata:
                stderr.WriteLine(val != null
                    ? $"{ErrPrefix}Cannot edit metadata: {key}"
                    : $"{ErrPrefix}Invalid metadata name: {key}");
                break;
            case ExitInvalidValue:
                stderr.WriteLine($"{ErrPrefix}Invalid value for {key}: {val}");
                break;
            case ExitMissingMetadata:
                stderr.WriteLine($"{ErrPrefix}Data has no metadata: {key}");
                break;
            case ExitNoEdit:
                stderr.WriteLine($"{ErrPrefix}Cannot edit metadata: {key}");
                break;
            case ExitInvalidCreate:
                stderr.WriteLine($"{ErrPrefix}{key} is not valid metadata for {fname}");
                break;
        }
    }

    // ----- key listing (-H) -----

    private void ListMetadataKeys(Opts opts, TextWriter stdout)
    {
        foreach (MetadataKey key in Metadata.Keys)
        {
            // has_key: when -n keys are given, restrict to those.
            if (opts.Keys.Count > 0 && !opts.Keys.Any(k => k.Name == key.Name))
            {
                continue;
            }
            if (opts.OnlyEditable && !key.Editable)
            {
                continue;
            }
            ListMetadataKey(key.Name, key.Description, opts, stdout);
        }
    }

    private void ListMetadataKey(string key, string descr, Opts opts, TextWriter stdout)
    {
        int n = KeyColumnWidth - key.Length;
        stdout.Write(key);
        if (opts.FormatAll)
        {
            for (int j = 0; j < n; j++)
            {
                stdout.Write(' ');
            }
        }
        else
        {
            stdout.Write('\t');
        }
        stdout.Write(descr);
        stdout.Write('\n');
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: s1kd-metadata [options] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -0, --null               Use null-delimited fields.");
        stdout.WriteLine("  -c, --set <file>         Set metadata using definitions in <file> (- for stdin).");
        stdout.WriteLine("  -d, --date-format <fmt>  Format to use for printing dates.");
        stdout.WriteLine("  -E, --editable           Include only editable metadata when showing all.");
        stdout.WriteLine("  -e, --exec <cmd>         Execute <cmd> for each CSDB object.");
        stdout.WriteLine("  -F, --format <fmt>       Print a formatted line for each CSDB object.");
        stdout.WriteLine("  -f, --overwrite          Overwrite modules when editing metadata.");
        stdout.WriteLine("  -H, --info               List information on available metadata.");
        stdout.WriteLine("  -l, --list               Input is a list of filenames.");
        stdout.WriteLine("  -m, --matches <regex>    Use a pattern instead of a literal value (-v) with -w/-W.");
        stdout.WriteLine("  -n, --name <name>        Specific metadata name to view/edit.");
        stdout.WriteLine("  -q, --quiet              Quiet mode, do not show non-fatal errors.");
        stdout.WriteLine("  -T, --raw                Do not format columns in output.");
        stdout.WriteLine("  -t, --tab                Use tab-delimited fields.");
        stdout.WriteLine("  -v, --value <value>      The value to set or match.");
        stdout.WriteLine("  -W, --where-not <name>   Only list/edit objects where metadata <name> does not equal a value.");
        stdout.WriteLine("  -w, --where <name>       Only list/edit objects where metadata <name> equals a value.");
        stdout.WriteLine("      --version            Show version information.");
        stdout.WriteLine("  <object>                 CSDB object(s) to view/edit metadata on.");
    }
}
