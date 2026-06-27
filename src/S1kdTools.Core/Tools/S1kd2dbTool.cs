using S1kdTools.DocBook;

namespace S1kdTools.Tools;

/// <summary>
/// Converts S1000D data modules to DocBook 5, in-process, using the embedded
/// <c>s1kd2db</c> (default) or Smart Avionics <c>s1000dtodb</c> stylesheets via
/// <see cref="DocBookConverter"/>.
///
/// This is a companion converter rather than one of the numbered
/// <c>s1kd-*</c> tools, but it follows the same CLI conventions and is exposed
/// as <c>s1kd s1kd2db</c> (and, via multi-call dispatch, <c>s1kd-s1kd2db</c>),
/// mirroring the upstream <c>s1kd2db</c> shell script while adding a
/// stylesheet-profile switch.
/// </summary>
public sealed class S1kd2dbTool : ITool
{
    public string Name => "s1kd2db";
    public string Description => "Convert S1000D data modules to DocBook 5.";
    public string Version => "1.0.0";

    private const int ExitBadOption = 2;

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var profile = DocBookProfile.S1kd2db;
        string outPath = "-";
        string? entityMapPath = null;
        bool islist = false;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return 0;
                case "--version":
                    stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
                    return 0;
                case "-S" or "--smart":
                    profile = DocBookProfile.SmartAvionics;
                    break;
                case "-b" or "--basic":
                    profile = DocBookProfile.S1kd2db;
                    break;
                case "-l" or "--list":
                    islist = true;
                    break;
                case "-o" or "--out":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -o requires an argument"); return ExitBadOption; }
                    outPath = args[i];
                    break;
                case "-e" or "--entity-map":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -e requires an argument"); return ExitBadOption; }
                    entityMapPath = args[i];
                    break;
                case "-p" or "--param":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -p requires an argument"); return ExitBadOption; }
                    if (!AddParam(parameters, args[i]))
                    {
                        stderr.WriteLine($"{Name}: ERROR: -p expects name=value, got: {args[i]}");
                        return ExitBadOption;
                    }
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return ExitBadOption;
                    }
                    files.Add(a);
                    break;
            }
        }

        var inputs = new List<string>();
        if (islist)
        {
            foreach (string listFile in files.Count > 0 ? files : new List<string> { "-" })
                inputs.AddRange(ReadList(listFile));
        }
        else
        {
            inputs.AddRange(files.Count > 0 ? files : new List<string> { "-" });
        }

        if (outPath != "-" && inputs.Count > 1)
        {
            stderr.WriteLine($"{Name}: ERROR: -o cannot be used with multiple input files.");
            return ExitBadOption;
        }

        int status = 0;
        foreach (string input in inputs)
        {
            try
            {
                string docbook = Convert(input, profile, parameters, entityMapPath);
                if (outPath != "-")
                    File.WriteAllText(outPath, docbook);
                else
                    stdout.Write(docbook);
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException
                                          or System.Xml.Xsl.XsltException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"{Name}: ERROR: {input}: {ex.Message}");
                status = 1;
            }
        }

        return status;
    }

    private static string Convert(string input, DocBookProfile profile,
        IReadOnlyDictionary<string, string> parameters, string? entityMapPath)
    {
        if (input == "-")
        {
            string xml = Console.In.ReadToEnd();
            var map = entityMapPath != null
                ? EntityUriResolver.ReadInfoEntityMap(File.ReadAllText(entityMapPath))
                : null;
            return DocBookConverter.Convert(xml, profile, parameters, map);
        }
        return DocBookConverter.ConvertFile(input, profile, parameters, entityMapPath);
    }

    private static bool AddParam(Dictionary<string, string> parameters, string spec)
    {
        int eq = spec.IndexOf('=');
        if (eq <= 0) return false;
        parameters[spec[..eq].Trim()] = spec[(eq + 1)..];
        return true;
    }

    private static IEnumerable<string> ReadList(string listFile)
    {
        using TextReader reader = listFile == "-" ? Console.In : new StreamReader(listFile);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Split('\t', '\r', '\n')[0];
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-Sblqh?] [-o <out>] [-e <map>] [-p <name=value>]... [<dms>...]");
        stdout.WriteLine();
        stdout.WriteLine("Convert S1000D data modules to DocBook 5.");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -b, --basic            Use the s1kd2db stylesheet (DocBook 5, default).");
        stdout.WriteLine("  -S, --smart            Use the Smart Avionics s1000dtodb stylesheet set.");
        stdout.WriteLine("  -e, --entity-map <map> Use <map> as the info-entity map (name=value).");
        stdout.WriteLine("  -l, --list             Treat input as a list of CSDB objects.");
        stdout.WriteLine("  -o, --out <out>        Output to <out> instead of stdout (single input).");
        stdout.WriteLine("  -p, --param <n=v>      Pass a stylesheet parameter (repeatable).");
        stdout.WriteLine("  -h, -?, --help         Show help/usage message.");
        stdout.WriteLine("      --version          Show version information.");
        stdout.WriteLine("  <dms>                  Data modules to convert. Otherwise, read from stdin.");
    }
}
