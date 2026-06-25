using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-metadata</c>: view and edit S1000D metadata on CSDB objects.
/// Implements the core option set (-n/-v/-H/-E/-T/-t/-0/-f and file arguments);
/// the remaining flags (-w/-W/-F/-e/-c/-l/-m/-d) are tracked in todo.md.
/// </summary>
public sealed class MetadataTool : ITool
{
    public string Name => "metadata";
    public string Description => "View and edit S1000D metadata on CSDB objects.";
    public string Version => "4.7.0";

    private const int KeyColumnWidth = 31;

    private const int ExitMissingMetadata = 4;
    private const int ExitNoWrite = 3;
    private const int ExitNoFile = 7;

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var names = new List<string>();
        var files = new List<string>();
        string? value = null;
        bool listKeys = false;
        bool editableOnly = false;
        bool formatColumns = true;
        bool overwrite = false;
        char endl = '\n';

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "--help":
                    ShowHelp(stdout);
                    return 0;
                case "--version":
                    stdout.WriteLine($"{Name} ({Version})");
                    return 0;
                case "-H" or "--info":
                    listKeys = true;
                    break;
                case "-E" or "--editable":
                    editableOnly = true;
                    break;
                case "-T" or "--raw":
                    formatColumns = false;
                    break;
                case "-t" or "--tab":
                    endl = '\t';
                    break;
                case "-0" or "--null":
                    endl = '\0';
                    break;
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-n" or "--name":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -n requires an argument"); return 2; }
                    names.Add(args[i]);
                    break;
                case "-v" or "--value":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -v requires an argument"); return 2; }
                    value = args[i];
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

        if (listKeys)
        {
            ListKeys(stdout, names, editableOnly, formatColumns);
            return 0;
        }

        if (files.Count == 0)
        {
            files.Add("-"); // read from stdin
        }

        int status = 0;
        foreach (string file in files)
        {
            int r = ProcessFile(file, names, value, editableOnly, formatColumns, overwrite, endl, stdout, stderr);
            if (r != 0)
            {
                status = r;
            }
        }
        return status;
    }

    private int ProcessFile(string file, List<string> names, string? value, bool editableOnly,
        bool formatColumns, bool overwrite, char endl, TextWriter stdout, TextWriter stderr)
    {
        XmlDocument doc;
        try
        {
            doc = file == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(file);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            stderr.WriteLine($"{Name}: ERROR: Could not read {file}: {ex.Message}");
            return ExitNoFile;
        }

        bool editing = value != null && names.Count > 0;

        if (editing)
        {
            foreach (string key in names)
            {
                if (!Metadata.Set(doc, key, value!))
                {
                    stderr.WriteLine($"{Name}: ERROR: Could not set {key} on {file}");
                }
            }

            if (overwrite && file != "-")
            {
                try
                {
                    XmlUtils.SaveDoc(doc, file);
                }
                catch (IOException)
                {
                    stderr.WriteLine($"{Name}: ERROR: {file} does not have write permission.");
                    return ExitNoWrite;
                }
            }
            else
            {
                stdout.Write(XmlUtils.ToXmlString(doc));
                stdout.Write('\n');
            }
            return 0;
        }

        if (names.Count > 0)
        {
            int status = 0;
            foreach (string key in names)
            {
                string? v = Metadata.Get(doc, key);
                if (v == null)
                {
                    if (endl > 0) stdout.Write(endl);
                    status = ExitMissingMetadata;
                    continue;
                }
                stdout.Write(v);
                if (endl > 0) stdout.Write(endl);
            }
            return status;
        }

        ShowAll(doc, stdout, editableOnly, formatColumns, endl);
        return 0;
    }

    private static void ShowAll(XmlDocument doc, TextWriter stdout, bool editableOnly, bool formatColumns, char endl)
    {
        foreach (var key in Metadata.Keys)
        {
            if (editableOnly && !key.Editable)
            {
                continue;
            }
            string? v = Metadata.Get(doc, key.Name);
            if (v == null)
            {
                continue;
            }

            if (endl == '\n')
            {
                stdout.Write(key.Name);
                if (formatColumns)
                {
                    stdout.Write(new string(' ', Math.Max(1, KeyColumnWidth - key.Name.Length)));
                }
                else
                {
                    stdout.Write('\t');
                }
            }
            stdout.Write(v);
            if (endl > 0) stdout.Write(endl);
        }
    }

    private static void ListKeys(TextWriter stdout, List<string> names, bool editableOnly, bool formatColumns)
    {
        foreach (var key in Metadata.Keys)
        {
            if (names.Count > 0 && !names.Contains(key.Name))
            {
                continue;
            }
            if (editableOnly && !key.Editable)
            {
                continue;
            }
            stdout.Write(key.Name);
            if (formatColumns)
            {
                stdout.Write(new string(' ', Math.Max(1, KeyColumnWidth - key.Name.Length)));
            }
            else
            {
                stdout.Write('\t');
            }
            stdout.WriteLine(key.Description);
        }
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -0, --null            Use null-delimited fields.");
        stdout.WriteLine("  -E, --editable        Include only editable metadata when showing all.");
        stdout.WriteLine("  -f, --overwrite       Overwrite modules when editing metadata.");
        stdout.WriteLine("  -H, --info            List information on available metadata.");
        stdout.WriteLine("  -n, --name <name>     Specific metadata name to view/edit.");
        stdout.WriteLine("  -T, --raw             Do not format columns in output.");
        stdout.WriteLine("  -t, --tab             Use tab-delimited fields.");
        stdout.WriteLine("  -v, --value <value>   The value to set.");
        stdout.WriteLine("      --version         Show version information.");
        stdout.WriteLine("  <object>              CSDB object(s) to view/edit metadata on.");
    }
}
