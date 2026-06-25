using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-sns</c>: organize the data modules of a CSDB into a directory
/// hierarchy based on the SNS (Standard Numbering System) structure described in
/// a BREX data module's <c>snsRules</c>, or simply print that structure.
///
/// <para>The original C tool drives everything with <c>chdir</c>/<c>mkdir</c>
/// relative to the process working directory. This port keeps the same
/// semantics but tracks the directory it is building explicitly (rather than
/// mutating the process cwd) so the logic stays testable and side-effect free
/// outside the target tree. The directory naming, link/copy/move placement and
/// SNS walk all mirror the C.</para>
/// </summary>
public sealed class SnsTool : ITool
{
    public string Name => "sns";
    public string Description => "Organize a CSDB into a directory structure based on an SNS.";
    public string Version => "1.8.0";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitEncodingError = 1;
    private const int ExitOsError = 2;
    private const int ExitNoBrex = 3;

    private const string DefaultSnsDName = "SNS";
    private const string ErrPrefix = "s1kd-sns: ERROR: ";

    /// <summary>How a data module file is associated with its SNS directory.</summary>
    private enum LinkMode
    {
        /// <summary>Hard link (the C default).</summary>
        Hard,
        /// <summary>Symbolic link (-s).</summary>
        Symbolic,
        /// <summary>Copy the file (-c).</summary>
        Copy,
        /// <summary>Move/rename the file (-m).</summary>
        Move,
    }

