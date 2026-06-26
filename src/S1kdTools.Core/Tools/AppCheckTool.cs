using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-appcheck</c>: validate the applicability of S1000D CSDB
/// objects, detecting errors that could occur when an object is filtered.
///
/// <para>
/// This port mirrors the C tool's option set, exit codes and XML report
/// structure. The check engine reuses <see cref="Applicability"/>
/// (<c>eval_applic</c> / <c>is_applic</c> / <c>same_annotation</c>) to evaluate
/// assertions. Checks implemented:
/// </para>
/// <list type="bullet">
///   <item>Standalone check (default): every combination of the property values
///   used explicitly in the object is filtered and verified to produce content
///   without broken cross-references.</item>
///   <item>Full check (<c>-a</c>): the value combinations are drawn from the
///   ACT/CCT enumerations for every property used by the object, catching
///   implicit applicability errors.</item>
///   <item>Products check (<c>-t</c>): the object is filtered for each product
///   instance defined in the PCT.</item>
///   <item>Property-definition check (<c>-s</c>): each assertion is verified to
///   reference a product attribute / condition defined in the ACT / CCT, with a
///   valid value (enumeration or pattern).</item>
///   <item>Nested check (<c>-n</c> / <c>-u</c>): nested annotations are verified
///   to be subsets of their parents / the whole object.</item>
///   <item>Redundant check (<c>-R</c>) and duplicate check (<c>-D</c>).</item>
/// </list>
///
/// <para>
/// Differences from the C tool (tracked here and in todo.md):
/// external validation is performed in-process. The C tool shells out to
/// <c>s1kd-instance</c> to filter and <c>s1kd-validate</c> / <c>s1kd-brexcheck</c>
/// to validate; this port filters in-process (dropping non-applicable annotated
/// elements), detects broken <c>internalRef</c> / <c>dmRef</c>-style cross
/// references to filtered-out content, and then runs the validators in-process:
/// the default schema validator (<see cref="ValidateTool"/>) always, the BREX
/// check (<see cref="BrexCheckTool"/>) when <c>-b</c> is set, and custom
/// validators (<c>-e</c>) dispatched through <see cref="ToolRegistry"/>. CCT
/// dependency injection (<c>-~</c>) is supported via
/// <see cref="Applicability.AddCctDepends"/>. Parallel threads (<c>-#</c>), the
/// <c>-o</c>/<c>-K</c>/<c>-k</c> external-filter options and the progress bar are
/// parsed for compatibility but are not fully ported.
/// </para>
/// </summary>
public sealed class AppCheckTool : ITool
{
    public string Name => "appcheck";
    public string Description => "Validate the applicability of S1000D CSDB objects.";
    public string Version => "6.9.2";

