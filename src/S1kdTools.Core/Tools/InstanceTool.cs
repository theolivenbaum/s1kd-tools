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
/// Partial / not ported (clearly noted): CIR resolution (-R/-x/-D/-d/-r),
/// PCT/ACT/CCT product filtering (-P/-p/-1/-2/-~), container resolution (-Q),
/// alts flattening (-F/-4), automatic naming/output-dir (-O/-5/-N),
/// update-instances (-@), source/repository ident control (-S/-3/-8),
/// set-applic (-W/-Y/-y), list-properties (-H), comments (-C/-X),
/// acronym fixing (-M), entity cleanup (-j), add-required (-Z), read-only (-%),
/// list input (-L), set originator (-g/-G), skill metadata (-k).
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
                case "-v" or "--verbose": break; // accepted, no extra output

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
        _ = napplics;

        FilterMode mode = prune ? FilterMode.Prune
            : simplify ? FilterMode.Simplify
            : reduce ? FilterMode.Reduce
            : FilterMode.Default;

        if (files.Count == 0)
        {
            files.Add("-");
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
            XmlDocument doc;
            try
            {
                doc = file == "-"
                    ? XmlUtils.ReadStream(Console.OpenStandardInput())
                    : XmlUtils.ReadDoc(file);
            }
            catch (FileNotFoundException)
            {
                if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not read source object: {file}");
                status = ExitMissingFile;
                continue;
            }
            catch (Exception ex) when (ex is IOException or XmlException)
            {
                if (!quiet) stderr.WriteLine($"{Name}: ERROR: {file} does not contain valid XML.");
                status = ExitBadXml;
                continue;
            }

            ApplyFilter(doc, defs, napplics, mode, reduce || simplify, prune, simplify,
                tagNonApplic, cleanDispText, remDupl, remUnused, delete, secClasses, skillCodes);

            // Metadata setters (subset; order mirrors the C tool).
            if (!string.IsNullOrEmpty(extension)) SetExtension(doc, extension);
            if (noExtension) StripExtension(doc);
            if (!string.IsNullOrEmpty(code)) SetCode(doc, code, quiet, stderr);
            SetTitle(doc, tech, info, infoNameVariant, noInfoName);
            if (!string.IsNullOrEmpty(language)) SetLang(doc, language);
            if (!string.IsNullOrEmpty(issinfo))
            {
                if (!SetIssue(doc, issinfo, quiet, stderr)) { status = ExitMissingArgs; }
            }
            if (issYear != null) SetIssueDate(doc, issYear, issMonth!, issDay!);
            if (!string.IsNullOrEmpty(isstype)) SetIssueType(doc, isstype);
            if (!string.IsNullOrEmpty(security)) SetSecurity(doc, security);
            if (remarks != null) SetRemarks(doc, remarks);

            // Output.
            string target = outFile ?? (overwrite && file != "-" ? file : "-");
            if (target == "-")
            {
                stdout.Write(XmlUtils.ToXmlString(doc));
                stdout.Write('\n');
            }
            else
            {
                if (File.Exists(target) && !overwrite && outFile == null)
                {
                    if (!quiet) stderr.WriteLine($"{Name}: WARNING: {target} already exists. Use -f to overwrite.");
                }
                else
                {
                    try
                    {
                        XmlUtils.SaveDoc(doc, target);
                    }
                    catch (IOException)
                    {
                        if (!quiet) stderr.WriteLine($"{Name}: ERROR: Could not write {target}");
                        status = ExitMissingFile;
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
        stdout.WriteLine("  -o, --out <file>                      Output to file instead of stdout.");
        stdout.WriteLine("  -f, --overwrite                       Overwrite output files.");
        stdout.WriteLine("  -q, --quiet                           Quiet mode.");
        stdout.WriteLine("  -v, --verbose                         Verbose output.");
        stdout.WriteLine("  -h, -?, --help                        Show help.");
        stdout.WriteLine("      --version                         Show version.");
        stdout.WriteLine("  <object>...                           Source CSDB object(s).");
    }
}
