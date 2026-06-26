using System.Text.RegularExpressions;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-instance</c>: produce filtered instances of S1000D CSDB
/// objects via applicability filtering and (partially) CIR resolution.
///
/// The applicability-filtering core is ported faithfully from
/// <c>reference/tools/s1kd-instance/s1kd-instance.c</c> (see
/// <see cref="S1kdTools.Instance"/> for the library API): removing
/// non-applicable content, reducing/simplifying/pruning annotations, removing
/// duplicate/unused annotations, and overwriting/whole-object metadata.
///
/// Implemented options (short + long):
///   -s/--assign, -a/--reduce, -A/--simplify, -9/--prune, -T/--tag,
///   -J/--clean-display-text, -6/--clean-annotations,
///   -#/--remove-duplicate-annotations, -^/--remove-deleted,
///   -U/--security-classes, -K/--skill-levels,
///   -c/--code, -e/--extension, -E/--no-extension, -t/--techname,
///   -i/--infoname, -V/--infoname-variant, -!/--no-infoname,
///   -l/--language, -n/--issue, -I/--date, -z/--issue-type, -u/--security,
///   -m/--remarks, -o/--out, -f/--overwrite, -q/--quiet, -v/--verbose,
///   -h/--help, --version.
///
/// CIR resolution: -R/--cir (explicit file or * to auto-find), -x/--xsl (custom
///   stylesheet), -D/--dump (dump built-in XSLT), -d/--dir, -r/--recursive.
///   All 17 built-in CIR types are resolved via the ported repository
///   stylesheets (see <see cref="S1kdTools.Instance.ResolveCir"/>).
/// Product filtering: -P/--pct + -p/--product assign a product's applicability
///   from a PCT, with -1/--act resolving the PCT per data module when no -P is
///   given. ACT/CCT primary-key resolution mirrors the C tool.
///
/// Depth features: container resolution (-Q), alts flattening (-F/-4),
/// automatic naming/output-dir (-O/-5/-N), update-instances (-@) with reapply
/// (-8) and dry-run (-7).
///
/// Source/repository ident control: -S/--no-source-ident and
///   -3/--no-repository-ident (matching the C flag mapping). A source ident is
///   added by default only when the extension, code, issue info or language is
///   changed.
/// Whole-object applicability overwrite/merge: -W/--set-applic, -y/--update-applic,
///   -Y/--applic. Whole-object checks: -w/--whole-objects, -0/--print-non-applic.
/// Originator: -g/--set-orig, -G/--custom-orig. Skill metadata: -k/--skill.
/// Comments: -C/--comment, -X/--comment-xpath. Acronym fixing: -M/--fix-acronyms.
/// Entity cleanup: -j/--clean-ents. Add-required: -Z/--add-required.
/// Read-only output: -%/--read-only. List input: -L/--list.
/// List properties report: -H/--list-properties (standalone/applic/all).
///
/// Partial / not ported (clearly noted): CCT dependency-test injection (-2/-~).
/// </summary>
public sealed class InstanceTool : ITool
{
    public string Name => "instance";
    public string Description => "Produce filtered instances of S1000D CSDB objects.";
    public string Version => "13.3.0";

    // Exit codes (mirror the C #defines).
    private const int ExitSuccess = 0;
    private const int ExitMissingArgs = 1;
    private const int ExitMissingFile = 2;
    private const int ExitMissingSource = 3;
    private const int ExitBadApplic = 4;
    private const int ExitBadXml = 6;
    private const int ExitBadArg = 7;
    private const int ExitBadDate = 8;

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var assigns = new List<string>();
        var files = new List<string>();

        bool reduce = false;       // -a
        bool simplify = false;     // -A
        bool prune = false;        // -9
        bool tagNonApplic = false; // -T
        bool cleanDispText = false; // -J
        bool remUnused = false;    // -6
        bool remDupl = false;      // -#
        bool delete = false;       // -^
        bool noExtension = false;  // -E
        bool noInfoName = false;   // -!
        bool overwrite = false;    // -f
        bool quiet = false;        // -q
        bool verbose = false;      // -v

        bool flatAlts = false;     // -F / -4
        bool fixAltsRefs = false;  // -4
        bool resolveContainers = false; // -Q
        string? outDir = null;     // -O (automatic naming)
        bool printFnames = false;  // -5
        bool omitIssue = false;    // -N
        bool updateInst = false;   // -@
        bool reApplic = false;     // -8
        bool dryRun = false;       // -7

        // New options.
        bool newApplic = false;       // -W/-Y/-y (overwrite/merge whole-object applic)
        bool combineApplic = true;    // false when -W (overwrite instead of combine)
        string newDisplayText = "";   // -Y
        string commentText = "";      // -C
        string commentPath = "/";     // -X
        bool fixAcronyms = false;     // -M
        bool cleanEnts = false;       // -j
        bool autocomp = false;        // -Z (add-required)
        bool lockFiles = false;       // -% (read-only)
        bool dmList = false;          // -L (list input)
        bool setOrig = false;         // -g/-G
        string? origSpec = null;      // -G
        string? skill = null;         // -k (set skill level)
        bool wholeDm = false;         // -w/-0
        bool printNonApplic = false;  // -0
        bool addSourceIdent = true;   // disabled via -S
        ListPropsRequest? listProps = null; // -H

        string? secClasses = null; // -U
        string? skillCodes = null; // -K
        string? code = null;       // -c
        string? extension = null;  // -e
        string? tech = null;       // -t
        string? info = null;       // -i
        string? infoNameVariant = null; // -V
        string? language = null;   // -l
        string? issinfo = null;    // -n
        string? issdate = null;    // -I
        string? isstype = null;    // -z
        string? security = null;   // -u
        string? remarks = null;    // -m
        string? outFile = null;    // -o

