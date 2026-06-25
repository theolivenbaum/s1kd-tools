namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-ls</c>: list CSDB objects in a directory tree. Supports type
/// selection, official/inwork and latest/old filtering, recursion, list input,
/// writable/read-only filtering and null-delimited output. The <c>-e/--exec</c>
/// option and the <c>-N/--omit-issue</c> file-content inwork lookup are tracked
/// in todo.md.
/// </summary>
public sealed class LsTool : ITool
{
    public string Name => "ls";
    public string Description => "List CSDB objects in a directory.";
    public string Version => "1.16.0";

    [Flags]
    private enum Show
    {
        None = 0,
        Dm = 0x001, Pm = 0x002, Com = 0x004, Imf = 0x008, Ddn = 0x010,
        Dml = 0x020, Icn = 0x040, Smc = 0x080, Upf = 0x100, Non = 0x200,
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var show = Show.None;
        bool onlyLatest = false, onlyOld = false, onlyOfficial = false, onlyInwork = false;
        bool onlyWritable = false, onlyReadonly = false, recursive = false, listInput = false;
        char sep = '\n';
        var inputs = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help": ShowHelp(stdout); return 0;
                case "--version": stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}"); return 0;
                case "-0" or "--null": sep = '\0'; break;
                case "-C" or "--com": show |= Show.Com; break;
                case "-D" or "--dm": show |= Show.Dm; break;
                case "-G" or "--icn": show |= Show.Icn; break;
                case "-L" or "--dml": show |= Show.Dml; break;
                case "-M" or "--imf": show |= Show.Imf; break;
                case "-P" or "--pm": show |= Show.Pm; break;
                case "-S" or "--smc": show |= Show.Smc; break;
                case "-U" or "--upf": show |= Show.Upf; break;
                case "-X" or "--ddn": show |= Show.Ddn; break;
                case "-n" or "--other": show |= Show.Non; break;
                case "-i" or "--official": onlyOfficial = true; break;
                case "-I" or "--inwork": onlyInwork = true; break;
                case "-l" or "--latest": onlyLatest = true; break;
                case "-o" or "--old": onlyOld = true; break;
                case "-w" or "--writable": onlyWritable = true; break;
                case "-R" or "--read-only": onlyReadonly = true; break;
                case "-r" or "--recursive": recursive = true; break;
                case "-7" or "--list": listInput = true; break;
                default:
                    if (a.StartsWith('-') && a.Length > 1)
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return 2;
                    }
                    inputs.Add(a);
                    break;
            }
        }

        if (show == Show.None)
        {
            show = Show.Dm | Show.Pm | Show.Com | Show.Icn | Show.Imf |
                   Show.Ddn | Show.Dml | Show.Smc | Show.Upf;
        }

        var buckets = new Dictionary<Show, List<string>>();
        foreach (Show t in new[] { Show.Dm, Show.Pm, Show.Com, Show.Imf, Show.Ddn, Show.Dml, Show.Icn, Show.Smc, Show.Upf, Show.Non })
        {
            if (show.HasFlag(t))
            {
                buckets[t] = new List<string>();
            }
        }

        // Collect inputs.
        if (inputs.Count > 0)
        {
            foreach (string input in inputs)
            {
                if (listInput)
                {
                    ReadList(input, buckets, onlyWritable, onlyReadonly, recursive);
                }
                else
                {
                    ListPath(input, buckets, onlyWritable, onlyReadonly, recursive);
                }
            }
        }
        else if (listInput)
        {
            ReadList(null, buckets, onlyWritable, onlyReadonly, recursive);
        }
        else
        {
            ListDir(".", buckets, onlyWritable, onlyReadonly, recursive);
        }

        // Sort.
        foreach (var (type, list) in buckets)
        {
            list.Sort(type == Show.Icn ? CompareIcn : Csdb.CompareBaseName);
        }

        // Print in the canonical order, applying filters per type.
        var printOrder = new[] { Show.Com, Show.Ddn, Show.Dm, Show.Dml, Show.Icn, Show.Imf, Show.Pm, Show.Smc, Show.Upf, Show.Non };

        foreach (Show type in printOrder)
        {
            if (!buckets.TryGetValue(type, out var list))
            {
                continue;
            }

            // Comments and DDNs have no issue/inwork; they are omitted with -o.
            if (type is Show.Com or Show.Ddn or Show.Non)
            {
                if (type != Show.Non && onlyOld)
                {
                    continue;
                }
                Print(list, stdout, sep);
                continue;
            }

            // ICNs cannot be filtered by official/inwork.
            if (type == Show.Icn)
            {
                if (onlyInwork)
                {
                    continue; // ICNs have no inwork notion in the C tool's output
                }
                var icns = ApplyLatestOld(list, onlyLatest, onlyOld, isIcn: true);
                Print(icns, stdout, sep);
                continue;
            }

            IEnumerable<string> result = list;
            if (onlyOfficial)
            {
                result = result.Where(Csdb.IsOfficialIssue);
            }
            else if (onlyInwork)
            {
                result = result.Where(f => !Csdb.IsOfficialIssue(f));
            }

            var filtered = ApplyLatestOld(result.ToList(), onlyLatest, onlyOld, isIcn: false);
            Print(filtered, stdout, sep);
        }

        return 0;
    }

    private static List<string> ApplyLatestOld(List<string> list, bool latest, bool old, bool isIcn)
    {
        if (latest)
        {
            return isIcn ? ExtractLatestIcns(list) : Csdb.ExtractLatestObjects(list);
        }
        if (old)
        {
            return isIcn ? RemoveLatestIcns(list) : Csdb.RemoveLatestObjects(list);
        }
        return list;
    }

    private static void Print(IEnumerable<string> files, TextWriter stdout, char sep)
    {
        foreach (string f in files)
        {
            stdout.Write(f);
            stdout.Write(sep);
        }
    }

    private static bool IsNon(string baseName) =>
        !(baseName.StartsWith('.') || Csdb.IsComment(baseName) || Csdb.IsDataDispatchNote(baseName) ||
          Csdb.IsDataModule(baseName) || Csdb.IsDataManagementList(baseName) || Csdb.IsIcn(baseName) ||
          Csdb.IsIcnMetadataFile(baseName) || Csdb.IsPublicationModule(baseName) ||
          Csdb.IsScormContentPackage(baseName) || Csdb.IsDataUpdateFile(baseName));

    private static Show? Classify(string baseName) => baseName switch
    {
        _ when Csdb.IsDataModule(baseName) => Show.Dm,
        _ when Csdb.IsPublicationModule(baseName) => Show.Pm,
        _ when Csdb.IsComment(baseName) => Show.Com,
        _ when Csdb.IsIcnMetadataFile(baseName) => Show.Imf,
        _ when Csdb.IsIcn(baseName) => Show.Icn,
        _ when Csdb.IsDataDispatchNote(baseName) => Show.Ddn,
        _ when Csdb.IsDataManagementList(baseName) => Show.Dml,
        _ when Csdb.IsScormContentPackage(baseName) => Show.Smc,
        _ when Csdb.IsDataUpdateFile(baseName) => Show.Upf,
        _ => null,
    };

    private static bool PassesAccess(string path, bool onlyWritable, bool onlyReadonly)
    {
        bool writable = IsWritable(path);
        if (onlyWritable && !writable) return false;
        if (onlyReadonly && writable) return false;
        return true;
    }

    private static bool IsWritable(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.ReadOnly) == 0;
        }
        catch
        {
            return false;
        }
    }

    private void ListDir(string path, Dictionary<Show, List<string>> buckets,
        bool onlyWritable, bool onlyReadonly, bool recursive)
    {
        string prefix = path == "." ? "" : path.EndsWith('/') ? path : path + "/";

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(path);
        }
        catch
        {
            return;
        }

        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            string cpath = prefix + name;

            if (!PassesAccess(cpath, onlyWritable, onlyReadonly))
            {
                continue;
            }

            Show? type = Classify(name);
            if (type.HasValue && buckets.TryGetValue(type.Value, out var list))
            {
                list.Add(cpath);
            }
            else if (recursive && Directory.Exists(cpath) && name is not ("." or ".."))
            {
                ListDir(cpath, buckets, onlyWritable, onlyReadonly, recursive);
            }
            else if (buckets.TryGetValue(Show.Non, out var nons) && IsNon(name))
            {
                nons.Add(cpath);
            }
        }
    }

    private static void ListPath(string path, Dictionary<Show, List<string>> buckets,
        bool onlyWritable, bool onlyReadonly, bool recursive)
    {
        if (Directory.Exists(path))
        {
            new LsTool().ListDir(path, buckets, onlyWritable, onlyReadonly, recursive);
            return;
        }
        if (!File.Exists(path) || !PassesAccess(path, onlyWritable, onlyReadonly))
        {
            return;
        }
        string baseName = Path.GetFileName(path);
        Show? type = Classify(baseName);
        if (type.HasValue && buckets.TryGetValue(type.Value, out var list))
        {
            list.Add(path);
        }
        else if (buckets.TryGetValue(Show.Non, out var nons) && IsNon(baseName))
        {
            nons.Add(path);
        }
    }

    private static void ReadList(string? path, Dictionary<Show, List<string>> buckets,
        bool onlyWritable, bool onlyReadonly, bool recursive)
    {
        using TextReader reader = path == null ? Console.In : new StreamReader(path);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Split('\t')[0].TrimEnd('\r', '\n');
            if (trimmed.Length > 0)
            {
                ListPath(trimmed, buckets, onlyWritable, onlyReadonly, recursive);
            }
        }
    }

    // ----- ICN-specific helpers (grouped by file extension) -----

    private static string IcnSortKey(string path)
    {
        string b = Path.GetFileName(path);
        int dot = b.IndexOf('.');
        return dot < 0 ? b : b[dot..] + b[..dot];
    }

    private static int CompareIcn(string a, string b) =>
        string.Compare(IcnSortKey(a), IcnSortKey(b), StringComparison.OrdinalIgnoreCase);

    // Group ICNs by code (basename up to the issue field) and extension.
    private static (string code, string ext) IcnParts(string path)
    {
        string b = Path.GetFileName(path);
        int dash = b.LastIndexOf('-');
        if (dash < 0)
        {
            return (b, "");
        }
        // The C compares strncmp(base, base, n-3) for the code (dropping the
        // 2-digit issue + separator) and the suffix from the last '-' on.
        string code = dash - 3 >= 0 ? b[..(dash - 3)] : b[..dash];
        string ext = b[dash..];
        return (code, ext);
    }

    private static List<string> ExtractLatestIcns(List<string> files)
    {
        var latest = new List<string>();
        for (int i = 0; i < files.Count; i++)
        {
            if (i == 0)
            {
                latest.Add(files[i]);
                continue;
            }
            var (c1, e1) = IcnParts(files[i]);
            var (c0, e0) = IcnParts(files[i - 1]);
            if (c1 != c0 || e1 != e0)
            {
                latest.Add(files[i]);
            }
            else
            {
                latest[^1] = files[i];
            }
        }
        return latest;
    }

    private static List<string> RemoveLatestIcns(List<string> files)
    {
        var old = new List<string>();
        for (int i = 0; i < files.Count - 1; i++)
        {
            var (c1, e1) = IcnParts(files[i]);
            var (c3, e3) = IcnParts(files[i + 1]);
            if (c1 == c3 && e1 == e3)
            {
                old.Add(files[i]);
            }
        }
        return old;
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-0CDGIiLlMNnoPRrSUwX7] [<object>|<dir> ...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -0, --null        Output null-delimited list.");
        stdout.WriteLine("  -C, --com         List comments.");
        stdout.WriteLine("  -D, --dm          List data modules.");
        stdout.WriteLine("  -G, --icn         List ICN files.");
        stdout.WriteLine("  -I, --inwork      Show only inwork issues.");
        stdout.WriteLine("  -i, --official    Show only official issues.");
        stdout.WriteLine("  -L, --dml         List DMLs.");
        stdout.WriteLine("  -l, --latest      Show only latest official/inwork issue.");
        stdout.WriteLine("  -M, --imf         List ICN metadata files.");
        stdout.WriteLine("  -n, --other       List non-S1000D files.");
        stdout.WriteLine("  -o, --old         Show only old official/inwork issues.");
        stdout.WriteLine("  -P, --pm          List publication modules.");
        stdout.WriteLine("  -R, --read-only   Show only non-writable object files.");
        stdout.WriteLine("  -r, --recursive   Recursively search directories.");
        stdout.WriteLine("  -S, --smc         List SCORM content packages.");
        stdout.WriteLine("  -U, --upf         List data update files.");
        stdout.WriteLine("  -w, --writable    Show only writable object files.");
        stdout.WriteLine("  -X, --ddn         List DDNs.");
        stdout.WriteLine("  -7, --list        Treat input as list of CSDB objects.");
        stdout.WriteLine("      --version     Show version information.");
    }
}