    /* Per-run option state. */
    private LinkMode _linkMode = LinkMode.Hard;
    private bool _onlyNumb;       // -n: name directories with the SNS code only.
    private bool _printSns;       // -p: print the SNS instead of organizing.

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string srcdname = Directory.GetCurrentDirectory();
        string? snsdname = null;
        var brexFiles = new List<string>();

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
                    case "-c" or "--copy":
                        _linkMode = LinkMode.Copy;
                        break;
                    case "-D" or "--srcdir":
                        // real_path(): resolve to an absolute path.
                        srcdname = Path.GetFullPath(NextArg(args, ref i, "-D", stderr));
                        break;
                    case "-d" or "--outdir":
                        snsdname = NextArg(args, ref i, "-d", stderr);
                        break;
                    case "-m" or "--move":
                        _linkMode = LinkMode.Move;
                        break;
                    case "-n" or "--only-code":
                        _onlyNumb = true;
                        break;
                    case "-p" or "--print":
                        _printSns = true;
                        break;
                    case "-s" or "--symlink":
                        _linkMode = LinkMode.Symbolic;
                        break;
                    default:
                        if (a.Length > 1 && a[0] == '-' && a != "-")
                        {
                            stderr.WriteLine($"{ErrPrefix}Unknown option: {a}");
                            return 2;
                        }
                        brexFiles.Add(a);
                        break;
                }
            }

            snsdname ??= DefaultSnsDName;

            if (brexFiles.Count > 0)
            {
                foreach (string brex in brexFiles)
                {
                    PrintOrSetupSns(brex, snsdname, srcdname, stdout, stderr);
                }
            }
            else
            {
                PrintOrSetupSns("-", snsdname, srcdname, stdout, stderr);
            }
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }

        return 0;
    }

    private static string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"{ErrPrefix}{opt} requires an argument");
            throw new ExitException(2);
        }
        return args[i];
    }

    /* ----- SNS reading / dispatch (print_or_setup_sns) ----- */

    private void PrintOrSetupSns(string brexFname, string snsdname, string srcdname,
        TextWriter stdout, TextWriter stderr)
    {
        XmlDocument brex;
        try
        {
            brex = brexFname == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(brexFname);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{ErrPrefix}Could not read BREX data module: {brexFname}");
            throw new ExitException(ExitNoBrex);
        }

        // //snsRules/snsDescr — the root of the SNS structure.
        if (brex.SelectSingleNode("//snsRules/snsDescr") is not XmlElement snsDescr)
        {
            // No SNS rules present: nothing to do (matches the C, which only
            // acts when the node set is non-empty).
            return;
        }

        if (_printSns)
        {
            PrintSns(snsDescr, -1, stdout);
            return;
        }

        if (!Directory.Exists(snsdname))
        {
            MakeDir(snsdname, stderr);
            SetupSns(snsDescr, snsdname, stderr);
        }

        SortSns(snsdname, srcdname, stderr);
    }

    /* ----- printing (indent / print_sns) ----- */

    private static void PrintSns(XmlNode node, int level, TextWriter stdout)
    {
        for (XmlNode? cur = node.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.LocalName == "snsCode")
            {
                Indent(level, stdout);
                stdout.Write(cur.InnerText);
            }
            else if (cur.LocalName == "snsTitle")
            {
                stdout.Write($" - {cur.InnerText}\n");
            }
            else if (cur.NodeType == XmlNodeType.Element)
            {
                PrintSns(cur, level + 1, stdout);
            }
        }
    }

    private static void Indent(int level, TextWriter stdout)
    {
        for (int i = 0; i < level * 4; ++i)
        {
            stdout.Write(' ');
        }
    }

    /* ----- directory structure creation (setup_sns) ----- */

    /// <summary>
    /// Recreate the C <c>setup_sns</c> walk. <paramref name="dir"/> is the
    /// directory the C tool would currently be "in"; rather than using
    /// <c>chdir</c> we thread the current directory through the recursion.
    /// </summary>
    private void SetupSns(XmlNode node, string dir, TextWriter stderr)
    {
        // The directory created for the most recent snsCode at this level, and
        // the snsCode itself (used when renaming on snsTitle).
        string code = string.Empty;
        string cur = dir;

        for (XmlNode? child = node.FirstChild; child != null; child = child.NextSibling)
        {
            if (child.LocalName == "snsCode")
            {
                code = child.InnerText;
                cur = Path.Combine(dir, code);
                MakeDir(cur, stderr);
            }
            else if (child.LocalName == "snsTitle")
            {
                if (_onlyNumb)
                {
                    continue;
                }

                string title = CleanStr(child.InnerText);
                string oldname = Path.Combine(dir, code);
                string newname = Path.Combine(dir, $"{code} - {title}");

                RenameDir(oldname, newname, stderr);
                cur = newname;
            }
            else if (child.NodeType == XmlNodeType.Element)
            {
                // Recurse into the nested SNS container, building beneath the
                // directory established by the preceding snsCode/snsTitle.
                SetupSns(child, cur, stderr);
            }
        }
    }

    /* ----- DM placement (sort_sns / placedm / sns_exists) ----- */

    private void SortSns(string snsdname, string srcdname, TextWriter stderr)
    {
        if (!Directory.Exists(srcdname))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(srcdname))
        {
            string fname = Path.GetFileName(path);
            if (!IsDModule(fname))
            {
                continue;
            }
            if (TryParseDmCode(fname, out DmCode dm))
            {
                PlaceDm(fname, dm, snsdname, srcdname, stderr);
            }
        }
    }

    /// <summary>
    /// Descend into the SNS directory hierarchy following the DM's system /
    /// subsystem / subsubsystem / assembly codes, then place a link/copy/move of
    /// the source file there. Mirrors <c>placedm</c>.
    /// </summary>
    private void PlaceDm(string fname, DmCode code, string snsdname, string srcdname, TextWriter stderr)
    {
        string srcPath = Path.Combine(srcdname, fname);

        // Walk down as far as a matching directory exists at each level.
        string dest = snsdname;
        if (SnsExists(dest, code.SystemCode, out string lvl1))
        {
            dest = lvl1;
            if (SnsExists(dest, code.SubSystemCode, out string lvl2))
            {
                dest = lvl2;
                if (SnsExists(dest, code.SubSubSystemCode, out string lvl3))
                {
                    dest = lvl3;
                    if (SnsExists(dest, code.AssyCode, out string lvl4))
                    {
                        dest = lvl4;
                    }
                }
            }
        }

        string destPath = Path.Combine(dest, fname);

        // If a file with this name already exists at the destination: when it is
        // the source file itself (placement back in the flat dir) don't relink,
        // otherwise remove it so the link/copy can be (re)created.
        bool act = true;
        if (File.Exists(destPath))
        {
            if (!SamePath(dest, srcdname))
            {
                TryDelete(destPath);
            }
            else
            {
                act = false;
            }
        }

        if (!act)
        {
            return;
        }

        try
        {
            switch (_linkMode)
            {
                case LinkMode.Symbolic:
                    File.CreateSymbolicLink(destPath, srcPath);
                    break;
                case LinkMode.Copy:
                    File.Copy(srcPath, destPath, overwrite: false);
                    break;
                case LinkMode.Move:
                    File.Move(srcPath, destPath);
                    break;
                case LinkMode.Hard:
                default:
                    CreateHardLink(srcPath, destPath);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{ErrPrefix}{ex.Message}: {srcPath} => {destPath}");
            throw new ExitException(ExitOsError);
        }
    }

    /// <summary>
    /// Test whether a sub-directory of <paramref name="dir"/> begins with the
    /// given SNS <paramref name="code"/>; return its full path. Mirrors
    /// <c>sns_exists</c>, which matches the first directory entry whose name
    /// starts with the code.
    /// </summary>
    private static bool SnsExists(string dir, string code, out string match)
    {
        match = dir;
        if (!Directory.Exists(dir))
        {
            return false;
        }
        foreach (string entry in Directory.EnumerateDirectories(dir))
        {
            string name = Path.GetFileName(entry);
            if (name.StartsWith(code, StringComparison.Ordinal))
            {
                match = entry;
                return true;
            }
        }
        return false;
    }

    /* ----- DM code parsing (is_dmodule / parse_dmcode) ----- */

    private static bool IsDModule(string fname)
    {
        bool prefixed = fname.StartsWith("DMC-", StringComparison.Ordinal) ||
                        fname.StartsWith("DME-", StringComparison.Ordinal);
        bool xml = fname.Length >= 4 &&
                   fname.AsSpan(fname.Length - 4).Equals(".XML", StringComparison.OrdinalIgnoreCase);
        return prefixed && xml;
    }

    /// <summary>The DM code fields used for SNS placement.</summary>
    private readonly record struct DmCode(
        string SystemCode,
        string SubSystemCode,
        string SubSubSystemCode,
        string AssyCode);

    /// <summary>
    /// Parse a data module code filename into its SNS components. Mirrors the
    /// <c>parse_dmcode</c> sscanf, which splits on '-' after the prefix:
    /// <c>PREFIX-MIC-SDC-SYS-SUBSYS+SUBSUBSYS-ASSY-...</c>. The C scan requires
    /// either 11 or 13 of its fields to be filled to succeed.
    /// </summary>
    private static bool TryParseDmCode(string fname, out DmCode code)
    {
        code = default;

        // Drop the prefix up to and including the first '-' (the "%*[^-]-").
        int firstDash = fname.IndexOf('-');
        if (firstDash < 0)
        {
            return false;
        }

        string[] parts = fname[(firstDash + 1)..].Split('-');
        // parts: [0]=modelIdentCode [1]=systemDiffCode [2]=systemCode
        //        [3]=subSystem+subSubSystem [4]=assyCode ...
        if (parts.Length < 5)
        {
            return false;
        }

        string systemCode = parts[2];
        string subField = parts[3];        // "%1s%1s": first char = subSystem, second = subSubSystem
        string assyCode = parts[4];

        if (systemCode.Length == 0 || subField.Length < 2 || assyCode.Length == 0)
        {
            return false;
        }

        code = new DmCode(
            SystemCode: systemCode,
            SubSystemCode: subField[..1],
            SubSubSystemCode: subField.Substring(1, 1),
            AssyCode: assyCode);
        return true;
    }

    /* ----- filesystem helpers (makedir / rename_dir / cleanstr / copy) ----- */

    /// <summary>Replace characters that cannot be used in directory names.</summary>
    private static string CleanStr(string s)
    {
        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            switch (chars[i])
            {
                case '/':
                case '<':
                case '>':
                case ':':
                case '"':
                case '\\':
                case '|':
                case '?':
                case '*':
                    chars[i] = ' ';
                    break;
            }
        }
        return new string(chars);
    }

    private static void MakeDir(string path, TextWriter stderr)
    {
        if (Directory.Exists(path))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{ErrPrefix}Cannot create directory {path}: {ex.Message}");
            throw new ExitException(ExitOsError);
        }
    }

    private static void RenameDir(string oldName, string newName, TextWriter stderr)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return;
        }
        try
        {
            Directory.Move(oldName, newName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{ErrPrefix}Cannot rename directory {oldName} to {newName}: {ex.Message}");
            throw new ExitException(ExitOsError);
        }
    }

    private static void CreateHardLink(string source, string dest)
    {
        // .NET has no portable hard-link API; fall back to copying, which keeps
        // the SNS tree usable on every platform. (The C uses link(2)/CreateHardLink.)
        File.Copy(source, dest, overwrite: false);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /* ----- help / version ----- */

    private void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
    }

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: s1kd-sns [-D <dir>] [-d <dir>] [-cmnpsh?] [<BREX> ...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -c, --copy          Copy files instead of linking.");
        stdout.WriteLine("  -D, --srcdir <dir>  Directory where DMs are stored. Default is current directory.");
        stdout.WriteLine("  -d, --outdir <dir>  Directory to organize DMs in to. Default is \"" + DefaultSnsDName + "\"");
        stdout.WriteLine("  -h, -?, --help      Show usage message.");
        stdout.WriteLine("  -m, --move          Move files instead of linking.");
        stdout.WriteLine("  -n, --only-code     Only use the SNS code to name directories.");
        stdout.WriteLine("  -p, --print         Print SNS instead of organizing.");
        stdout.WriteLine("  -s, --symlink       Use symbolic links.");
        stdout.WriteLine("  --version           Show version information.");
        stdout.WriteLine("  <BREX>              BREX data module to read SNS structure from.");
    }
}