        // CIR resolution + product filtering.
        var cirs = new List<CirSpec>();    // -R (explicit files)
        bool findCir = false;              // -R *
        string searchDir = ".";            // -d
        bool recursive = false;            // -r
        string? defCirXslFile = null;      // -x (when no preceding -R)
        bool addRepIdent = true;           // disabled via -S
        string? userPct = null;            // -P / --pct
        string? product = null;            // -p / --product
        string? userAct = null;            // -1 / --act
        string? userCct = null;            // -2 / --cct

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];

            string? RequireArg(string opt)
            {
                if (++i >= args.Count)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: {opt} requires an argument");
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
                    stdout.WriteLine($"s1kd-{Name} (s1kd-tools) {Version}");
                    return ExitSuccess;

                case "-a" or "--reduce": reduce = true; break;
                case "-A" or "--simplify": simplify = true; break;
                case "-9" or "--prune": prune = true; break;
                case "-T" or "--tag": tagNonApplic = true; break;
                case "-J" or "--clean-display-text": cleanDispText = true; break;
                case "-6" or "--clean-annotations": remUnused = true; break;
                case "-#" or "--remove-duplicate-annotations": remDupl = true; break;
                case "-^" or "--remove-deleted": delete = true; break;
                case "-E" or "--no-extension": noExtension = true; break;
                case "-!" or "--no-infoname": noInfoName = true; break;
                case "-f" or "--overwrite": overwrite = true; break;
                case "-q" or "--quiet": quiet = true; break;
                case "-v" or "--verbose": verbose = true; break;

                case "-F" or "--flatten-alts": flatAlts = true; break;
                case "-4" or "--flatten-alts-refs": flatAlts = true; fixAltsRefs = true; break;
                case "-Q" or "--resolve-containers": resolveContainers = true; break;
                case "-N" or "--omit-issue": omitIssue = true; break;
                case "-5" or "--print": printFnames = true; break;
                case "-@" or "--update-instances": updateInst = true; break;
                case "-8" or "--reapply": reApplic = true; break;
                case "-7" or "--dry-run": dryRun = true; break;
                case "-O" or "--outdir":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; outDir = v; break; }

                // Whole-object applicability (-W overwrite, -y merge, -Y merge + display text).
                case "-W" or "--set-applic": newApplic = true; combineApplic = false; break;
                case "-y" or "--update-applic": newApplic = true; break;
                case "-Y" or "--applic":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; newApplic = true; newDisplayText = v; break; }

                // Comments (-C text, -X xpath).
                case "-C" or "--comment":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; commentText = v; break; }
                case "-X" or "--comment-xpath":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; commentPath = v; break; }

                case "-M" or "--fix-acronyms": fixAcronyms = true; break;
                case "-j" or "--clean-ents": cleanEnts = true; break;
                case "-Z" or "--add-required": autocomp = true; break;
                case "-%" or "--read-only": lockFiles = true; break;
                case "-L" or "--list": dmList = true; break;
                case "-w" or "--whole-objects": wholeDm = true; break;
                case "-0" or "--print-non-applic": printNonApplic = true; wholeDm = true; break;

                case "-g" or "--set-orig": setOrig = true; break;
                case "-G" or "--custom-orig":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; setOrig = true; origSpec = v; break; }

                case "-k" or "--skill":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; skill = v; break; }

                case "-H" or "--list-properties":
                {
                    string? v = RequireArg(a); if (v == null) return ExitMissingArgs;
                    listProps ??= new ListPropsRequest(v switch
                    {
                        "all" => Instance.ListPropsMethod.All,
                        "applic" => Instance.ListPropsMethod.Applic,
                        _ => Instance.ListPropsMethod.Standalone,
                    });
                    break;
                }

                case "-s" or "--assign":
                {
                    string? v = RequireArg(a); if (v == null) return ExitMissingArgs;
                    assigns.Add(v); break;
                }
                case "-U" or "--security-classes":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; secClasses = v; break; }
                case "-K" or "--skill-levels":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; skillCodes = v; break; }
                case "-c" or "--code":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; code = v; break; }
                case "-e" or "--extension":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; extension = v; break; }
                case "-t" or "--techname":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; tech = v; break; }
                case "-i" or "--infoname":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; info = v; break; }
                case "-V" or "--infoname-variant":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; infoNameVariant = v; break; }
                case "-l" or "--language":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; language = v; break; }
                case "-n" or "--issue":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; issinfo = v; break; }
                case "-I" or "--date":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; issdate = v; break; }
                case "-z" or "--issue-type":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; isstype = v; break; }
                case "-u" or "--security":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; security = v; break; }
                case "-m" or "--remarks":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; remarks = v; break; }
                case "-o" or "--out":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; outFile = v; break; }

                case "-R" or "--cir":
                {
                    string? v = RequireArg(a); if (v == null) return ExitMissingArgs;
                    if (v == "*") { findCir = true; }
                    else { cirs.Add(new CirSpec(v)); }
                    break;
                }
                case "-x" or "--xsl":
                {
                    string? v = RequireArg(a); if (v == null) return ExitMissingArgs;
                    // Attach to the most recent -R, or set the default CIR XSLT.
                    if (cirs.Count > 0) { cirs[^1].XslFile = v; }
                    else { defCirXslFile ??= v; }
                    break;
                }
                case "-D" or "--dump":
                {
                    string? v = RequireArg(a); if (v == null) return ExitMissingArgs;
                    string? xsl = Instance.DumpCirXsl(v);
                    if (xsl == null)
                    {
                        if (!quiet) stderr.WriteLine($"{Name}: WARNING: No built-in XSLT for CIR type: {v}");
                        return ExitBadArg;
                    }
                    stdout.Write(xsl);
                    if (!xsl.EndsWith('\n')) stdout.Write('\n');
                    return ExitSuccess;
                }
                case "-d" or "--dir":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; searchDir = v; break; }
                case "-r" or "--recursive":
                    recursive = true; break;
                // Mirrors the C flag mapping: -S = --no-source-ident, -3 = --no-repository-ident.
                case "-S" or "--no-source-ident":
                    addSourceIdent = false; break;
                case "-3" or "--no-repository-ident":
                    addRepIdent = false; break;

                case "-P" or "--pct":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; userPct = v; break; }
                case "-p" or "--product":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; product = v; break; }
                case "-1" or "--act":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; userAct = v; break; }
                case "-2" or "--cct":
                { string? v = RequireArg(a); if (v == null) return ExitMissingArgs; userCct = v; break; }

                default:
                    if (a.StartsWith('-') && a.Length > 1 && a != "-")
                    {
                        if (!quiet) stderr.WriteLine($"{Name}: ERROR: Unknown or unsupported option: {a}");
                        return ExitBadArg;
                    }
                    files.Add(a);
                    break;
            }
        }

        // Build the user-defined applicability definitions (mirrors read_applic
        // + define_applic). napplics counts user definitions.
        var (defs, napplics) = BuildDefs(assigns, quiet, stderr, out int errCode);
        if (defs == null)
        {
            return errCode;
        }

        // -H: build and print a properties report, then exit (mirrors the C
        // props_report path of main, which runs before the filtering loop).
        if (listProps != null)
        {
            return RunListProperties(listProps.Method, files, dmList, userAct, userCct, userPct,
                searchDir, recursive, verbose, quiet, stdout, stderr);
        }

        // Except in update mode (-@, not supported here), only add a source ident
        // by default when the extension, code, issue info or language is changed.
        if (string.IsNullOrEmpty(extension) && string.IsNullOrEmpty(code) &&
            string.IsNullOrEmpty(issinfo) && string.IsNullOrEmpty(language))
        {
            addSourceIdent = false;
        }

        // Read a user-supplied PCT (-P) once; it applies to all data modules.
        XmlDocument? userPctDoc = null;
        if (userPct != null)
        {
            try
            {
                userPctDoc = XmlUtils.ReadDoc(userPct);
            }
            catch (Exception ex) when (ex is IOException or XmlException or FileNotFoundException)
            {
                if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read PCT {userPct}");
                return ExitMissingFile;
            }
        }

        // A custom default CIR stylesheet supplied via -x with no preceding -R.
        string? defCirXslText = null;
        if (defCirXslFile != null)
        {
            try
            {
                defCirXslText = File.ReadAllText(defCirXslFile);
            }
            catch (IOException)
            {
                if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read XSLT {defCirXslFile}");
                return ExitMissingFile;
            }
        }

        // Resolve -R * by searching for CIR data modules under the search dir.
        if (findCir)
        {
            foreach (string cirFile in FindCirs(searchDir, recursive))
            {
                cirs.Add(new CirSpec(cirFile));
            }
        }

        // Apply a user PCT + product up front (these definitions are global).
        bool loadPctPerDm = false;
        if (!string.IsNullOrEmpty(product))
        {
            if (userPctDoc != null)
            {
                int n = Instance.LoadApplicFromPct(defs, userPctDoc, product, perDm: false);
                if (n == 0 && !quiet)
                {
                    stderr.WriteLine($"{Name}: WARNING: No product matching '{product}' in PCT '{userPct}'.");
                }
            }
            else
            {
                // The PCT must be located per data module via its ACT.
                loadPctPerDm = true;
            }
        }

        // CCT dependency-test injection (-2/-~ in the filtering loop) is not
        // ported yet; -2 is still used by -H list-properties above.
        _ = userCct;

        FilterMode mode = prune ? FilterMode.Prune
            : simplify ? FilterMode.Simplify
            : reduce ? FilterMode.Reduce
            : FilterMode.Default;

        if (files.Count == 0)
        {
            files.Add("-");
        }

        // -L: the named files are lists of object paths (one per line); read
        // stdin when no list file is given. Mirrors the dmlist handling in main.
        if (dmList)
        {
            var expanded = new List<string>();
            foreach (string listFile in files)
            {
                IEnumerable<string> lines;
                if (listFile == "-")
                {
                    lines = ReadLines(new StreamReader(Console.OpenStandardInput()));
                }
                else if (File.Exists(listFile))
                {
                    lines = File.ReadAllLines(listFile);
                }
                else
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read list file: {listFile}");
                    return ExitMissingFile;
                }
                foreach (string line in lines)
                {
                    string p = line.Trim();
                    if (p.Length > 0)
                    {
                        expanded.Add(p);
                    }
                }
            }
            files = expanded;
        }

        // Date validation/normalisation for -I.
        string? issYear = null, issMonth = null, issDay = null;
        if (issdate != null)
        {
            if (issdate == "-")
            {
                var now = DateTime.Now;
                issYear = now.Year.ToString("D4");
                issMonth = now.Month.ToString("D2");
                issDay = now.Day.ToString("D2");
            }
            else
            {
                var m = Regex.Match(issdate, @"^(\d{4})-(\d{2})-(\d{2})$");
                if (!m.Success)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: Bad issue date: {issdate}");
                    return ExitBadDate;
                }
                issYear = m.Groups[1].Value;
                issMonth = m.Groups[2].Value;
                issDay = m.Groups[3].Value;
            }
        }

        int status = ExitSuccess;
        foreach (string file in files)
        {
            // When updating an instance (-@), the named file is the instance;
            // resolve its source master object, which is what we filter. The
            // instance's applicability/skill/security/CIRs are loaded into the
            // active definitions so the source is re-derived identically.
            string src = file;
            string? instSrc = null;
            string? perFileSkill = skillCodes;
            string? perFileSec = secClasses;
            var perFileCirs = new List<CirSpec>(cirs);

            if (updateInst)
            {
                if (file != "-" && !File.Exists(file))
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read source object: {file}");
                    return ExitMissingFile;
                }

                XmlDocument inst;
                try
                {
                    inst = file == "-"
                        ? XmlUtils.ReadStream(Console.OpenStandardInput())
                        : XmlUtils.ReadDoc(file);
                }
                catch (Exception ex) when (ex is IOException or XmlException or FileNotFoundException)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: {file} does not contain valid XML.");
                    status = ExitBadXml;
                    continue;
                }

                string? sourceFile = FindSourceFile(inst, searchDir, recursive);
                if (sourceFile == null)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not find source object for instance {file}");
                    status = ExitMissingSource;
                    continue;
                }

                instSrc = file;
                src = sourceFile;
                if (verbose) stderr.WriteLine($"{Name}: INFO: Updating instance {file} from source {src}...");

                Instance.LoadApplicFromInst(defs, inst);
                perFileSkill = Instance.LoadSkillFromInst(inst) ?? perFileSkill;
                perFileSec = Instance.LoadSecFromInst(inst) ?? perFileSec;
                foreach (XmlNode repIdent in Instance.TakeCirsFromInst(inst))
                {
                    // repositorySourceDmIdent carries dmCode directly (copied from
                    // the CIR's dmIdent).
                    string? cirFile = FindRefDmFile(repIdent, searchDir, recursive);
                    if (cirFile != null)
                    {
                        perFileCirs.Add(new CirSpec(cirFile));
                    }
                }
            }

            XmlDocument doc;
            try
            {
                doc = src == "-"
                    ? XmlUtils.ReadStream(Console.OpenStandardInput())
                    : XmlUtils.ReadDoc(src);
            }
            catch (FileNotFoundException)
            {
                if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read source object: {src}");
                status = ExitMissingFile;
                continue;
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                if (!quiet) stderr.WriteLine($"{Name}: ERROR: {src} does not contain valid XML.");
                status = ExitBadXml;
                continue;
            }

            // Reapply the source object's own applicability (-8).
            if (reApplic)
            {
                Instance.LoadApplicFromInst(defs, doc);
            }

            // Load the product applicability per data module (when no -P given):
            // locate the PCT through the ACT this DM references.
            bool perDmLoaded = false;
            if (loadPctPerDm)
            {
                perDmLoaded = LoadPctPerDm(doc, defs, product!, userAct, searchDir, recursive, quiet, stderr);
            }

            // -w/-0: only create the instance if the whole object is applicable
            // (and matches skill/security/issue-type filters).
            if (wholeDm && !Instance.CreateInstance(doc, defs, skillCodes, secClasses, delete))
            {
                if (printNonApplic)
                {
                    stdout.WriteLine(file);
                }
                if (verbose)
                {
                    stderr.WriteLine($"{Name}: INFO: Ignoring non-applicable object: {file}");
                }
                if (perDmLoaded)
                {
                    Instance.ClearPerDmApplic(defs);
                }
                continue;
            }

            // Copy acronym definitions to acronyms prior to filtering (-M).
            if (fixAcronyms)
            {
                Instance.FixAcronymsPre(doc);
            }

            // Add a sourceDmIdent/sourcePmIdent linking to the source object.
            if (addSourceIdent)
            {
                Instance.AddSource(doc);
            }

            // Resolve CIR references before applicability filtering, mirroring
            // the order in the C main loop.
            if (perFileCirs.Count > 0)
            {
                bool isPm = doc.DocumentElement?.LocalName == "pm";
                foreach (CirSpec cir in perFileCirs)
                {
                    if (!File.Exists(cir.File))
                    {
                        if (!quiet) stderr.WriteLine($"{Name}: WARNING: Could not find CIR {cir.File}.");
                        continue;
                    }

                    XmlDocument cirDoc;
                    try
                    {
                        cirDoc = XmlUtils.ReadDoc(cir.File);
                    }
                    catch (Exception ex) when (ex is IOException or XmlException)
                    {
                        if (!quiet) stderr.WriteLine($"{Name}: ERROR: {cir.File} is not a valid CIR data module.");
                        status = ExitBadXml;
                        continue;
                    }

                    string? customXsl = defCirXslText;
                    if (cir.XslFile != null)
                    {
                        try { customXsl = File.ReadAllText(cir.XslFile); }
                        catch (IOException)
                        {
                            if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read XSLT {cir.XslFile}");
                            status = ExitMissingFile;
                            continue;
                        }
                    }

                    // PMs never get a repository source ident.
                    bool addSrc = addRepIdent && !isPm;
                    Instance.ResolveCir(doc, defs, cirDoc, addSrc, customXsl);
                }
            }

            ApplyFilter(doc, defs, napplics, mode, reduce || simplify, prune, simplify,
                tagNonApplic, cleanDispText, remDupl, remUnused, delete, perFileSec, perFileSkill);

            // Metadata setters (order mirrors the C tool).
            if (!string.IsNullOrEmpty(extension)) SetExtension(doc, extension);
            if (noExtension) StripExtension(doc);
            if (!string.IsNullOrEmpty(code)) SetCode(doc, code, quiet, stderr);
            SetTitle(doc, tech, info, infoNameVariant, noInfoName);
            if (!string.IsNullOrEmpty(language)) SetLang(doc, language);

            // -W/-y/-Y: overwrite/merge the whole-object applicability.
            if (newApplic && napplics > 0)
            {
                // Simplify the whole-object applic before merging, to remove
                // duplicate information. Not needed when overwriting.
                if (combineApplic)
                {
                    Instance.SimplWholeApplic(defs, doc, !prune);
                }
                Instance.SetApplic(doc, defs, napplics, newDisplayText, combineApplic);
            }

            if (!string.IsNullOrEmpty(issinfo))
            {
                if (!SetIssue(doc, issinfo, quiet, stderr)) { status = ExitMissingArgs; }
            }
            if (issYear != null) SetIssueDate(doc, issYear, issMonth!, issDay!);
            if (!string.IsNullOrEmpty(isstype)) SetIssueType(doc, isstype);
            if (!string.IsNullOrEmpty(security)) SetSecurity(doc, security);
            if (setOrig) Instance.SetOrig(doc, origSpec);
            if (!string.IsNullOrEmpty(skill)) Instance.SetSkill(doc, skill);
            if (remarks != null) SetRemarks(doc, remarks);

            // -C: insert an XML comment at -X path (default the root).
            if (commentText.Length > 0)
            {
                Instance.InsertComment(doc, commentText, commentPath);
            }

            // Remove empty PM entries left after filtering a PM.
            if (doc.DocumentElement?.LocalName == "pm")
            {
                Instance.RemoveEmptyPmEntries(doc);
            }

            // -j: remove unused external entities (e.g. ICNs).
            if (cleanEnts)
            {
                Instance.CleanEntities(doc);
            }

            // -Z: fix certain elements automatically after filtering.
            if (autocomp)
            {
                Instance.Autocomplete(doc);
            }

            // -M: reference repeated acronyms after the first.
            if (fixAcronyms)
            {
                Instance.FixAcronymsPost(doc);
            }

            // Flatten alts elements (-F / -4), after metadata.
            if (flatAlts)
            {
                Instance.FlattenAlts(doc, fixAltsRefs);
            }

            // Resolve references to container data modules (-Q).
            if (resolveContainers)
            {
                Instance.ResolveContainers(doc, defs,
                    refIdent =>
                    {
                        string? path = FindRefDmFile(refIdent, searchDir, recursive);
                        if (path == null)
                        {
                            return null;
                        }
                        try { return (XmlUtils.ReadDoc(path), path); }
                        catch (Exception ex) when (ex is IOException or XmlException) { return null; }
                    },
                    path =>
                    {
                        if (!quiet) stderr.WriteLine($"{Name}: WARNING: Could not resolve container {path}");
                    });
            }

            // The ACT/PCT may differ for the next object, so per-DM assigns (and
            // those loaded from an instance) must be cleared. Defs set with -s
            // carry over. Mirrors the C "load_applic_per_dm || re_applic" check.
            if (perDmLoaded || reApplic || updateInst)
            {
                Instance.ClearPerDmApplic(defs);
            }

            // When updating an instance, reset the source name to the instance so
            // overwrite (-f) targets the instance file.
            if (updateInst && instSrc != null)
            {
                src = instSrc;
            }

            // Determine the output target. With -O the file is named automatically
            // in the output directory; otherwise it goes to -o, the source (-f) or
            // stdout.
            string target;
            if (outDir != null)
            {
                if (!Directory.Exists(outDir))
                {
                    try { Directory.CreateDirectory(outDir); }
                    catch (IOException)
                    {
                        if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not create directory {outDir}");
                        return ExitBadArg;
                    }
                }

                string? name = Instance.AutoName(src, doc, outDir, omitIssue);
                if (name == null)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: Cannot automatically name unsupported object types.");
                    return ExitBadXml;
                }
                target = name;
            }
            else if (outFile != null)
            {
                target = outFile;
            }
            else if (overwrite && src != "-")
            {
                target = src;
            }
            else
            {
                target = "-";
            }

            if (target == "-")
            {
                if (!dryRun)
                {
                    stdout.Write(XmlUtils.ToXmlString(doc));
                    stdout.Write('\n');
                }
            }
            else
            {
                // An existing output file is only overwritten when -f is given
                // (mirrors the C "!use_stdout && access(out)==0 && !force_overwrite"
                // check, which applies to both -o and -O).
                bool exists = File.Exists(target);
                if (exists && !overwrite)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: WARNING: {target} already exists. Use -f to overwrite.");
                }
                else
                {
                    if (!dryRun)
                    {
                        try
                        {
                            XmlUtils.SaveDoc(doc, target);
                            // -%: make the written instance read-only.
                            if (lockFiles)
                            {
                                try { File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly); }
                                catch (IOException) { /* best-effort, mirrors mkreadonly */ }
                            }
                        }
                        catch (IOException)
                        {
                            if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not write {target}");
                            status = ExitMissingFile;
                        }
                    }
                    if (printFnames)
                    {
                        stdout.WriteLine(target);
                    }
                }
            }
        }

        return status;
    }

    /// <summary>
    /// Run the applicability filter in-place over <paramref name="doc"/>, mirroring
    /// the order of operations in the C <c>main</c> loop. <paramref name="defs"/> is
    /// the user definitions element.
    /// </summary>
    private static void ApplyFilter(XmlDocument doc, XmlElement defs, int napplics, FilterMode mode,
        bool cleanOrSimpl, bool prune, bool simpl, bool tagNonApplic, bool cleanDispText,
        bool remDupl, bool remUnused, bool delete, string? secClasses, string? skillCodes)
    {
        XmlElement? root = doc.DocumentElement;
        if (root == null)
        {
            return;
        }

        XmlNode? rag = doc.SelectSingleNode("//referencedApplicGroup|//inlineapplics");

        bool hasDefs = HasElementChild(defs);

        if (rag != null)
        {
            if (hasDefs)
            {
                Instance.StripApplic(defs, rag, root, tagNonApplic);

                if (cleanOrSimpl)
                {
                    // remtrue is true unless prune (-9).
                    bool remtrue = !prune;
                    Instance.CleanApplicStmts(defs, rag, remtrue);

                    if (!HasElementChild(rag))
                    {
                        rag.ParentNode?.RemoveChild(rag);
                        rag = null;
                    }

                    // Called unconditionally (matches C: clean_applic runs even
                    // when the group was just removed, to drop dangling
                    // applicRefId references).
                    Instance.CleanApplic(rag, root);

                    if ((simpl || prune) && rag != null)
                    {
                        rag = Instance.SimplApplicClean(defs, rag, remtrue, cleanDispText);
                    }

                    if (remtrue && rag != null)
                    {
                        rag = Instance.RemSupersets(defs, rag, root, !simpl);
                    }
                }
            }

            if (remDupl && rag != null)
            {
                rag = Instance.RemDuplAnnotations(doc, rag);
            }

            if (remUnused && rag != null)
            {
                rag = Instance.RemUnusedAnnotations(doc, rag);
            }
        }

        if (secClasses != null)
        {
            FilterElementsByAtt(doc, "securityClassification", secClasses);
        }
        if (skillCodes != null)
        {
            FilterElementsByAtt(doc, "skillLevelCode", skillCodes);
        }
        if (delete)
        {
            XmlUtils.RemoveDeleteElements(doc);
        }

        _ = napplics;
        _ = mode;
    }

    /// <summary>
    /// Build the user definitions element from <c>-s ident:type=value</c> args.
    /// Mirrors <c>read_applic</c> + <c>define_applic</c> (with multi-value merge).
    /// </summary>
    private static (XmlElement? defs, int napplics) BuildDefs(
        List<string> assigns, bool quiet, TextWriter stderr, out int errCode)
    {
        errCode = ExitSuccess;
        var owner = XmlUtils.NewDocument();
        var defs = owner.CreateElement("applic");
        owner.AppendChild(defs);
        int napplics = 0;

        var pattern = new Regex(@"^[^:]+:(prodattr|condition)=[^|~]+");

        foreach (string s in assigns)
        {
            if (!pattern.IsMatch(s))
            {
                if (!quiet)
                {
                    stderr.WriteLine(
                        $"s1kd-instance: ERROR: Malformed applicability definition: \"{s}\". " +
                        "Definitions must be in the form of \"<ident>:<type>=<value>\".");
                }
                errCode = ExitBadApplic;
                return (null, 0);
            }

            // strtok(":"), strtok("="), strtok("") — ident, type, value.
            int colon = s.IndexOf(':');
            int eq = s.IndexOf('=', colon + 1);
            string ident = s[..colon];
            string type = s[(colon + 1)..eq];
            string value = s[(eq + 1)..];

            DefineApplic(defs, ref napplics, ident, type, value);
        }

        return (defs, napplics);
    }

    /// <summary>Define a value for a product attribute or condition. Mirrors <c>define_applic</c> (user-defined path).</summary>
    private static void DefineApplic(XmlElement defs, ref int napplics, string ident, string type, string value)
    {
        XmlElement? assert = null;
        for (XmlNode? cur = defs.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur is XmlElement el &&
                el.GetAttribute("applicPropertyIdent") == ident &&
                el.GetAttribute("applicPropertyType") == type)
            {
                assert = el;
            }
        }

        if (assert == null)
        {
            assert = defs.OwnerDocument!.CreateElement("assert");
            assert.SetAttribute("applicPropertyIdent", ident);
            assert.SetAttribute("applicPropertyType", type);
            assert.SetAttribute("applicPropertyValues", value);
            assert.SetAttribute("userDefined", "true");
            defs.AppendChild(assert);
            napplics++;
            return;
        }

        // Existing definition: merge values (user-defined may modify).
        if (assert.HasAttribute("applicPropertyValues"))
        {
            string first = assert.GetAttribute("applicPropertyValues");
            if (first != value)
            {
                AddValueChild(assert, first);
                AddValueChild(assert, value);
                assert.RemoveAttribute("applicPropertyValues");
            }
        }
        else
        {
            bool dup = false;
            for (XmlNode? cur = assert.FirstChild; cur != null && !dup; cur = cur.NextSibling)
            {
                if (cur.InnerText == value)
                {
                    dup = true;
                }
            }
            if (!dup)
            {
                AddValueChild(assert, value);
            }
        }
    }

    private static void AddValueChild(XmlElement assert, string value)
    {
        var v = assert.OwnerDocument!.CreateElement("value");
        v.InnerText = value;
        assert.AppendChild(v);
    }

    private static bool HasElementChild(XmlNode node)
    {
        for (XmlNode? cur = node.FirstChild; cur != null; cur = cur.NextSibling)
        {
            if (cur.NodeType == XmlNodeType.Element)
            {
                return true;
            }
        }
        return false;
    }

    // ---- element filtering by attribute (mirrors filter_elements_by_att) ----
    private static void FilterElementsByAtt(XmlDocument doc, string att, string codes)
    {
        var toRemove = new List<XmlNode>();
        foreach (XmlNode n in doc.SelectNodes($"//content//*[@{att}]")!)
        {
            string val = ((XmlElement)n).GetAttribute(att);
            // C uses strstr(codes, val): substring containment.
            if (!codes.Contains(val))
            {
                toRemove.Add(n);
            }
        }
        foreach (XmlNode n in toRemove)
        {
            n.ParentNode?.RemoveChild(n);
        }
    }

    // ---- metadata setters (subset, ported from s1kd-instance.c) ----

    private static void SetLang(XmlDocument doc, string lang)
    {
        XmlNode? language = doc.SelectSingleNode(
            "//dmIdent/language|//dmaddres/language|//pmIdent/language|//pmaddres/language|" +
            "//commentIdent/language|//cstatus/language|//updateIdent/language");
        if (language is not XmlElement el || el.ParentNode == null)
        {
            return;
        }
        string parent = el.ParentNode.LocalName;
        bool iss30 = parent is "dmaddres" or "pmaddres";
        if (parent is not ("dmIdent" or "pmIdent" or "dmaddres" or "pmaddres"))
        {
            return;
        }

        int dash = lang.IndexOf('-');
        string langCode = dash >= 0 ? lang[..dash] : lang;
        string countryCode = dash >= 0 ? lang[(dash + 1)..] : string.Empty;

        el.SetAttribute(iss30 ? "language" : "languageIsoCode", langCode.ToLowerInvariant());
        el.SetAttribute(iss30 ? "country" : "countryIsoCode", countryCode.ToUpperInvariant());
    }

    private static void SetIssueType(XmlDocument doc, string type)
    {
        XmlNode? status = doc.SelectSingleNode(
            "//dmStatus|//pmStatus|//commentStatus|//dmlStatus|//scormContentPackageStatus|//issno");
        if (status is not XmlElement el)
        {
            return;
        }
        el.SetAttribute(el.LocalName == "issno" ? "type" : "issueType", type);
    }

    private static bool SetIssue(XmlDocument doc, string issinfo, bool quiet, TextWriter stderr)
    {
        XmlNode? issueInfo = doc.SelectSingleNode(
            "//dmIdent/issueInfo|//dmaddres/issno|//pmIdent/issueInfo|//pmaddres/issno|" +
            "//dmlIdent/issueInfo|//dml/issno|//imfIdent/issueInfo|//updateIdent/issueInfo");
        if (issueInfo is not XmlElement el)
        {
            return true;
        }

        var m = Regex.Match(issinfo, @"^(\d{1,3})-(\d{1,2})$");
        if (!m.Success)
        {
            if (!quiet) stderr.WriteLine("s1kd-instance: ERROR: Invalid format for issue/in-work number.");
            return false;
        }
        string issue = m.Groups[1].Value;
        string inwork = m.Groups[2].Value;

        if (el.LocalName == "issueInfo")
        {
            el.SetAttribute("issueNumber", issue);
            el.SetAttribute("inWork", inwork);
        }
        else
        {
            el.SetAttribute("issno", issue);
            el.SetAttribute("inwork", inwork);
        }
        return true;
    }

    private static void SetIssueDate(XmlDocument doc, string year, string month, string day)
    {
        XmlNode? issueDate = doc.SelectSingleNode("//issueDate|//issdate");
        if (issueDate is not XmlElement el)
        {
            return;
        }
        el.SetAttribute("year", year);
        el.SetAttribute("month", month);
        el.SetAttribute("day", day);
    }

    private static void SetSecurity(XmlDocument doc, string sec)
    {
        XmlNode? security = doc.SelectSingleNode("//security");
        if (security is not XmlElement el || el.ParentNode == null)
        {
            return;
        }
        bool iss4x = el.ParentNode.LocalName is "dmStatus" or "pmStatus";
        el.SetAttribute(iss4x ? "securityClassification" : "class", sec);
    }

    private static void SetRemarks(XmlDocument doc, string s)
    {
        XmlNode? status = doc.SelectSingleNode(
            "//dmStatus|//pmStatus|//commentStatus|//dmlStatus|//status|//pmstatus|//cstatus");
        if (status is not XmlElement st)
        {
            return;
        }

        bool iss30 = st.LocalName is "status" or "pmstatus" or "cstatus";

        XmlNode? existing = st.SelectSingleNode("remarks");
        existing?.ParentNode?.RemoveChild(existing);

        if (s.Length == 0)
        {
            return;
        }

        var remarks = doc.CreateElement("remarks");
        var para = doc.CreateElement(iss30 ? "p" : "simplePara");
        para.InnerText = s;
        remarks.AppendChild(para);
        st.AppendChild(remarks);
    }

    private static void SetExtension(XmlDocument doc, string extension)
    {
        XmlNode? identExtension = doc.SelectSingleNode(
            "//dmIdent/identExtension|//pmIdent/identExtension|//dmaddres/dmcextension");
        XmlNode? code = doc.SelectSingleNode("//dmIdent/dmCode|//pmIdent/pmCode|//dmaddres/dmc");
        if (code is not XmlElement codeEl || codeEl.ParentNode == null)
        {
            return;
        }
        bool iss30 = codeEl.LocalName == "dmc";

        string[] parts = extension.Split('-');
        string producer = parts.Length > 0 ? parts[0] : string.Empty;
        string extCode = parts.Length > 1 ? parts[1] : string.Empty;

        if (identExtension is not XmlElement extEl)
        {
            extEl = doc.CreateElement(iss30 ? "dmcextension" : "identExtension");
            codeEl.ParentNode.InsertBefore(extEl, codeEl);
        }

        if (iss30)
        {
            var p = doc.CreateElement("dmeproducer"); p.InnerText = producer; extEl.AppendChild(p);
            var c = doc.CreateElement("dmecode"); c.InnerText = extCode; extEl.AppendChild(c);
        }
        else
        {
            extEl.SetAttribute("extensionProducer", producer);
            extEl.SetAttribute("extensionCode", extCode);
        }
    }

    private static void StripExtension(XmlDocument doc)
    {
        XmlNode? ext = doc.SelectSingleNode(
            "//dmIdent/identExtension|//pmIdent/identExtension|//dmaddres/dmcextension");
        ext?.ParentNode?.RemoveChild(ext);
    }

    private static void SetCode(XmlDocument doc, string newCode, bool quiet, TextWriter stderr)
    {
        XmlNode? code = doc.SelectSingleNode(
            "//dmIdent/dmCode|//pmIdent/pmCode|//commentIdent/commentCode|//dmlIdent/dmlCode|" +
            "//dmaddres/dmc/avee|//pmaddres/pmc|//cstatus/ccode|//dml/dmlc");
        if (code is not XmlElement el)
        {
            return;
        }

        if (el.LocalName is "dmCode")
        {
            SetDmCode(el, newCode, quiet, stderr);
        }
        else if (el.LocalName is "pmCode")
        {
            SetPmCode(el, newCode, quiet, stderr);
        }
        // Issue 3.0 and comment/DML codes: not ported (note).
    }

    private static void SetDmCode(XmlElement code, string s, bool quiet, TextWriter stderr)
    {
        // modelIdentCode-systemDiffCode-systemCode-subSystemCode subSubSystemCode-
        // assyCode-disassyCode disassyCodeVariant-infoCode infoCodeVariant-itemLocationCode
        // [-learnCode-learnEventCode]
        var m = Regex.Match(s,
            @"^([^-]{1,14})-([^-]{1,4})-([^-]{1,3})-(.)(.)-([^-]{1,4})-(..)([^-]{1,3})-(...)(.)-(.)(?:-(...)(.))?$");
        if (!m.Success)
        {
            if (!quiet) stderr.WriteLine($"s1kd-instance: ERROR: Bad data module code: {s}.");
            return;
        }
        code.SetAttribute("modelIdentCode", m.Groups[1].Value);
        code.SetAttribute("systemDiffCode", m.Groups[2].Value);
        code.SetAttribute("systemCode", m.Groups[3].Value);
        code.SetAttribute("subSystemCode", m.Groups[4].Value);
        code.SetAttribute("subSubSystemCode", m.Groups[5].Value);
        code.SetAttribute("assyCode", m.Groups[6].Value);
        code.SetAttribute("disassyCode", m.Groups[7].Value);
        code.SetAttribute("disassyCodeVariant", m.Groups[8].Value);
        code.SetAttribute("infoCode", m.Groups[9].Value);
        code.SetAttribute("infoCodeVariant", m.Groups[10].Value);
        code.SetAttribute("itemLocationCode", m.Groups[11].Value);
        if (m.Groups[12].Success)
        {
            code.SetAttribute("learnCode", m.Groups[12].Value);
            code.SetAttribute("learnEventCode", m.Groups[13].Value);
        }
    }

    private static void SetPmCode(XmlElement code, string s, bool quiet, TextWriter stderr)
    {
        var m = Regex.Match(s, @"^([^-]{1,14})-([^-]{1,5})-([^-]{1,5})-(..)$");
        if (!m.Success)
        {
            if (!quiet) stderr.WriteLine($"s1kd-instance: ERROR: Bad publication module code: {s}.");
            return;
        }
        code.SetAttribute("modelIdentCode", m.Groups[1].Value);
        code.SetAttribute("pmIssuer", m.Groups[2].Value);
        code.SetAttribute("pmNumber", m.Groups[3].Value);
        code.SetAttribute("pmVolume", m.Groups[4].Value);
    }

    private static void SetTitle(XmlDocument doc, string? tech, string? info, string? infoNameVariant, bool noInfoName)
    {
        XmlNode? dmTitle = doc.SelectSingleNode("//dmAddressItems/dmTitle|//dmaddres/dmtitle");
        XmlNode? techName = doc.SelectSingleNode(
            "//dmAddressItems/dmTitle/techName|//pmAddressItems/pmTitle|//commentAddressItems/commentTitle|" +
            "//dmaddres/dmtitle/techname|//pmaddres/pmtitle|//cstatus/ctitle");
        if (techName is not XmlElement techEl)
        {
            return;
        }
        bool iss30 = techEl.LocalName is not ("techName" or "pmTitle" or "commentTitle");

        XmlNode? infoName = doc.SelectSingleNode("//dmAddressItems/dmTitle/infoName|//dmaddres/dmtitle/infoname");
        XmlNode? infoNameVariantNode = doc.SelectSingleNode("//dmAddressItems/dmTitle/infoNameVariant");

        if (tech != null)
        {
            techEl.InnerText = tech;
        }

        if (info != null)
        {
            if (infoName == null && dmTitle != null)
            {
                infoName = doc.CreateElement(iss30 ? "infoname" : "infoName");
                dmTitle.AppendChild(infoName);
            }
            if (infoName != null)
            {
                infoName.InnerText = info;
            }
        }
        else if (noInfoName && infoName != null)
        {
            infoName.ParentNode?.RemoveChild(infoName);
        }

        if (infoNameVariant != null && dmTitle != null)
        {
            if (infoNameVariantNode != null)
            {
                infoNameVariantNode.InnerText = infoNameVariant;
            }
            else
            {
                var v = doc.CreateElement("infoNameVariant");
                v.InnerText = infoNameVariant;
                dmTitle.AppendChild(v);
            }
        }
        else if (noInfoName && infoNameVariantNode != null)
        {
            infoNameVariantNode.ParentNode?.RemoveChild(infoNameVariantNode);
        }
    }

    // ---- list-properties (-H) ----

    /// <summary>A pending <c>-H</c> request: the property listing method.</summary>
    private sealed class ListPropsRequest
    {
        public ListPropsRequest(Instance.ListPropsMethod method) => Method = method;
        public Instance.ListPropsMethod Method { get; }
    }

    /// <summary>
    /// Build and print a properties report for the named objects (or lists, with
    /// <paramref name="dmList"/>). Mirrors the <c>props_report</c> branch of the C
    /// <c>main</c> function.
    /// </summary>
    private int RunListProperties(Instance.ListPropsMethod method, List<string> files, bool dmList,
        string? userAct, string? userCct, string? userPct, string searchDir, bool recursive,
        bool verbose, bool quiet, TextWriter stdout, TextWriter stderr)
    {
        _ = verbose;

        // Resolve the list of object paths.
        var paths = new List<string>();
        IEnumerable<string> inputs = files.Count == 0 ? new[] { "-" } : files;
        foreach (string f in inputs)
        {
            if (dmList)
            {
                IEnumerable<string> lines;
                if (f == "-")
                {
                    lines = ReadLines(new StreamReader(Console.OpenStandardInput()));
                }
                else if (File.Exists(f))
                {
                    lines = File.ReadAllLines(f);
                }
                else
                {
                    if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read list file: {f}");
                    continue;
                }
                foreach (string line in lines)
                {
                    string p = line.Trim();
                    if (p.Length > 0)
                    {
                        paths.Add(p);
                    }
                }
            }
            else
            {
                paths.Add(f);
            }
        }

        XmlDocument? ReadObject(string path)
        {
            try
            {
                return path == "-" ? XmlUtils.ReadStream(Console.OpenStandardInput()) : XmlUtils.ReadDoc(path);
            }
            catch (Exception ex) when (ex is IOException or XmlException or FileNotFoundException)
            {
                return null;
            }
        }

        // ACT/CCT/PCT resolution for non-standalone modes.
        XmlDocument? FindActDoc(XmlDocument doc)
        {
            if (userAct != null)
            {
                try { return XmlUtils.ReadDoc(userAct); }
                catch (Exception ex) when (ex is IOException or XmlException or FileNotFoundException) { return null; }
            }
            string? file = FindRefDmFile(
                doc.SelectSingleNode("//applicCrossRefTableRef/dmRef/dmRefIdent|//actref/refdm"),
                searchDir, recursive);
            return ReadRefDoc(file);
        }

        XmlDocument? FindCctDoc(XmlDocument act)
        {
            if (userCct != null)
            {
                try { return XmlUtils.ReadDoc(userCct); }
                catch (Exception ex) when (ex is IOException or XmlException or FileNotFoundException) { return null; }
            }
            string? file = FindRefDmFile(
                act.SelectSingleNode("//condCrossRefTableRef/dmRef/dmRefIdent|//cctref/refdm"),
                searchDir, recursive);
            return ReadRefDoc(file);
        }

        XmlDocument? FindPctDoc(XmlDocument act)
        {
            if (userPct != null)
            {
                try { return XmlUtils.ReadDoc(userPct); }
                catch (Exception ex) when (ex is IOException or XmlException or FileNotFoundException) { return null; }
            }
            string? file = FindRefDmFile(
                act.SelectSingleNode("//productCrossRefTableRef/dmRef/dmRefIdent|//pctref/refdm"),
                searchDir, recursive);
            return ReadRefDoc(file);
        }

        XmlDocument report = Instance.BuildPropertiesReport(paths, method, ReadObject,
            FindActDoc, FindCctDoc, FindPctDoc);

        stdout.Write(XmlUtils.ToXmlString(report));
        stdout.Write('\n');
        return ExitSuccess;
    }

    private static XmlDocument? ReadRefDoc(string? file)
    {
        if (file == null)
        {
            return null;
        }
        try { return XmlUtils.ReadDoc(file); }
        catch (Exception ex) when (ex is IOException or XmlException or FileNotFoundException) { return null; }
    }

    private static IEnumerable<string> ReadLines(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            yield return line;
        }
    }

    private void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine($"Usage: s1kd-{Name} [options] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -s, --assign <ident>:<type>=<value>   Applicability definition.");
        stdout.WriteLine("  -a, --reduce                          Remove unambiguous annotations.");
        stdout.WriteLine("  -A, --simplify                        Simplify and reduce annotations.");
        stdout.WriteLine("  -9, --prune                           Simplify by removing only false assertions.");
        stdout.WriteLine("  -T, --tag                             Tag non-applicable elements instead of removing.");
        stdout.WriteLine("  -J, --clean-display-text              Remove display text from simplified annotations.");
        stdout.WriteLine("  -6, --clean-annotations               Remove unused applicability annotations.");
        stdout.WriteLine("  -#, --remove-duplicate-annotations    Remove duplicate annotations.");
        stdout.WriteLine("  -^, --remove-deleted                  Remove deleted elements.");
        stdout.WriteLine("  -U, --security-classes <classes>      Filter on security classes.");
        stdout.WriteLine("  -K, --skill-levels <levels>           Filter on skill levels.");
        stdout.WriteLine("  -c, --code <code>                     New code of the instance.");
        stdout.WriteLine("  -e, --extension <ext>                 Extension on the instance code.");
        stdout.WriteLine("  -E, --no-extension                    Remove extension from instance.");
        stdout.WriteLine("  -t, --techname <name>                 New techName/pmTitle.");
        stdout.WriteLine("  -i, --infoname <name>                 New infoName.");
        stdout.WriteLine("  -V, --infoname-variant <variant>      New info name variant.");
        stdout.WriteLine("  -!, --no-infoname                     Remove infoName.");
        stdout.WriteLine("  -l, --language <lang>                 Language of the instance.");
        stdout.WriteLine("  -n, --issue <iss>                     Issue and inwork numbers.");
        stdout.WriteLine("  -I, --date <date>                     Issue date (- for today).");
        stdout.WriteLine("  -z, --issue-type <type>               Issue type.");
        stdout.WriteLine("  -u, --security <sec>                  Security classification.");
        stdout.WriteLine("  -m, --remarks <remarks>               Remarks for the instance.");
        stdout.WriteLine("  -R, --cir <CIR>                       Resolve externalized items using the given CIR (* to auto-find).");
        stdout.WriteLine("  -x, --xsl <XSL>                       Custom XSLT to resolve the preceding CIR's references.");
        stdout.WriteLine("  -D, --dump <CIR type>                 Dump the built-in XSLT for a CIR type and exit.");
        stdout.WriteLine("  -d, --dir <dir>                       Directory to search for CIRs/ACTs/PCTs.");
        stdout.WriteLine("  -r, --recursive                       Search for CIRs recursively.");
        stdout.WriteLine("  -S, --no-source-ident                 Do not add a sourceDmIdent/sourcePmIdent.");
        stdout.WriteLine("  -3, --no-repository-ident             Do not add a repositorySourceDmIdent.");
        stdout.WriteLine("  -W, --set-applic                      Overwrite whole-object applicability.");
        stdout.WriteLine("  -y, --update-applic                   Set whole-object applicability from the defs.");
        stdout.WriteLine("  -Y, --applic <text>                   Set whole-object applic with display text.");
        stdout.WriteLine("  -g, --set-orig                        Set originator to identify this tool.");
        stdout.WriteLine("  -G, --custom-orig <NCAGE/name>        Set a custom NCAGE/name originator.");
        stdout.WriteLine("  -k, --skill <level>                   Set the skill level of the instance.");
        stdout.WriteLine("  -C, --comment <comment>               Add an XML comment to the instance.");
        stdout.WriteLine("  -X, --comment-xpath <xpath>           XPath where the -C comment is inserted.");
        stdout.WriteLine("  -M, --fix-acronyms                    Keep acronyms valid after filtering.");
        stdout.WriteLine("  -j, --clean-ents                      Remove unused external entities.");
        stdout.WriteLine("  -Z, --add-required                    Fix certain elements after filtering.");
        stdout.WriteLine("  -w, --whole-objects                   Check the status of the whole object.");
        stdout.WriteLine("  -0, --print-non-applic                Print names of non-applicable objects.");
        stdout.WriteLine("  -H, --list-properties <method>        List applicability properties used.");
        stdout.WriteLine("  -L, --list                            Treat input as a list of objects.");
        stdout.WriteLine("  -%, --read-only                       Make output instances read-only.");
        stdout.WriteLine("  -P, --pct <PCT>                       PCT file to read products from.");
        stdout.WriteLine("  -p, --product <product>               ID/primary key of a product in the PCT to filter on.");
        stdout.WriteLine("  -1, --act <ACT>                       Use the given ACT data module.");
        stdout.WriteLine("  -2, --cct <CCT>                       Use the given CCT data module.");
        stdout.WriteLine("  -o, --out <file>                      Output to file instead of stdout.");
        stdout.WriteLine("  -O, --outdir <dir>                    Output to dir with an automatically generated filename.");
        stdout.WriteLine("  -5, --print                           Print the names of the output files.");
        stdout.WriteLine("  -N, --omit-issue                      Omit issue/inwork from automatic filenames.");
        stdout.WriteLine("  -F, --flatten-alts                    Flatten alts elements with a single child.");
        stdout.WriteLine("  -4, --flatten-alts-refs               Flatten alts and fix internalRefTargetType.");
        stdout.WriteLine("  -Q, --resolve-containers              Resolve references to container data modules.");
        stdout.WriteLine("  -@, --update-instances                Update existing instances from their source.");
        stdout.WriteLine("  -8, --reapply                         Reapply the source object's applicability.");
        stdout.WriteLine("  -7, --dry-run                         Do not actually create or update any instances.");
        stdout.WriteLine("  -f, --overwrite                       Overwrite output files.");
        stdout.WriteLine("  -q, --quiet                           Quiet mode.");
        stdout.WriteLine("  -v, --verbose                         Verbose output.");
        stdout.WriteLine("  -h, -?, --help                        Show help.");
        stdout.WriteLine("      --version                         Show version.");
        stdout.WriteLine("  <object>...                           Source CSDB object(s).");
    }

    // ---- CIR / PCT support helpers ----

    /// <summary>A CIR reference (from -R) and an optional custom resolution XSLT (-x).</summary>
    private sealed class CirSpec
    {
        public CirSpec(string file) => File = file;
        public string File { get; }
        public string? XslFile { get; set; }
    }

    /// <summary>
    /// Find CIR data modules under <paramref name="searchDir"/> (mirrors
    /// <c>find_cirs</c> + <c>auto_add_cirs</c>), keeping only the latest issue of
    /// each. Files are matched by the <c>DMC-</c> prefix and verified to contain a
    /// repository.
    /// </summary>
    private static List<string> FindCirs(string searchDir, bool recursive)
    {
        var found = new List<string>();
        if (!Directory.Exists(searchDir))
        {
            return found;
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(searchDir, "*", option);
        }
        catch (IOException)
        {
            return found;
        }

        foreach (string path in candidates)
        {
            string name = Path.GetFileName(path);
            if (Csdb.IsDataModule(name) && Instance.IsCir(path))
            {
                found.Add(path);
            }
        }

        // Use only the latest issue of each CIR.
        return Csdb.ExtractLatestObjects(found);
    }

    /// <summary>
    /// Locate this data module's PCT (via its ACT) and assign the product's
    /// applicability into <paramref name="defs"/> on a per-DM basis. The ACT is
    /// taken from <paramref name="userAct"/> if supplied, otherwise resolved from
    /// the DM's applicCrossRefTableRef. Returns true if any per-DM definitions
    /// were added.
    /// </summary>
    private static bool LoadPctPerDm(XmlDocument doc, XmlElement defs, string product,
        string? userAct, string searchDir, bool recursive, bool quiet, TextWriter stderr)
    {
        XmlDocument? act = null;
        if (userAct != null)
        {
            try { act = XmlUtils.ReadDoc(userAct); }
            catch (Exception ex) when (ex is IOException or XmlException or FileNotFoundException) { act = null; }
        }
        else
        {
            string? actFile = FindRefDmFile(
                doc.SelectSingleNode("//applicCrossRefTableRef/dmRef/dmRefIdent|//actref/refdm"),
                searchDir, recursive);
            if (actFile != null)
            {
                try { act = XmlUtils.ReadDoc(actFile); }
                catch (Exception ex) when (ex is IOException or XmlException) { act = null; }
            }
        }

        if (act == null)
        {
            return false;
        }

        string? pctFile = FindRefDmFile(
            act.SelectSingleNode("//productCrossRefTableRef/dmRef/dmRefIdent|//pctref/refdm"),
            searchDir, recursive);
        if (pctFile == null)
        {
            return false;
        }

        XmlDocument pct;
        try { pct = XmlUtils.ReadDoc(pctFile); }
        catch (Exception ex) when (ex is IOException or XmlException) { return false; }

        int n = Instance.LoadApplicFromPct(defs, pct, product, perDm: true);
        if (n == 0 && !quiet)
        {
            stderr.WriteLine($"s1kd-instance: WARNING: No product matching '{product}' in PCT '{pctFile}'.");
        }
        return n > 0;
    }

    /// <summary>
    /// Build the CSDB filename code from a dmRefIdent and locate the matching data
    /// module under the search directory. A focused port of
    /// <c>find_dmod_fname</c> + <c>find_csdb_object</c> for ACT/PCT resolution.
    /// </summary>
    private static string? FindRefDmFile(XmlNode? dmRefIdent, string searchDir, bool recursive)
    {
        if (dmRefIdent == null)
        {
            return null;
        }
        XmlNode? dmCode = dmRefIdent.SelectSingleNode("dmCode|avee");
        if (dmCode is not XmlElement c)
        {
            return null;
        }

        string V(string attr) => c.GetAttribute(attr);
        string code =
            $"DMC-{V("modelIdentCode")}-{V("systemDiffCode")}-{V("systemCode")}-" +
            $"{V("subSystemCode")}{V("subSubSystemCode")}-{V("assyCode")}-" +
            $"{V("disassyCode")}{V("disassyCodeVariant")}-" +
            $"{V("infoCode")}{V("infoCodeVariant")}-{V("itemLocationCode")}";

        if (!Directory.Exists(searchDir))
        {
            return null;
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(searchDir, code + "*", option);
        }
        catch (IOException)
        {
            return null;
        }

        // Prefer the latest matching issue.
        var matches = candidates.Where(p => Csdb.IsDataModule(Path.GetFileName(p))).ToList();
        if (matches.Count == 0)
        {
            return null;
        }
        return Csdb.ExtractLatestObjects(matches).FirstOrDefault() ?? matches[0];
    }

    /// <summary>
    /// Locate the master object file an instance was derived from, via its
    /// <c>sourceDmIdent</c> / <c>sourcePmIdent</c> / <c>srcdmaddres</c>. A focused
    /// port of <c>find_source</c> (the node lookup is in
    /// <see cref="Instance.FindSourceIdent"/>; the file search is here).
    /// </summary>
    private static string? FindSourceFile(XmlDocument inst, string searchDir, bool recursive)
    {
        XmlNode? sdi = Instance.FindSourceIdent(inst);
        if (sdi == null)
        {
            return null;
        }

        if (sdi.LocalName is "sourceDmIdent" or "srcdmaddres")
        {
            return FindRefDmFile(sdi, searchDir, recursive);
        }

        if (sdi.LocalName == "sourcePmIdent")
        {
            return FindRefPmFile(sdi, searchDir, recursive);
        }

        return null;
    }

    /// <summary>
    /// Build the CSDB filename code from a pmCode-bearing ident and locate the
    /// matching publication module. Companion to <see cref="FindRefDmFile"/> for
    /// PM source idents.
    /// </summary>
    private static string? FindRefPmFile(XmlNode pmRefIdent, string searchDir, bool recursive)
    {
        XmlNode? pmCode = pmRefIdent.SelectSingleNode("pmCode|pmc");
        if (pmCode is not XmlElement c)
        {
            return null;
        }

        string V(string attr) => c.GetAttribute(attr);
        string code = $"PMC-{V("modelIdentCode")}-{V("pmIssuer")}-{V("pmNumber")}-{V("pmVolume")}";

        if (!Directory.Exists(searchDir))
        {
            return null;
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(searchDir, code + "*", option);
        }
        catch (IOException)
        {
            return null;
        }

        var matches = candidates.Where(p => Csdb.IsPublicationModule(Path.GetFileName(p))).ToList();
        if (matches.Count == 0)
        {
            return null;
        }
        return Csdb.ExtractLatestObjects(matches).FirstOrDefault() ?? matches[0];
    }
}