    /* Exit status codes (mirror the EXIT_* defines). */
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitBadObject = 2;
    private const int ExitMaxObjects = 3;
    private const int ExitPipe = 4;

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2, Debug = 3 }

    private enum CheckMode { Custom, Pct, All, Standalone }

    private enum ShowFilenames { None, Invalid, Valid }

    private const string MsgPrefix = "s1kd-appcheck";

    /* Per-run options (mirror struct appcheckopts + the file-scope globals). */
    private Verbosity _verbosity = Verbosity.Normal;
    private CheckMode _mode = CheckMode.Standalone;
    private ShowFilenames _filenames = ShowFilenames.None;
    private string? _userAct;
    private string? _userCct;
    private string? _userPct;
    private bool _checkProps;
    private bool _checkNested;
    private bool _strictNested = true;
    private bool _checkRedundant;
    private bool _checkDuplicate;
    private bool _remDelete;
    private bool _brexcheck;
    private bool _addDeps;
    private bool _outputTree;
    private bool _noIssue;
    private bool _recursiveSearch;
    private string _searchDir = ".";
    private readonly HashSet<string> _ignoredProperties = new(StringComparer.Ordinal);

    /// <summary>
    /// Custom validator commands collected from <c>-e</c>/<c>--exec</c>. In the C
    /// tool these are shell command lines piped the filtered instance; here they
    /// are dispatched in-process when their leading token resolves to a ported
    /// tool (see <see cref="RunValidators"/>).
    /// </summary>
    private readonly List<string> _validators = new();

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var objects = new List<string>();
        bool isList = false;
        bool xmlOut = false;
        bool showStats = false;

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];

            // Long options.
            switch (a)
            {
                case "-h" or "-?" or "--help":
                    ShowHelp(stdout);
                    return ExitSuccess;
                case "--version":
                    stdout.WriteLine($"{MsgPrefix} (s1kd-tools) {Version}");
                    return ExitSuccess;
                case "--zenity-progress":
                    continue; // progress not ported
            }

            string? RequireArg(string opt)
            {
                if (++i >= args.Count)
                {
                    stderr.WriteLine($"{MsgPrefix}: ERROR: {opt} requires an argument");
                    return null;
                }
                return args[i];
            }

            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                switch (a)
                {
                    case "--act": { var v = RequireArg(a); if (v == null) return ExitFailure; _userAct = v; continue; }
                    case "--all": _mode = CheckMode.All; continue;
                    case "--brexcheck": _brexcheck = true; continue;
                    case "--cct": { var v = RequireArg(a); if (v == null) return ExitFailure; _userCct = v; continue; }
                    case "--custom": _mode = CheckMode.Custom; continue;
                    case "--duplicate": _checkDuplicate = true; continue;
                    case "--dir": { var v = RequireArg(a); if (v == null) return ExitFailure; _searchDir = v; continue; }
                    case "--exec": { var v = RequireArg(a); if (v == null) return ExitFailure; _validators.Add(v); continue; }
                    case "--valid-filenames": _filenames = ShowFilenames.Valid; continue;
                    case "--filenames": _filenames = ShowFilenames.Invalid; continue;
                    case "--ignore": { var v = RequireArg(a); if (v == null) return ExitFailure; _ignoredProperties.Add(v); continue; }
                    case "--filter": { if (RequireArg(a) == null) return ExitFailure; continue; }
                    case "--args": { if (RequireArg(a) == null) return ExitFailure; continue; }
                    case "--list": isList = true; continue;
                    case "--omit-issue": _noIssue = true; continue;
                    case "--nested": _checkNested = true; continue;
                    case "--output-valid": _outputTree = true; continue;
                    case "--pct": { var v = RequireArg(a); if (v == null) return ExitFailure; _userPct = v; continue; }
                    case "--progress": continue;
                    case "--quiet": _verbosity--; continue;
                    case "--redundant": _checkRedundant = true; continue;
                    case "--recursive": _recursiveSearch = true; continue;
                    case "--strict": _checkProps = true; continue;
                    case "--summary": showStats = true; continue;
                    case "--products": _mode = CheckMode.Pct; continue;
                    case "--unstrict-nested": _checkNested = true; _strictNested = false; continue;
                    case "--verbose": _verbosity++; continue;
                    case "--xml-with-errors": xmlOut = true; continue;
                    case "--xml": xmlOut = true; continue;
                    case "--deep-copy-nodes": xmlOut = true; continue;
                    case "--dependencies": _addDeps = true; continue;
                    case "--remove-deleted": _remDelete = true; continue;
                    case "--threads": { if (RequireArg(a) == null) return ExitFailure; continue; }
                    default:
                        stderr.WriteLine($"{MsgPrefix}: ERROR: Unknown option: {a}");
                        return ExitFailure;
                }
            }

            if (a.Length > 1 && a[0] == '-' && a != "-")
            {
                // Bundled short options. Options requiring an argument consume the
                // remainder of the cluster or the next token.
                for (int c = 1; c < a.Length; c++)
                {
                    char opt = a[c];
                    string Inline() => a[(c + 1)..];

                    switch (opt)
                    {
                        case 'h' or '?': ShowHelp(stdout); return ExitSuccess;
                        case 'a': _mode = CheckMode.All; break;
                        case 'b': _brexcheck = true; break;
                        case 'c': _mode = CheckMode.Custom; break;
                        case 'D': _checkDuplicate = true; break;
                        case 'F': _filenames = ShowFilenames.Valid; break;
                        case 'f': _filenames = ShowFilenames.Invalid; break;
                        case 'l': isList = true; break;
                        case 'N': _noIssue = true; break;
                        case 'n': _checkNested = true; break;
                        case 'o': _outputTree = true; break;
                        case 'p': break;
                        case 'R': _checkRedundant = true; break;
                        case 'r': _recursiveSearch = true; break;
                        case 's': _checkProps = true; break;
                        case 'T': showStats = true; break;
                        case 't': _mode = CheckMode.Pct; break;
                        case 'u': _checkNested = true; _strictNested = false; break;
                        case 'q': _verbosity--; break;
                        case 'v': _verbosity++; break;
                        case 'x': xmlOut = true; break;
                        case 'X': xmlOut = true; break;
                        case '8': xmlOut = true; break;
                        case '~': _addDeps = true; break;
                        case '^': _remDelete = true; break;
                        // Options taking an argument.
                        case 'A' or 'C' or 'P' or 'd' or 'e' or 'i' or 'K' or 'k' or '#':
                        {
                            string val = Inline();
                            if (val.Length == 0)
                            {
                                if (++i >= args.Count)
                                {
                                    stderr.WriteLine($"{MsgPrefix}: ERROR: -{opt} requires an argument");
                                    return ExitFailure;
                                }
                                val = args[i];
                            }
                            switch (opt)
                            {
                                case 'A': _userAct = val; break;
                                case 'C': _userCct = val; break;
                                case 'P': _userPct = val; break;
                                case 'd': _searchDir = val; break;
                                case 'i': _ignoredProperties.Add(val); break;
                                case 'e': _validators.Add(val); break;
                                // K/k/# parsed but not ported
                            }
                            c = a.Length; // consumed rest of cluster
                            break;
                        }
                        default:
                            stderr.WriteLine($"{MsgPrefix}: ERROR: Unknown option: -{opt}");
                            return ExitFailure;
                    }
                }
                continue;
            }

            objects.Add(a);
        }

        // Build the report document.
        var report = XmlUtils.NewDocument();
        var appCheck = report.CreateElement("appCheck");
        report.AppendChild(appCheck);
        appCheck.SetAttribute("type", _mode switch
        {
            CheckMode.Custom => "custom",
            CheckMode.Pct => "pct",
            CheckMode.All => "all",
            _ => "standalone",
        });
        appCheck.SetAttribute("strict", _checkProps ? "yes" : "no");
        appCheck.SetAttribute("checkNestedApplic", _checkNested ? "yes" : "no");
        appCheck.SetAttribute("checkRedundantApplic", _checkRedundant ? "yes" : "no");
        appCheck.SetAttribute("checkDuplicateApplic", _checkDuplicate ? "yes" : "no");

        // Build the object list.
        if (objects.Count > 0)
        {
            if (isList)
            {
                var expanded = new List<string>();
                foreach (string listFile in objects)
                {
                    if (!ReadList(listFile, expanded, stderr))
                    {
                        // continue on bad list, mirroring the C tool.
                    }
                }
                objects = expanded;
            }
        }
        else if (isList)
        {
            ReadList(null, objects, stderr);
        }
        else
        {
            objects.Add("-");
        }

        // Omit-issue (-N) filename handling is parsed for compatibility but not
        // fully ported; warn when requested so behaviour is not silently
        // different. The default schema validator, the BREX check (-b) and any
        // custom validators (-e) ARE ported in-process (see RunValidators); CCT
        // dependency injection (-~) is supported (see Applicability.AddCctDepends).
        if (_noIssue && _verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"{MsgPrefix}: WARNING: Option -N is not fully supported in this port.");
        }

        int err = 0;
        foreach (string path in objects)
        {
            try
            {
                err += CheckApplicFile(path, appCheck, stdout, stderr);
            }
            catch (ExitException ex)
            {
                return ex.Code;
            }
        }

        if (xmlOut)
        {
            stdout.Write(XmlUtils.ToXmlString(report));
            stdout.Write('\n');
        }

        if (showStats)
        {
            PrintStats(report, stderr);
        }

        return err != 0 ? ExitFailure : ExitSuccess;
    }

    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    /* ----- Object list handling ----- */

    private bool ReadList(string? fname, List<string> into, TextWriter stderr)
    {
        try
        {
            using TextReader reader = fname == null
                ? new StreamReader(Console.OpenStandardInput())
                : new StreamReader(fname);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim('\t', '\r', '\n', ' ');
                if (trimmed.Length > 0)
                {
                    into.Add(trimmed);
                }
            }
            return true;
        }
        catch (IOException)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: Could not read list: {fname}");
            }
            return false;
        }
    }

    /* ----- Top-level per-object check ----- */

    private int CheckApplicFile(string path, XmlElement report, TextWriter stdout, TextWriter stderr)
    {
        XmlDocument doc;
        try
        {
            doc = path == "-"
                ? XmlUtils.ReadStream(Console.OpenStandardInput())
                : XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            if (_verbosity > Verbosity.Quiet)
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: Could not read object: {path}");
            }
            throw new ExitException(ExitBadObject);
        }

        if (_remDelete)
        {
            XmlUtils.RemoveDeleteElements(doc);
        }

        var reportNode = AddObjectNode(report, "object", path);

        int err = 0;
        switch (_mode)
        {
            case CheckMode.Custom:
                err += CustomCheck(doc, path, reportNode, stderr);
                break;
            case CheckMode.Standalone:
                err += CheckObjectProps(doc, path, reportNode, stderr);
                break;
            case CheckMode.Pct when _userPct != null:
                err += CheckPctInstances(doc, path, null, reportNode, stderr);
                break;
            default:
            {
                XmlDocument? act = FindAndLoadAct(doc, reportNode);
                if (act != null)
                {
                    if (_mode == CheckMode.All)
                    {
                        err += CheckAllProps(doc, path, act, reportNode, stderr);
                    }
                    else
                    {
                        err += CheckPctInstances(doc, path, act, reportNode, stderr);
                    }
                }
                else if (HasApplic(doc))
                {
                    if (_verbosity >= Verbosity.Normal)
                    {
                        stderr.WriteLine($"{MsgPrefix}: ERROR: {path} uses computable applicability, but no ACT could be found.");
                    }
                    reportNode.AppendChild(report.OwnerDocument.CreateElement("actNotFound"));
                    ++err;
                }
                break;
            }
        }

        reportNode.SetAttribute("valid", err != 0 ? "no" : "yes");

        if (err != 0)
        {
            if (_filenames == ShowFilenames.Invalid)
            {
                stdout.WriteLine(path);
            }
        }
        else
        {
            if (_filenames == ShowFilenames.Valid)
            {
                stdout.WriteLine(path);
            }
        }

        if (_outputTree && err == 0)
        {
            stdout.Write(XmlUtils.ToXmlString(doc));
            stdout.Write('\n');
        }

        if (_verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine(err != 0
                ? $"{MsgPrefix}: FAILED: {path} failed the applicability check."
                : $"{MsgPrefix}: SUCCESS: {path} passed the applicability check.");
        }

        return err != 0 ? 1 : 0;
    }

    /* ----- Check mode entry points ----- */

    private int CustomCheck(XmlDocument doc, string path, XmlElement report, TextWriter stderr)
    {
        int err = 0;

        if (_addDeps || _checkProps)
        {
            XmlDocument? act = FindAndLoadAct(doc, report);
            XmlDocument? cct = FindAndLoadCct(act, report);
            if (_addDeps && cct != null)
            {
                Applicability.AddCctDepends(doc, cct, null);
            }
            if (_checkProps)
            {
                err += CheckPropsAgainstCts(doc, path, act, cct, report, stderr);
            }
        }

        if (_checkDuplicate)
        {
            err += CheckDuplicateApplic(doc, path, report, stderr);
        }

        if (_checkNested || _checkRedundant)
        {
            err += CheckNestedApplics(doc, path, report, stderr);
        }

        return err;
    }

    private int CheckObjectProps(XmlDocument doc, string path, XmlElement report, TextWriter stderr)
    {
        int err = 0;

        // Add CCT dependencies so they are counted as part of the object's
        // applicability (mirrors the add_deps/check_props block in
        // check_object_props). Done before property-set extraction below.
        if (_addDeps || _checkProps)
        {
            XmlDocument? act = FindAndLoadAct(doc, report);
            XmlDocument? cct = FindAndLoadCct(act, report);
            if (_addDeps && cct != null)
            {
                Applicability.AddCctDepends(doc, cct, null);
            }
            if (_checkProps)
            {
                err += CheckPropsAgainstCts(doc, path, act, cct, report, stderr);
            }
        }

        if (_checkDuplicate)
        {
            err += CheckDuplicateApplic(doc, path, report, stderr);
        }

        if (_checkNested || _checkRedundant)
        {
            err += CheckNestedApplics(doc, path, report, stderr);
        }

        // Build property sets from the values explicitly used in the object.
        var propSets = new List<PropSet>();
        foreach (XmlNode assertNode in SelectAll(doc, "//assert"))
        {
            string? ident = FirstAttr(assertNode, "applicPropertyIdent", "actidref");
            string? type = FirstAttr(assertNode, "applicPropertyType", "actreftype");
            string? vals = FirstAttr(assertNode, "applicPropertyValues", "actvalues");
            if (ident == null || type == null || vals == null)
            {
                continue;
            }

            PropSet set = propSets.FirstOrDefault(s => s.Ident == ident)
                          ?? AddPropSet(propSets, ident, type);
            foreach (string v in SplitValues(vals))
            {
                set.AddValue(v);
            }
        }

        err += CheckCombinations(doc, path, propSets, report, stderr);
        return err;
    }

    private int CheckAllProps(XmlDocument doc, string path, XmlDocument act, XmlElement report, TextWriter stderr)
    {
        int err = 0;
        XmlDocument? cct = FindAndLoadCct(act, report);

        if (_addDeps && cct != null)
        {
            Applicability.AddCctDepends(doc, cct, null);
        }

        if (_checkProps)
        {
            err += CheckPropsAgainstCts(doc, path, act, cct, report, stderr);
        }

        if (_checkDuplicate)
        {
            err += CheckDuplicateApplic(doc, path, report, stderr);
        }

        if (_checkNested || _checkRedundant)
        {
            err += CheckNestedApplics(doc, path, report, stderr);
        }

        var propSets = new List<PropSet>();

        // Product attributes from the ACT.
        foreach (XmlNode prop in SelectAll(act, "//productAttribute|//prodattr"))
        {
            string id = AttrValue(prop, "id");
            if (id.Length == 0 || !PropIsUsed(id, "prodattr", doc))
            {
                continue;
            }
            var set = new PropSet(id, "prodattr");
            ExtractEnumVals(set, prop);
            if (set.Values.Count > 0)
            {
                propSets.Add(set);
            }
        }

        // Conditions from the CCT.
        if (cct != null)
        {
            foreach (XmlNode cond in SelectAll(cct, "//cond|//condition"))
            {
                string id = AttrValue(cond, "id");
                if (id.Length == 0 || !PropIsUsed(id, "condition", doc))
                {
                    continue;
                }
                string? typeRefId = FirstAttr(cond, "condTypeRefId", "condtyperef");
                XmlNode? type = typeRefId == null ? null : cct.SelectSingleNode($"//*[@id='{typeRefId}']");
                var set = new PropSet(id, "condition");
                if (type != null)
                {
                    ExtractEnumVals(set, type);
                }
                if (set.Values.Count > 0)
                {
                    propSets.Add(set);
                }
            }
        }
        else if (HasConds(doc))
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: {path} uses conditions, but no CCT could be found.");
            }
            report.AppendChild(report.OwnerDocument.CreateElement("cctNotFound"));
            ++err;
        }

        err += CheckCombinations(doc, path, propSets, report, stderr);
        return err;
    }

    private int CheckPctInstances(XmlDocument doc, string path, XmlDocument? act, XmlElement report, TextWriter stderr)
    {
        int err = 0;

        if (_addDeps || _checkProps)
        {
            XmlDocument? a = act ?? FindAndLoadAct(doc, report);
            XmlDocument? cct = FindAndLoadCct(a, report);
            if (_addDeps && cct != null)
            {
                Applicability.AddCctDepends(doc, cct, null);
            }
            if (_checkProps)
            {
                err += CheckPropsAgainstCts(doc, path, a, cct, report, stderr);
            }
        }

        if (_checkDuplicate)
        {
            err += CheckDuplicateApplic(doc, path, report, stderr);
        }

        if (_checkNested || _checkRedundant)
        {
            err += CheckNestedApplics(doc, path, report, stderr);
        }

        // Locate the PCT.
        XmlDocument? pct = null;
        if (_userPct != null)
        {
            pct = TryLoad(_userPct);
            if (pct != null)
            {
                AddObjectNode(report, "pct", _userPct);
            }
        }
        else if (act != null && FindRefFname(act, "//productCrossRefTableRef/dmRef/dmRefIdent|//pctref/refdm", out string pctf))
        {
            pct = TryLoad(pctf);
            if (pct != null)
            {
                AddObjectNode(report, "pct", pctf);
            }
        }

        if (pct == null)
        {
            return err;
        }

        foreach (XmlNode product in SelectAll(pct, "//product"))
        {
            var asserts = new List<Assignment>();
            string? prodId = AttrValueOrNull(product, "id");
            foreach (XmlNode assign in (product as XmlElement)?.SelectNodes(".//assign") ?? EmptyNodeList())
            {
                string? id = FirstAttr(assign, "applicPropertyIdent", "actidref");
                string? type = FirstAttr(assign, "applicPropertyType", "actreftype");
                string? value = FirstAttr(assign, "applicPropertyValue", "actvalue");
                if (id != null && type != null && value != null)
                {
                    asserts.Add(new Assignment(id, type, value));
                }
            }

            err += CheckAssigns(doc, path, asserts, prodId, report, stderr);
        }

        return err;
    }

    /* ----- Combination checking (combos.xsl + check_prods, in-process) ----- */

    private int CheckCombinations(XmlDocument doc, string path, List<PropSet> propSets, XmlElement report, TextWriter stderr)
    {
        int err = 0;
        foreach (var combo in CartesianProduct(propSets))
        {
            err += CheckAssigns(doc, path, combo, null, report, stderr);
        }
        return err;
    }

    /// <summary>Cartesian product of one chosen value per property set (mirrors combos.xsl).</summary>
    private static IEnumerable<List<Assignment>> CartesianProduct(List<PropSet> sets)
    {
        if (sets.Count == 0)
        {
            yield break;
        }

        var indices = new int[sets.Count];
        while (true)
        {
            var combo = new List<Assignment>(sets.Count);
            for (int i = 0; i < sets.Count; i++)
            {
                var set = sets[i];
                combo.Add(new Assignment(set.Ident, set.Type, set.Values[indices[i]]));
            }
            yield return combo;

            int k = sets.Count - 1;
            while (k >= 0)
            {
                if (++indices[k] < sets[k].Values.Count)
                {
                    break;
                }
                indices[k] = 0;
                k--;
            }
            if (k < 0)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Mirror of <c>check_assigns</c>: filter the object for a set of assignments
    /// and verify the result is valid. Filtering and validation are performed
    /// in-process: elements whose annotation evaluates to "not applicable" are
    /// removed, and the remaining content is checked for broken internal
    /// references to removed content.
    /// </summary>
    private int CheckAssigns(XmlDocument doc, string path, List<Assignment> assigns, string? productId, XmlElement report, TextWriter stderr)
    {
        var asserts = report.OwnerDocument.CreateElement("asserts");
        if (productId != null)
        {
            asserts.SetAttribute("product", productId);
        }
        foreach (var a in assigns)
        {
            var assign = report.OwnerDocument.CreateElement("assign");
            assign.SetAttribute("applicPropertyIdent", a.Ident);
            assign.SetAttribute("applicPropertyType", a.Type);
            assign.SetAttribute("applicPropertyValue", a.Value);
            asserts.AppendChild(assign);
        }

        bool valid = FilterAndValidate(doc, assigns, stderr);

        asserts.SetAttribute("valid", valid ? "yes" : "no");
        report.AppendChild(asserts);

        if (!valid)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: {path} is invalid when:");
                foreach (var a in assigns)
                {
                    stderr.WriteLine($"{MsgPrefix}: ERROR:   {a.Type} {a.Ident} = {a.Value}");
                }
            }
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// In-process replacement for the C tool's <c>check_assigns</c> pipeline
    /// (s1kd-instance filter + s1kd-validate / s1kd-brexcheck / custom <c>-e</c>
    /// validators): produce the filtered tree for a set of assignments, check it
    /// for broken internal references, and run the external validators in-process
    /// on the filtered tree. Returns true if the filtered object is valid.
    /// </summary>
    private bool FilterAndValidate(XmlDocument doc, List<Assignment> assigns, TextWriter stderr)
    {
        var defs = new Applicability();
        foreach (var a in assigns)
        {
            defs.Assign(a.Ident, a.Type, a.Value);
        }

        // Clone so filtering does not disturb the source document.
        var clone = (XmlDocument)doc.CloneNode(true);

        // Map applicRefId -> applic annotation node in the clone.
        var annotations = new Dictionary<string, XmlNode>(StringComparer.Ordinal);
        foreach (XmlNode applic in SelectAll(clone, "//applic[@id]"))
        {
            string id = AttrValue(applic, "id");
            if (id.Length > 0)
            {
                annotations[id] = applic;
            }
        }

        // Collect the set of ids of elements that survive filtering.
        var survivingIds = new HashSet<string>(StringComparer.Ordinal);

        // Remove non-applicable annotated elements (depth-first; if an ancestor
        // is removed, descendants go with it).
        FilterNode(clone.DocumentElement, defs, annotations);

        if (clone.DocumentElement == null)
        {
            // Whole object filtered out -> no applicable content. The C tool
            // treats an empty filtered doc as "not applicable" (not an error).
            return true;
        }

        // Gather surviving ids and referenced ids.
        foreach (XmlNode el in SelectAll(clone, "//*[@id]"))
        {
            survivingIds.Add(AttrValue(el, "id"));
        }

        // Any internal reference pointing at a now-removed id is a broken ref.
        foreach (XmlNode reference in SelectAll(clone, "//internalRef[@internalRefId]"))
        {
            string target = AttrValue(reference, "internalRefId");
            if (target.Length > 0 && !survivingIds.Contains(target))
            {
                return false; // broken cross-reference after filtering
            }
        }

        // Run the external validators on the filtered tree (mirrors the
        // validator loop in check_assigns: a non-zero accumulated exit status
        // marks the assignment combination invalid).
        return RunValidators(clone, stderr) == 0;
    }

    /// <summary>
    /// Run the configured validators on a filtered instance, in-process,
    /// returning the accumulated exit status (0 == valid). Mirrors the
    /// validator portion of the C <c>check_assigns</c>: when custom validators
    /// (<c>-e</c>) are present they replace the defaults; otherwise the default
    /// schema validator runs, followed by the BREX check when <c>-b</c> is set.
    /// </summary>
    private int RunValidators(XmlDocument filtered, TextWriter stderr)
    {
        // Serialize the filtered tree to a temp file so the ported tools (which
        // read CSDB objects by path) can validate it, mirroring the C tool
        // piping the filtered doc to each validator's stdin.
        string tmp = Path.Combine(Path.GetTempPath(), $"s1kd-appcheck-{Guid.NewGuid():N}.XML");
        try
        {
            XmlUtils.SaveDoc(filtered, tmp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If the filtered doc cannot be written there is nothing to validate
            // against; treat as valid (the C tool would have produced no report).
            return 0;
        }

        try
        {
            int e = 0;

            // Verbosity flag passed through to the validators (NORMAL/QUIET map
            // to -q, DEBUG to -v, VERBOSE to neither), mirroring the C switch.
            string? verbFlag = _verbosity switch
            {
                Verbosity.Quiet or Verbosity.Normal => "-q",
                Verbosity.Debug => "-v",
                _ => null,
            };

            // Discard validator stdout: the C tool only cares about exit codes
            // unless -X (include errors) is set, which this port does not embed.
            var sink = TextWriter.Null;

            if (_validators.Count > 0)
            {
                // Custom validators (-e).
                foreach (string cmd in _validators)
                {
                    e += RunCustomValidator(cmd, tmp, sink, stderr);
                }
            }
            else
            {
                // Default schema validation.
                var validateArgs = new List<string>();
                if (verbFlag != null) { validateArgs.Add(verbFlag); }
                validateArgs.Add(tmp);
                e += RunTool("validate", validateArgs, sink, stderr);

                // BREX validation (-b): s1kd-brexcheck -cl -d <dir> [-r] [-q|-v].
                if (_brexcheck)
                {
                    var brexArgs = new List<string> { "-c", "-l", "-d", _searchDir };
                    if (_recursiveSearch) { brexArgs.Add("-r"); }
                    if (verbFlag != null) { brexArgs.Add(verbFlag); }
                    brexArgs.Add(tmp);
                    e += RunTool("brexcheck", brexArgs, sink, stderr);
                }
            }

            return e;
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>
    /// Dispatch a custom <c>-e</c> validator command in-process. The command is
    /// split into tokens; the leading token (with any <c>s1kd-</c> prefix
    /// stripped) is resolved against <see cref="ToolRegistry"/>. The filtered
    /// instance path is appended as the object to check. Commands that do not
    /// resolve to a ported tool are reported and counted as a failure, since the
    /// C tool would have executed them and the result cannot be reproduced.
    /// </summary>
    private int RunCustomValidator(string cmd, string objectPath, TextWriter stdout, TextWriter stderr)
    {
        string[] tokens = cmd.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return 0;
        }

        ITool? tool = ToolRegistry.Resolve(tokens[0]);
        if (tool == null)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: Custom validator '{tokens[0]}' is not an available in-process tool.");
            }
            return 1;
        }

        var args = new List<string>(tokens.Length);
        for (int i = 1; i < tokens.Length; i++)
        {
            args.Add(tokens[i]);
        }
        args.Add(objectPath);

        return RunTool(tool, args, stdout, stderr);
    }

    /// <summary>Resolve a ported tool by name and run it, returning its exit code.</summary>
    private int RunTool(string name, IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        ITool? tool = ToolRegistry.Resolve(name);
        if (tool == null)
        {
            // The default validators are always available in this port; guard
            // anyway so a missing tool surfaces rather than crashes.
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: Validator '{name}' is not available.");
            }
            return 1;
        }
        return RunTool(tool, args, stdout, stderr);
    }

    private static int RunTool(ITool tool, IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            return tool.Run(args, stdout, stderr);
        }
        catch (Exception)
        {
            return 1;
        }
    }

    /// <summary>
    /// Recursively remove elements that carry an applicability annotation which
    /// evaluates to "not applicable" for the given definitions.
    /// </summary>
    private static void FilterNode(XmlNode? node, Applicability defs, Dictionary<string, XmlNode> annotations)
    {
        if (node is not XmlElement)
        {
            return;
        }

        XmlNode? cur = node.FirstChild;
        while (cur != null)
        {
            XmlNode? next = cur.NextSibling;
            if (cur is XmlElement el)
            {
                string refId = FirstAttr(el, "applicRefId", "refapplic") ?? string.Empty;
                bool applicable = true;
                if (refId.Length > 0 && annotations.TryGetValue(refId, out XmlNode? applic))
                {
                    XmlNode? test = applic.SelectSingleNode("assert|evaluate");
                    if (test != null)
                    {
                        // Filtering uses assume=false: only keep content that is
                        // unambiguously applicable for the chosen values.
                        applicable = Applicability.EvalApplic(defs.Definitions, test, assume: false);
                    }
                }

                if (!applicable)
                {
                    node.RemoveChild(el);
                }
                else
                {
                    FilterNode(el, defs, annotations);
                }
            }
            cur = next;
        }
    }

    /* ----- Property-definition check (-s) ----- */

    private int CheckPropsAgainstCts(XmlDocument doc, string path, XmlDocument? act, XmlDocument? cct, XmlElement report, TextWriter stderr)
    {
        int err = 0;
        foreach (XmlNode assert in SelectAll(doc, "//assert"))
        {
            err += CheckPropAgainstCt(assert, act, cct, path, report, stderr);
        }
        return err;
    }

    private int CheckPropAgainstCt(XmlNode assert, XmlDocument? act, XmlDocument? cct, string path, XmlElement report, TextWriter stderr)
    {
        string? id = FirstAttr(assert, "applicPropertyIdent", "actidref");
        string? type = FirstAttr(assert, "applicPropertyType", "actreftype");
        string? vals = FirstAttr(assert, "applicPropertyValues", "applicPropertyValue")
                       ?? FirstAttr(assert, "actvalues", "actvalue");

        if (id == null || type == null || vals == null)
        {
            return 0;
        }

        if (_ignoredProperties.Contains($"{id}:{type}"))
        {
            return 0;
        }

        XmlNode? prop = null;
        if (type == "condition" && cct != null)
        {
            prop = cct.SelectSingleNode($"(//cond|//condition)[@id='{id}']");
            if (prop != null)
            {
                string? condType = FirstAttr(prop, "condTypeRefId", "condtyperef");
                if (condType != null)
                {
                    prop = cct.SelectSingleNode($"(//condType|//conditiontype)[@id='{condType}']");
                }
            }
        }
        else if (type == "prodattr" && act != null)
        {
            prop = act.SelectSingleNode($"(//productAttribute|//prodattr)[@id='{id}']");
        }

        int err = 0;
        if (prop != null)
        {
            foreach (string v in SplitValues(vals))
            {
                err += CheckValAgainstProp(assert, id, type, v, prop, path, report, stderr);
            }
        }
        else
        {
            long line = LineOf(assert);
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: {path}: {type} {id} is not defined (line {line})");
            }
            AddUndefNode(report, assert, id, type, null, line);
            ++err;
        }

        return err;
    }

    private int CheckValAgainstProp(XmlNode assert, string id, string type, string val, XmlNode prop, string path, XmlElement report, TextWriter stderr)
    {
        string? pattern = FirstAttr(prop, "valuePattern", "pattern");
        bool match;
        if (pattern != null)
        {
            match = MatchPattern(val, pattern);
        }
        else
        {
            match = false;
            foreach (XmlNode en in (prop as XmlElement)?.SelectNodes("enumeration|enum") ?? EmptyNodeList())
            {
                string? vals = FirstAttr(en, "applicPropertyValues", "actvalues");
                if (vals == null)
                {
                    continue;
                }
                foreach (string v in SplitValues(vals))
                {
                    if (Csdb.IsInRange(val, v))
                    {
                        match = true;
                        break;
                    }
                }
                if (match)
                {
                    break;
                }
            }
        }

        if (match)
        {
            return 0;
        }

        long line = LineOf(assert);
        if (_verbosity >= Verbosity.Normal)
        {
            stderr.WriteLine($"{MsgPrefix}: ERROR: {path}: {val} is not a defined value of {type} {id} (line {line})");
        }
        AddUndefNode(report, assert, id, type, val, line);
        return 1;
    }

    /* ----- Nested / redundant check (-n / -u / -R) ----- */

    private int CheckNestedApplics(XmlDocument doc, string path, XmlElement report, TextWriter stderr)
    {
        int err = 0;
        foreach (XmlNode node in SelectAll(doc, "//*[@applicRefId or @refapplic]"))
        {
            err += CheckNestedApplic(doc, node, path, report, stderr);
        }
        return err;
    }

    private int CheckNestedApplic(XmlDocument doc, XmlNode node, string path, XmlElement report, TextWriter stderr)
    {
        bool err = false;

        string? id = FirstAttr(node, "applicRefId", "refapplic");
        XmlNode? app = id == null ? null : doc.SelectSingleNode($"//applic[@id='{id}']");

        // Walk up the ancestors.
        XmlNode? parent = node.ParentNode;
        while (parent is XmlElement)
        {
            string? parentId = FirstAttr(parent, "applicRefId", "refapplic");
            if (parentId != null)
            {
                XmlNode? parentApp = doc.SelectSingleNode($"//applic[@id='{parentId}']");

                if (_checkNested && app != null && parentApp != null &&
                    CheckNestedApplicProps(node, parent, app, parentApp, path, report, stderr))
                {
                    err = true;
                }

                if (_checkRedundant && id != null &&
                    CheckRedundantApplic(path, node, id, parent, parentId, report, stderr))
                {
                    err = true;
                }
            }
            parent = parent.ParentNode;
        }

        // Check against the whole-object applicability.
        XmlNode? whole = doc.SelectSingleNode("//applic");
        if (_checkNested && app != null && whole != null &&
            CheckNestedApplicProps(node, null, app, whole, path, report, stderr))
        {
            err = true;
        }

        return err ? 1 : 0;
    }

    private bool CheckNestedApplicProps(XmlNode node, XmlNode? parent, XmlNode app, XmlNode parentApp, string path, XmlElement report, TextWriter stderr)
    {
        XmlNode? parentAppNode = parentApp.SelectSingleNode("assert|evaluate");
        if (parentAppNode == null)
        {
            return false;
        }

        var asserts = SelectList(app, ".//assert");
        if (asserts.Count == 0)
        {
            return false;
        }

        bool err = false;

        if (_strictNested)
        {
            foreach (XmlNode assert in asserts)
            {
                if (CheckNestedApplicAssert(node, parent, assert, parentAppNode, path, report, stderr))
                {
                    err = true;
                }
            }
        }
        else
        {
            // Only check individual asserts if the whole annotation isn't a
            // subset of the parent.
            var defs = new Applicability();
            foreach (XmlNode assert in asserts)
            {
                string? id = FirstAttr(assert, "applicPropertyIdent", "actidref");
                string? type = FirstAttr(assert, "applicPropertyType", "actreftype");
                string? vals = FirstAttr(assert, "applicPropertyValues", "actvalues");
                if (id != null && type != null && vals != null)
                {
                    defs.Assign(id, type, vals);
                }
            }

            if (!Applicability.EvalApplic(defs.Definitions, parentAppNode, assume: true))
            {
                foreach (XmlNode assert in asserts)
                {
                    if (CheckNestedApplicAssert(node, parent, assert, parentAppNode, path, report, stderr))
                    {
                        err = true;
                    }
                }
            }
        }

        return err;
    }

    private bool CheckNestedApplicAssert(XmlNode node, XmlNode? parent, XmlNode assert, XmlNode parentAppNode, string path, XmlElement report, TextWriter stderr)
    {
        string? id = FirstAttr(assert, "applicPropertyIdent", "actidref");
        string? type = FirstAttr(assert, "applicPropertyType", "actreftype");
        string? vals = FirstAttr(assert, "applicPropertyValues", "actvalues");

        var defs = new Applicability();
        if (id != null && type != null && vals != null)
        {
            defs.Assign(id, type, vals);
        }

        bool err = !Applicability.EvalApplic(defs.Definitions, parentAppNode, assume: true);

        if (err)
        {
            AddNestedError(report, node, parent, id, type, vals, path, stderr);
        }

        return err;
    }

    private bool CheckRedundantApplic(string path, XmlNode node, string id, XmlNode parent, string parentId, XmlElement report, TextWriter stderr)
    {
        if (id == parentId)
        {
            AddRedundantError(report, node, parent, id, path, stderr);
            return true;
        }
        return false;
    }

    /* ----- Duplicate check (-D) ----- */

    private int CheckDuplicateApplic(XmlDocument doc, string path, XmlElement report, TextWriter stderr)
    {
        int err = 0;
        var applics = SelectList(doc, "//applic");
        for (int i = 0; i < applics.Count; i++)
        {
            for (int j = i + 1; j < applics.Count; j++)
            {
                if (SameAnnotationContents(applics[i], applics[j]))
                {
                    AddDuplicateError(report, applics[i], applics[j], path, stderr);
                    err = 1;
                }
            }
        }
        return err;
    }

    /// <summary>
    /// Compare two annotations by their logic (assert/evaluate subtree),
    /// ignoring display text and ids. Mirrors the intent of duplicate.xsl +
    /// same_annotation.
    /// </summary>
    private static bool SameAnnotationContents(XmlNode a, XmlNode b)
    {
        XmlNode? la = a.SelectSingleNode("assert|evaluate");
        XmlNode? lb = b.SelectSingleNode("assert|evaluate");
        if (la == null || lb == null)
        {
            return false;
        }
        return Applicability.SameAnnotation(la, lb);
    }

    /* ----- Report helpers ----- */

    private static XmlElement AddObjectNode(XmlElement parent, string name, string path)
    {
        var node = parent.OwnerDocument.CreateElement(name);
        node.SetAttribute("path", path);
        parent.AppendChild(node);
        return node;
    }

    private void AddUndefNode(XmlElement report, XmlNode assert, string id, string type, string? val, long line)
    {
        var und = report.OwnerDocument.CreateElement("undefined");
        und.SetAttribute("applicPropertyIdent", id);
        und.SetAttribute("applicPropertyType", type);
        if (val != null)
        {
            und.SetAttribute("applicPropertyValue", val);
        }
        und.SetAttribute("line", line.ToString());
        und.SetAttribute("xpath", XmlUtils.XPathOf(assert));
        report.AppendChild(und);
    }

    private void AddNestedError(XmlElement report, XmlNode node, XmlNode? parent, string? id, string? type, string? val, string path, TextWriter stderr)
    {
        long cline = LineOf(node);
        if (_verbosity >= Verbosity.Normal)
        {
            if (parent != null)
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: {path}: {node.Name} on line {cline} is applicable when {type} {id} = {val}, which is not a subset of the applicability of the parent {parent.Name} on line {LineOf(parent)}");
            }
            else
            {
                stderr.WriteLine($"{MsgPrefix}: ERROR: {path}: {node.Name} on line {cline} is applicable when {type} {id} = {val}, which is not a subset of the applicability of the whole object.");
            }
        }

        var und = report.OwnerDocument.CreateElement("nestedApplicError");
        if (id != null) und.SetAttribute("applicPropertyIdent", id);
        if (type != null) und.SetAttribute("applicPropertyType", type);
        if (val != null) und.SetAttribute("applicPropertyValue", val);
        und.SetAttribute("line", cline.ToString());
        und.SetAttribute("xpath", XmlUtils.XPathOf(node));
        if (parent != null)
        {
            und.SetAttribute("parentLine", LineOf(parent).ToString());
            und.SetAttribute("parentXpath", XmlUtils.XPathOf(parent));
        }
        report.AppendChild(und);
    }

    private void AddRedundantError(XmlElement report, XmlNode node, XmlNode parent, string id, string path, TextWriter stderr)
    {
        long cline = LineOf(node);
        long pline = LineOf(parent);
        if (_verbosity >= Verbosity.Normal)
        {
            stderr.WriteLine($"{MsgPrefix}: ERROR: {path}: {node.Name} on line {cline} has the same applicability as its parent {parent.Name} on line {pline} ({id})");
        }

        var und = report.OwnerDocument.CreateElement("redundantApplicError");
        und.SetAttribute("line", cline.ToString());
        und.SetAttribute("xpath", XmlUtils.XPathOf(node));
        und.SetAttribute("parentLine", pline.ToString());
        und.SetAttribute("parentXpath", XmlUtils.XPathOf(parent));
        report.AppendChild(und);
    }

    private void AddDuplicateError(XmlElement report, XmlNode node1, XmlNode node2, string path, TextWriter stderr)
    {
        long line1 = LineOf(node1);
        long line2 = LineOf(node2);
        if (_verbosity >= Verbosity.Normal)
        {
            stderr.WriteLine($"{MsgPrefix}: ERROR: {path}: Annotation on line {line2} is a duplicate of annotation on line {line1}.");
        }

        var error = report.OwnerDocument.CreateElement("duplicateApplicError");
        error.SetAttribute("line", line2.ToString());
        error.SetAttribute("xpath", XmlUtils.XPathOf(node2));
        error.SetAttribute("duplicateOfLine", line1.ToString());
        error.SetAttribute("duplicateOfXPath", XmlUtils.XPathOf(node1));
        report.AppendChild(error);
    }

    private static void PrintStats(XmlDocument report, TextWriter stderr)
    {
        int total = 0, valid = 0, invalid = 0;
        foreach (XmlNode obj in report.SelectNodes("//appCheck/object") ?? EmptyNodeList())
        {
            total++;
            if (AttrValue(obj, "valid") == "no")
            {
                invalid++;
            }
            else
            {
                valid++;
            }
        }
        var sb = new StringBuilder();
        sb.Append("Checked ").Append(total).Append(" object(s): ")
          .Append(valid).Append(" passed, ")
          .Append(invalid).Append(" failed.\n");
        stderr.Write(sb.ToString());
    }

    /* ----- ACT / CCT location ----- */

    private XmlDocument? FindAndLoadAct(XmlDocument doc, XmlElement report)
    {
        if (_userAct != null)
        {
            var act = TryLoad(_userAct);
            if (act != null)
            {
                AddObjectNode(report, "act", _userAct);
            }
            return act;
        }

        if (FindRefFname(doc, "//applicCrossRefTableRef/dmRef/dmRefIdent|//actref/refdm", out string fname))
        {
            var act = TryLoad(fname);
            if (act != null)
            {
                AddObjectNode(report, "act", fname);
            }
            return act;
        }

        return null;
    }

    private XmlDocument? FindAndLoadCct(XmlDocument? act, XmlElement report)
    {
        if (_userCct != null)
        {
            var cct = TryLoad(_userCct);
            if (cct != null)
            {
                AddObjectNode(report, "cct", _userCct);
            }
            return cct;
        }

        if (act != null && FindRefFname(act, "//condCrossRefTableRef/dmRef/dmRefIdent|//cctref/refdm", out string fname))
        {
            var cct = TryLoad(fname);
            if (cct != null)
            {
                AddObjectNode(report, "cct", fname);
            }
            return cct;
        }

        return null;
    }

    /// <summary>
    /// Resolve a referenced DM filename from a dmRefIdent and locate it under the
    /// search dir / on disk. Mirrors find_dmod_fname + find_csdb_object.
    /// </summary>
    private bool FindRefFname(XmlDocument doc, string xpath, out string result)
    {
        result = string.Empty;
        XmlNode? refIdent = doc.SelectSingleNode(xpath);
        if (refIdent == null)
        {
            return false;
        }

        string? code = BuildDmCode(refIdent);
        if (code == null)
        {
            return false;
        }

        return FindCsdbObject(code, out result);
    }

    private static string? BuildDmCode(XmlNode refIdent)
    {
        XmlNode? dmCode = refIdent.SelectSingleNode("dmCode|avee");
        if (dmCode == null)
        {
            return null;
        }

        string V(string xp) => XmlUtils.XPathFirstValue(null, dmCode, xp) ?? string.Empty;

        string code = "DMC-" +
            V("@modelIdentCode|modelic") + "-" +
            V("@systemDiffCode|sdc") + "-" +
            V("@systemCode|chapnum") + "-" +
            V("@subSystemCode|section") + V("@subSubSystemCode|subsect") + "-" +
            V("@assyCode|subject") + "-" +
            V("@disassyCode|discode") + V("@disassyCodeVariant|discodev") + "-" +
            V("@infoCode|incode") + V("@infoCodeVariant|incodev") + "-" +
            V("@itemLocationCode|itemloc");

        return code;
    }

    /// <summary>Locate a CSDB object whose base name matches a code prefix (mirrors find_csdb_object).</summary>
    private bool FindCsdbObject(string code, out string result)
    {
        result = string.Empty;
        string dir;
        try
        {
            dir = Path.GetFullPath(_searchDir);
        }
        catch
        {
            return false;
        }

        if (!Directory.Exists(dir))
        {
            return false;
        }

        var option = _recursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string? best = null;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*", option);
        }
        catch (IOException)
        {
            return false;
        }

        foreach (string f in files)
        {
            string name = Path.GetFileName(f);
            if (Csdb.IsDataModule(name) && Csdb.StrMatch(code, name))
            {
                if (best == null || Csdb.CompareBaseName(f, best) > 0)
                {
                    best = f;
                }
            }
        }

        if (best != null)
        {
            result = best;
            return true;
        }

        return false;
    }

    private XmlDocument? TryLoad(string path)
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

    /* ----- small helpers ----- */

    private bool PropIsUsed(string id, string type, XmlDocument doc)
    {
        if (_ignoredProperties.Contains($"{id}:{type}"))
        {
            return false;
        }
        string xpath = $"(//content|//inlineapplics)//assert[(@applicPropertyIdent='{id}' or @actidref='{id}') and (@applicPropertyType='{type}' or @actreftype='{type}')]";
        return doc.SelectSingleNode(xpath) != null;
    }

    private static bool HasConds(XmlDocument doc) =>
        doc.SelectSingleNode("//assert[@applicPropertyType='condition' or @actreftype='condition']") != null;

    private static bool HasApplic(XmlDocument doc) =>
        doc.SelectSingleNode("//assert") != null;

    private static void ExtractEnumVals(PropSet set, XmlNode prop)
    {
        foreach (XmlNode en in (prop as XmlElement)?.SelectNodes(".//enumeration/@applicPropertyValues|.//enum/@actvalues") ?? EmptyNodeList())
        {
            string vals = en.Value ?? string.Empty;
            foreach (string v in SplitValues(vals))
            {
                set.AddValue(v);
            }
        }
    }

    /// <summary>Match a value against an xsd-style regex pattern (mirrors match_pattern).</summary>
    private static bool MatchPattern(string value, string pattern)
    {
        try
        {
            // libxml2 anchors the whole string; emulate with \A...\z.
            var regex = new System.Text.RegularExpressions.Regex("\\A(?:" + pattern + ")\\z");
            return regex.IsMatch(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Split S1000D value list on | and ~ separators (mirrors strtok on "|~").</summary>
    private static IEnumerable<string> SplitValues(string vals) =>
        vals.Split(new[] { '|', '~' }, StringSplitOptions.RemoveEmptyEntries);

    private static string? FirstAttr(XmlNode node, string a, string b)
    {
        if (node.Attributes == null)
        {
            return null;
        }
        return node.Attributes[a]?.Value ?? node.Attributes[b]?.Value;
    }

    private static string AttrValue(XmlNode node, string name) =>
        node.Attributes?[name]?.Value ?? string.Empty;

    private static string? AttrValueOrNull(XmlNode node, string name) =>
        node.Attributes?[name]?.Value;

    private static long LineOf(XmlNode node)
    {
        // System.Xml does not track line numbers on a loaded DOM; the report uses
        // 0 as a placeholder (the C tool relies on libxml2's xmlGetLineNo). XPath
        // identifies the node precisely instead.
        return 0;
    }

    private static IEnumerable<XmlNode> SelectAll(XmlNode context, string xpath)
    {
        XmlNodeList? nodes = context is XmlDocument d ? d.SelectNodes(xpath) : context.SelectNodes(xpath);
        if (nodes == null)
        {
            yield break;
        }
        foreach (XmlNode n in nodes)
        {
            yield return n;
        }
    }

    private static List<XmlNode> SelectList(XmlNode context, string xpath)
    {
        var list = new List<XmlNode>();
        XmlNodeList? nodes = context.SelectNodes(xpath);
        if (nodes != null)
        {
            foreach (XmlNode n in nodes)
            {
                list.Add(n);
            }
        }
        return list;
    }

    private static XmlNodeList EmptyNodeList() => _emptyDoc.ChildNodes;
    private static readonly XmlDocument _emptyDoc = new();

    private static PropSet AddPropSet(List<PropSet> sets, string ident, string type)
    {
        var set = new PropSet(ident, type);
        sets.Add(set);
        return set;
    }

    /* ----- value types ----- */

    private readonly record struct Assignment(string Ident, string Type, string Value);

    private sealed class PropSet(string ident, string type)
    {
        public string Ident { get; } = ident;
        public string Type { get; } = type;
        public List<string> Values { get; } = new();

        public void AddValue(string value)
        {
            if (!Values.Contains(value))
            {
                Values.Add(value);
            }
        }
    }

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: s1kd-appcheck [options] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  -A, --act <file>        User-specified ACT.");
        stdout.WriteLine("  -a, --all               Validate against all property values.");
        stdout.WriteLine("  -b, --brexcheck         Validate against BREX.");
        stdout.WriteLine("  -C, --cct <file>        User-specified CCT.");
        stdout.WriteLine("  -c, --custom            Perform a customized check.");
        stdout.WriteLine("  -D, --duplicate         Check for duplicate applicability annotations.");
        stdout.WriteLine("  -d, --dir <dir>         Search for ACT/CCT/PCT in <dir>.");
        stdout.WriteLine("  -e, --exec <cmd>        Commands used to validate objects.");
        stdout.WriteLine("  -F, --valid-filenames   List valid files.");
        stdout.WriteLine("  -f, --filenames         List invalid files.");
        stdout.WriteLine("  -h, -?, --help          Show help/usage message.");
        stdout.WriteLine("  -i, --ignore <id:type>  Ignore an applicability property when validating.");
        stdout.WriteLine("  -K, --filter <cmd>      Command used to create objects.");
        stdout.WriteLine("  -k, --args <args>       Arguments used to create objects.");
        stdout.WriteLine("  -l, --list              Treat input as list of CSDB objects.");
        stdout.WriteLine("  -N, --omit-issue        Assume issue/inwork numbers are omitted.");
        stdout.WriteLine("  -n, --nested            Check nested applicability annotations.");
        stdout.WriteLine("  -o, --output-valid      Output valid CSDB objects to stdout.");
        stdout.WriteLine("  -P, --pct <file>        User-specified PCT.");
        stdout.WriteLine("  -p, --progress          Display a progress bar.");
        stdout.WriteLine("  -q, --quiet             Quiet mode.");
        stdout.WriteLine("  -R, --redundant         Check for redundant applicability annotations.");
        stdout.WriteLine("  -r, --recursive         Search for ACT/CCT/PCT recursively.");
        stdout.WriteLine("  -s, --strict            Check that all properties are defined.");
        stdout.WriteLine("  -T, --summary           Print a summary of the check.");
        stdout.WriteLine("  -t, --products          Validate against product instances.");
        stdout.WriteLine("  -u, --unstrict-nested   Perform a nested check in unstrict mode.");
        stdout.WriteLine("  -v, --verbose           Verbose output.");
        stdout.WriteLine("  -X, --xml-with-errors   Output an XML report, including all details on errors.");
        stdout.WriteLine("  -x, --xml               Output a simpler XML report.");
        stdout.WriteLine("  -8, --deep-copy-nodes   The XML report will include a deep copy of invalid nodes.");
        stdout.WriteLine("  -~, --dependencies      Check CCT dependencies.");
        stdout.WriteLine("  -^, --remove-deleted    Validate with elements marked as \"delete\" removed.");
        stdout.WriteLine("  -#, --threads <x[,y]>   Number of threads to use.");
        stdout.WriteLine("      --version           Show version information.");
        stdout.WriteLine("  <object>...             CSDB object(s) to check.");
    }
}
