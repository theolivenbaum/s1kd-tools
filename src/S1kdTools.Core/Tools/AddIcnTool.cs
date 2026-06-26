using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-addicn</c>: add the DTD entity and notation declarations
/// required to reference an ICN (Information Control Number) file from an
/// S1000D module.
/// </summary>
/// <remarks>
/// Mirrors <c>reference/tools/s1kd-addicn/s1kd-addicn.c</c>. The ICN entity and
/// notation work is delegated to the shared <see cref="Icn"/> helpers, which port
/// the <c>add_icn</c>/<c>add_notation</c> functions in
/// <c>reference/tools/common/s1kd_tools.c</c>.
/// </remarks>
public sealed class AddIcnTool : ITool
{
    public string Name => "addicn";
    public string Description => "Add entity/notation declarations for an ICN.";
    public string Version => "1.5.1";

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string src = "-";
        string outPath = "-";
        bool overwrite = false;
        bool fullpath = false;
        var icns = new List<string>();

        // The C tool uses getopt_long; argument processing stops collecting
        // options once a non-option token is seen only because getopt permutes
        // by default. To stay faithful to the documented usage we accept options
        // anywhere and treat the rest as ICNs.
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
                case "-f" or "--overwrite":
                    overwrite = true;
                    break;
                case "-F" or "--full-path":
                    fullpath = true;
                    break;
                case "-s" or "--source":
                    if (++i >= args.Count) { stderr.WriteLine($"s1kd-{Name}: ERROR: {a} requires an argument."); return 2; }
                    src = args[i];
                    break;
                case "-o" or "--out":
                    if (++i >= args.Count) { stderr.WriteLine($"s1kd-{Name}: ERROR: {a} requires an argument."); return 2; }
                    outPath = args[i];
                    break;
                default:
                    if (a.Length > 1 && a[0] == '-' && a != "-")
                    {
                        // Support combined short options such as "-fF" or "-fs".
                        if (TryParseCombinedShort(a, args, ref i, ref overwrite, ref fullpath, ref src, ref outPath, out bool handled, out int rc, stdout, stderr))
                        {
                            if (handled)
                            {
                                if (rc != int.MinValue)
                                {
                                    return rc;
                                }
                                break;
                            }
                        }
                        stderr.WriteLine($"s1kd-{Name}: ERROR: Unknown option: {a}");
                        return 2;
                    }
                    icns.Add(a);
                    break;
            }
        }

        XmlDocument doc;
        try
        {
            doc = src == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(src);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            // The C tool silently does nothing when read_xml_doc returns NULL,
            // returning 0. Preserve that exit code while reporting the cause.
            stderr.WriteLine($"s1kd-{Name}: ERROR: Could not read {src}: {ex.Message}");
            return 0;
        }

        foreach (string icn in icns)
        {
            Icn.AddIcn(doc, icn, fullpath);
        }

        if (overwrite && src != "-")
        {
            XmlUtils.SaveDoc(doc, src);
        }
        else if (outPath == "-")
        {
            stdout.Write(Icn.SerializeWithDtd(doc));
            stdout.Write('\n');
        }
        else
        {
            File.WriteAllText(outPath, Icn.SerializeWithDtd(doc), new UTF8Encoding(false));
        }

        return 0;
    }

    /// <summary>
    /// Handle combined short option clusters (e.g. <c>-fF</c>, <c>-fs FILE</c>).
    /// Returns true when the token was recognised as a short-option cluster.
    /// </summary>
    private bool TryParseCombinedShort(
        string token, IReadOnlyList<string> args, ref int i,
        ref bool overwrite, ref bool fullpath, ref string src, ref string outPath,
        out bool handled, out int rc, TextWriter stdout, TextWriter stderr)
    {
        handled = false;
        rc = int.MinValue;

        if (token.Length < 2 || token[0] != '-' || token[1] == '-')
        {
            return false;
        }

        for (int p = 1; p < token.Length; p++)
        {
            char c = token[p];
            switch (c)
            {
                case 'f':
                    overwrite = true;
                    break;
                case 'F':
                    fullpath = true;
                    break;
                case 'h':
                case '?':
                    ShowHelp(stdout);
                    handled = true;
                    rc = 0;
                    return true;
                case 's':
                case 'o':
                    {
                        // Remainder of token is the argument, else next token.
                        string val;
                        if (p + 1 < token.Length)
                        {
                            val = token[(p + 1)..];
                        }
                        else if (i + 1 < args.Count)
                        {
                            val = args[++i];
                        }
                        else
                        {
                            stderr.WriteLine($"s1kd-{Name}: ERROR: -{c} requires an argument.");
                            handled = true;
                            rc = 2;
                            return true;
                        }
                        if (c == 's') src = val; else outPath = val;
                        handled = true;
                        return true;
                    }
                default:
                    return false; // unknown character; let caller report error
            }
        }

        handled = true;
        return true;
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-o <file>] [-s <src>] [-fh?] <ICN>...");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -F, --full-path     Include full ICN file path.");
        stdout.WriteLine("  -f, --overwrite     Overwrite source file.");
        stdout.WriteLine("  -h, -?, --help      Show help/usage message.");
        stdout.WriteLine("  -o, --out <file>    Output filename.");
        stdout.WriteLine("  -s, --source <src>  Source filename.");
        stdout.WriteLine("  --version           Show version information.");
        stdout.WriteLine("  <ICN>...            ICNs to add.");
    }

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }
}
