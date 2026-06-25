using System.Xml;
using S1kdTools;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-brexcheck</c>: check CSDB objects against BREX (Business Rules
/// EXchange) data modules — the computable business rules.
///
/// <para>
/// Thoroughly ported: the structure object rules (<c>structureObjectRule</c> /
/// <c>objrule</c>) — evaluating each rule's <c>objectPath</c> against the object,
/// honouring the <c>allowedObjectFlag</c> (0/1) and, with <c>-c</c>, the allowed
/// <c>objectValue</c> set (single value / <c>pattern</c> / <c>range</c>); BREX
/// resolution (explicit <c>-b</c>, default <c>-B</c>, or the object's
/// <c>brexDmRef</c>); and the XML report structure.
/// </para>
///
/// <para>
/// Partial (see todo.md): SNS rule checking (<c>-S</c>) is implemented for data
/// modules; notation rule checking (<c>-n</c>) is a stub because System.Xml does
/// not expose internal-DTD NOTATION declarations. Layered BREX (<c>-l</c>),
/// severity-level configuration (<c>-w</c>), summary stats (<c>-T</c>), progress
/// bars, and XPath 2.0 (<c>-X</c>) are not ported.
/// </para>
/// </summary>
public sealed class BrexCheckTool : ITool
{
    public string Name => "brexcheck";
    public string Description => "Check CSDB objects against BREX data modules.";
    public string Version => "5.2.2";

    // Exit status codes (mirror the C #defines).
    private const int ExitSuccess = 0;
    private const int ExitBrexError = 1;
    private const int ExitBadDmodule = 2;
    private const int ExitBrexNotFound = 3;
    private const int ExitBadXPathVersion = 4;

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var brexFiles = new List<string>();
        var searchPaths = new List<string>();
        var files = new List<string>();
        string searchDir = ".";
        bool useDefaultBrex = false;
        bool checkValues = false;
        bool checkSns = false;
        bool strictSns = false;
        bool unstrictSns = false;
        bool checkNotations = false;
        bool xmlOut = false;
        bool quiet = false;
        bool verbose = false;
        bool ignoreEmpty = false;
        bool remDelete = false;
        bool isList = false;
        var showFnames = ShowFnames.None;

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return ExitSuccess;
                case "--version":
                    stdout.WriteLine($"{Name} (s1kd-tools) {Version}");
                    return ExitSuccess;
                case "-B" or "--default-brex":
                    useDefaultBrex = true;
                    break;
                case "-b" or "--brex":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -b requires an argument"); return ExitBadDmodule; }
                    brexFiles.Add(args[i]);
                    break;
                case "-d" or "--dir":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -d requires an argument"); return ExitBadDmodule; }
                    searchDir = args[i];
                    break;
                case "-I" or "--include":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -I requires an argument"); return ExitBadDmodule; }
                    searchPaths.Add(args[i]);
                    break;
                case "-c" or "--values":
                    checkValues = true;
                    break;
                case "-S" or "--sns":
                    checkSns = true;
                    break;
                case "-t" or "--strict":
                    strictSns = true;
                    break;
                case "-u" or "--unstrict":
                    unstrictSns = true;
                    break;
                case "-n" or "--notations":
                    checkNotations = true;
                    break;
                case "-x" or "--xml":
                    xmlOut = true;
                    break;
                case "-q" or "--quiet":
                    quiet = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-e" or "--ignore-empty":
                    ignoreEmpty = true;
                    break;
                case "-^" or "--remove-deleted":
                    remDelete = true;
                    break;
                case "-L" or "--list":
                    isList = true;
                    break;
                case "-F" or "--valid-filenames":
                    showFnames = ShowFnames.Valid;
                    break;
                case "-f" or "--filenames":
                    showFnames = ShowFnames.Invalid;
                    break;
                case "-X" or "--xpath-version":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -X requires an argument"); return ExitBadDmodule; }
                    if (args[i] != "1.0")
                    {
                        if (!quiet) stderr.WriteLine($"{Name}: ERROR: Unsupported XPath version: {args[i]}");
                        return ExitBadXPathVersion;
                    }
                    break;
                // Recognised but not (yet) ported flags — accepted as no-ops so
                // existing command lines don't break.
                case "-l" or "--layered":
                case "-N" or "--omit-issue":
                case "-o" or "--output-valid":
                case "-p" or "--progress":
                case "-r" or "--recursive":
                case "-s" or "--short":
                case "-T" or "--summary":
                case "-8" or "--deep-copy-nodes":
                    break;
                case "-w" or "--severity-levels":
                    if (++i >= args.Count) { stderr.WriteLine($"{Name}: ERROR: -w requires an argument"); return ExitBadDmodule; }
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        stderr.WriteLine($"{Name}: ERROR: Unknown option: {a}");
                        return ExitBadDmodule;
                    }
                    files.Add(a);
                    break;
            }
        }

        // Build the object list.
        var objects = new List<string>();
        bool useStdin = false;
        if (files.Count > 0)
        {
            if (isList)
            {
                foreach (string lf in files)
                {
                    objects.AddRange(ReadListFile(lf, stderr));
                }
            }
            else
            {
                objects.AddRange(files);
            }
        }
        else
        {
            objects.Add("-");
            useStdin = true;
        }

        BrexCheckOptions opts = BuildOptions(checkValues, checkSns, strictSns, unstrictSns, checkNotations, quiet, verbose);

        XmlDocument outDoc = XmlUtils.NewDocument();
        XmlElement brexCheck = outDoc.CreateElement("brexCheck");
        outDoc.AppendChild(brexCheck);

        int status = 0;

        foreach (string objPath in objects)
        {
            XmlDocument objDoc;
            try
            {
                objDoc = objPath == "-"
                    ? XmlUtils.ReadStream(Console.OpenStandardInput())
                    : XmlUtils.ReadDoc(objPath);
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                if (ignoreEmpty)
                {
                    continue;
                }
                if (!quiet)
                {
                    stderr.WriteLine(useStdin
                        ? $"{Name}: ERROR: stdin does not contain valid XML."
                        : $"{Name}: ERROR: Could not read file \"{objPath}\".");
                }
                return ExitBadDmodule;
            }

            if (remDelete)
            {
                XmlUtils.RemoveDeleteElements(objDoc);
            }

            // Resolve the BREX module(s) for this object.
            XmlDocument? brexDoc;
            string brexPath;
            if (brexFiles.Count > 0)
            {
                brexPath = brexFiles[0];
                brexDoc = LoadBrex(brexPath, objDoc, stderr, quiet);
                if (brexDoc == null) return ExitBadDmodule;
            }
            else if (useDefaultBrex)
            {
                brexPath = BrexCheck.DefaultBrexDmc(objDoc);
                brexDoc = BrexCheck.LoadDefaultBrex(brexPath);
                if (brexDoc == null)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: No default BREX data module found for {brexPath}.");
                    return ExitBrexNotFound;
                }
            }
            else
            {
                int err = ResolveReferencedBrex(objDoc, searchDir, searchPaths, objects,
                    out brexPath, out brexDoc, stderr, quiet);
                if (err == -1)
                {
                    // Object does not reference a BREX: warn and skip.
                    if (!quiet)
                    {
                        stderr.WriteLine(useStdin
                            ? $"{Name}: WARNING: Object on stdin does not reference a BREX data module."
                            : $"{Name}: WARNING: {objPath} does not reference a BREX data module.");
                    }
                    continue;
                }
                if (err == 1 || brexDoc == null)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: No BREX data module found for {(useStdin ? "object on stdin" : objPath)}.");
                    return ExitBrexNotFound;
                }
            }

            int errs = BrexCheck.Check(objDoc, brexDoc, opts, objPath, brexPath, out XmlDocument report);

            // Splice this object's <document> result into the combined report.
            XmlNode? documentNode = report.DocumentElement?.SelectSingleNode("document");
            if (documentNode != null)
            {
                brexCheck.AppendChild(outDoc.ImportNode(documentNode, true));
            }

            // Carry the configuration attributes onto the combined report once.
            if (report.DocumentElement != null && brexCheck.Attributes.Count == 0)
            {
                foreach (XmlAttribute attr in report.DocumentElement.Attributes)
                {
                    brexCheck.SetAttribute(attr.Name, attr.Value);
                }
            }

            if (verbose && !quiet)
            {
                stderr.WriteLine(errs > 0
                    ? $"{Name}: FAILED: {objPath} failed to validate against BREX {brexPath}."
                    : $"{Name}: SUCCESS: {objPath} validated successfully against BREX {brexPath}.");
            }

            status += errs;
        }

        // Print filenames if requested.
        if (showFnames != ShowFnames.None)
        {
            PrintFnames(brexCheck, showFnames, stdout);
        }

        if (xmlOut)
        {
            stdout.Write(XmlUtils.ToXmlString(outDoc));
            stdout.Write('\n');
        }

        return status > 0 ? ExitBrexError : ExitSuccess;
    }

    private static BrexCheckOptions BuildOptions(bool values, bool sns, bool strict, bool unstrict,
        bool notations, bool quiet, bool verbose)
    {
        BrexCheckOptions opts = BrexCheckOptions.None;
        if (values) opts |= BrexCheckOptions.Values;
        if (sns) opts |= BrexCheckOptions.Sns;
        if (strict) opts |= BrexCheckOptions.StrictSns;
        if (unstrict) opts |= BrexCheckOptions.UnstrictSns;
        if (notations) opts |= BrexCheckOptions.Notations;
        if (verbose) opts |= BrexCheckOptions.VerboseLog;
        else if (!quiet) opts |= BrexCheckOptions.NormalLog;
        return opts;
    }

    /// <summary>Load a BREX module by name: a default code, or a file path.</summary>
    private XmlDocument? LoadBrex(string name, XmlDocument objDoc, TextWriter stderr, bool quiet)
    {
        if (name == "-")
        {
            // The object on stdin is itself the BREX; check it against itself.
            return (XmlDocument)objDoc.Clone();
        }

        XmlDocument? def = BrexCheck.LoadDefaultBrex(name);
        if (def != null)
        {
            return def;
        }

        if (File.Exists(name))
        {
            try
            {
                return XmlUtils.ReadDoc(name);
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read file \"{name}\".");
                return null;
            }
        }

        if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not find BREX data module: {name}");
        return null;
    }

    /// <summary>
    /// Find the BREX referenced by an object's <c>brexDmRef</c>, then locate the
    /// matching file. Returns -1 if the object has no brexDmRef, 0 if found, 1 if
    /// referenced but not found. Mirrors <c>find_brex_fname_from_doc</c>.
    /// </summary>
    private int ResolveReferencedBrex(XmlDocument objDoc, string searchDir, List<string> searchPaths,
        List<string> objects, out string brexPath, out XmlDocument? brexDoc, TextWriter stderr, bool quiet)
    {
        brexPath = string.Empty;
        brexDoc = null;

        XmlNode? brexDmRef = objDoc.SelectSingleNode("//brexDmRef|//brexref");
        if (brexDmRef == null)
        {
            return -1;
        }

        string code = BuildBrexDmCode(brexDmRef);
        if (code.Length == 0)
        {
            return -1;
        }

        // Search the current directory, the include paths, and the object list.
        var dirs = new List<string> { searchDir };
        dirs.AddRange(searchPaths);

        foreach (string dir in dirs)
        {
            string? found = FindFileByCode(dir, code);
            if (found != null)
            {
                brexPath = found;
                brexDoc = TryLoad(found);
                if (brexDoc != null)
                {
                    return 0;
                }
            }
        }

        foreach (string obj in objects)
        {
            if (obj == "-")
            {
                continue;
            }
            string baseName = Path.GetFileName(obj);
            if (Csdb.StrMatch(code, baseName))
            {
                brexPath = obj;
                brexDoc = TryLoad(obj);
                if (brexDoc != null)
                {
                    return 0;
                }
            }
        }

        // Fall back to a built-in default BREX matching the code.
        XmlDocument? def = BrexCheck.LoadDefaultBrex(code.Length >= 34 ? code[..34] : code);
        if (def != null)
        {
            brexPath = code;
            brexDoc = def;
            return 0;
        }

        if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not find BREX data module: {code}");
        return 1;
    }

    private static XmlDocument? TryLoad(string path)
    {
        try
        {
            return XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return null;
        }
    }

    /// <summary>Find a file in <paramref name="dir"/> whose name matches the DM code prefix.</summary>
    private static string? FindFileByCode(string dir, string code)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }
        foreach (string path in Directory.EnumerateFiles(dir))
        {
            string baseName = Path.GetFileName(path);
            if (baseName.Length >= 4 &&
                baseName.AsSpan(baseName.Length - 4).Equals(".XML", StringComparison.OrdinalIgnoreCase) &&
                Csdb.StrMatch(code, baseName))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// Build the DMC string (e.g. "DMC-XX-A-...") of a referenced BREX DM, used to
    /// match a file on disk. Mirrors the dmcode construction in
    /// <c>find_brex_fname_from_doc</c> (issue 4.x+ form only).
    /// </summary>
    private static string BuildBrexDmCode(XmlNode brexDmRef)
    {
        XmlElement? dmCode = brexDmRef.SelectSingleNode(".//dmCode") as XmlElement;
        if (dmCode == null)
        {
            return string.Empty;
        }

        string G(string a) => dmCode.GetAttribute(a);

        string code = $"DMC-{G("modelIdentCode")}-{G("systemDiffCode")}-{G("systemCode")}-" +
                      $"{G("subSystemCode")}{G("subSubSystemCode")}-{G("assyCode")}-" +
                      $"{G("disassyCode")}{G("disassyCodeVariant")}-" +
                      $"{G("infoCode")}{G("infoCodeVariant")}-{G("itemLocationCode")}";

        // Wildcard the issue/inwork and language so StrMatch matches any issue.
        code += "_???-??";
        if (brexDmRef.SelectSingleNode(".//language") is XmlElement)
        {
            code += "_??-??";
        }
        return code;
    }

    private static IEnumerable<string> ReadListFile(string path, TextWriter stderr)
    {
        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(path);
        }
        catch (IOException)
        {
            stderr.WriteLine($"s1kd-brexcheck: ERROR: Could not read list: {path}");
            yield break;
        }
        foreach (string line in lines)
        {
            string t = line.Trim();
            if (t.Length > 0)
            {
                yield return t;
            }
        }
    }

    private enum ShowFnames { None, Invalid, Valid }

    private static void PrintFnames(XmlElement brexCheck, ShowFnames mode, TextWriter stdout)
    {
        XmlNodeList? docs = brexCheck.SelectNodes("document");
        if (docs == null)
        {
            return;
        }
        foreach (XmlNode doc in docs)
        {
            bool hasError = doc.SelectSingleNode("brex/error") != null;
            bool show = mode == ShowFnames.Invalid ? hasError : !hasError;
            if (show && doc is XmlElement el)
            {
                stdout.WriteLine(el.GetAttribute("path"));
            }
        }
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [-b <brex>] [-d <dir>] [-I <path>] [-X <version>] [-F|-f] [-BceLNnqrSvx^h?] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -B, --default-brex     Use the default BREX.");
        stdout.WriteLine("  -b, --brex <brex>      Use <brex> as the BREX data module.");
        stdout.WriteLine("  -c, --values           Check object values.");
        stdout.WriteLine("  -d, --dir <dir>        Directory to start search for BREX in.");
        stdout.WriteLine("  -e, --ignore-empty     Ignore empty/non-XML files.");
        stdout.WriteLine("  -F, --valid-filenames  Print the filenames of valid objects.");
        stdout.WriteLine("  -f, --filenames        Print the filenames of invalid objects.");
        stdout.WriteLine("  -I, --include <path>   Add <path> to BREX search path.");
        stdout.WriteLine("  -L, --list             Input is a list of data module filenames.");
        stdout.WriteLine("  -n, --notations        Check notation rules (partial).");
        stdout.WriteLine("  -q, --quiet            Quiet mode. Do not print errors.");
        stdout.WriteLine("  -S, --sns              Check SNS rules.");
        stdout.WriteLine("  -t, --strict           Strict SNS checking.");
        stdout.WriteLine("  -u, --unstrict         Unstrict SNS checking.");
        stdout.WriteLine("  -v, --verbose          Verbose mode.");
        stdout.WriteLine("  -X, --xpath-version    Force XPath version (only 1.0 supported).");
        stdout.WriteLine("  -x, --xml              XML output.");
        stdout.WriteLine("  -^, --remove-deleted   Check with \"delete\" elements removed.");
        stdout.WriteLine("      --version          Show version information.");
        stdout.WriteLine("  <object>               CSDB object(s) to check.");
    }
}
