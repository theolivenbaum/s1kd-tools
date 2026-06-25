using System.Globalization;
using System.Text;
using System.Xml;

namespace S1kdTools.Tools;

/// <summary>
/// Port of <c>s1kd-uom</c>: convert units of measure in the quantity data of
/// CSDB objects.
/// <para>
/// The C tool builds an XSLT stylesheet on the fly from its <c>.uom</c>
/// configuration (a list of <c>&lt;convert&gt;</c> rules, each with an XPath
/// <c>formula</c>) and applies it with libxslt. This port performs the same
/// conversion directly against the <see cref="XmlDocument"/> DOM: it locates the
/// quantity elements, resolves their unit of measure, applies the matching
/// conversion formula, formats the result and rewrites the unit attributes.
/// </para>
/// <para>
/// Implemented: unit conversion (<c>-u/-t/-e/-F</c>), predefined and custom
/// conversion sets (<c>-s/-S</c>), duplicate quantities (<c>-d/-D</c>), custom
/// <c>.uom</c> files (<c>-U</c>), dumping the built-in configuration
/// (<c>-,</c>/<c>-.</c>), list input (<c>-l</c>), overwrite (<c>-f</c>) and the
/// matching exit codes. The display preformatting feature (<c>-p</c>/<c>-P</c>)
/// is recognised but not applied — see the remark on <see cref="_dispFmt"/>.
/// </para>
/// </summary>
public sealed class UomTool : ITool
{
    public string Name => "uom";
    public string Description => "Convert units of measure in quantity data.";
    public string Version => "1.20.0";

    /* Exit codes (mirror the EXIT_* defines). */
    private const int ExitNoConv = 1;
    private const int ExitNoUom = 2;

    private enum Verbosity { Quiet = 0, Normal = 1, Verbose = 2 }

    private const string ErrPrefix = "s1kd-uom: ERROR: ";
    private const string WrnPrefix = "s1kd-uom: WARNING: ";
    private const string InfPrefix = "s1kd-uom: INFO: ";

    private Verbosity _verbosity = Verbosity.Normal;

    // Number format applied to converted values when a rule does not specify
    // its own (mirrors the user-format param / -F at the global level).
    private string? _format;

    // Display preformat (-p) / custom uomdisplay file (-P). Recorded but not
    // applied; preformatting is a separate, large presentation subsystem.
    private string? _dispFmt;

    // Duplicate-quantity options (-d / -D).
    private bool _duplicate;
    private string? _duplFmt;

    /// <summary>Thrown internally to mirror the C tool's <c>exit()</c> calls.</summary>
    private sealed class ExitException(int code) : Exception
    {
        public int Code { get; } = code;
    }

    public int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool list = false;
        bool overwrite = false;
        bool dumpUom = false;
        bool dumpUomDisp = false;
        string uomFname = "";
        string uomDispFname = "";
        var files = new List<string>();

        // The requested set of conversions (mirrors the C "conversions" node).
        var convDoc = XmlUtils.NewDocument();
        XmlElement conversions = convDoc.CreateElement("conversions");
        convDoc.AppendChild(conversions);
        XmlElement? cur = null; // the conversion currently being built (-u …)

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
                    case "-D" or "--duplicate-format":
                        _duplFmt ??= NextArg(args, ref i, "-D", stderr);
                        _duplicate = true;
                        break;
                    case "-d" or "--duplicate":
                        _duplicate = true;
                        break;
                    case "-e" or "--formula":
                    {
                        string expr = NextArg(args, ref i, "-e", stderr);
                        if (cur == null)
                        {
                            if (_verbosity >= Verbosity.Normal)
                            {
                                stderr.WriteLine($"{ErrPrefix}-e: Unit conversions must be specified as: " +
                                                 "-u <uom> -t <uom> [-e <expr>] [-F <fmt>]");
                            }
                            return ExitNoUom;
                        }
                        cur.SetAttribute("formula", expr);
                        break;
                    }
                    case "-F" or "--format":
                    {
                        string fmt = NextArg(args, ref i, "-F", stderr);
                        if (cur != null)
                        {
                            cur.SetAttribute("format", fmt);
                        }
                        else
                        {
                            _format = fmt;
                        }
                        break;
                    }
                    case "-f" or "--overwrite":
                        overwrite = true;
                        break;
                    case "-l" or "--list":
                        list = true;
                        break;
                    case "-P" or "--uomdisplay":
                        uomDispFname = NextArg(args, ref i, "-P", stderr);
                        break;
                    case "-p" or "--preformat":
                        _dispFmt = NextArg(args, ref i, "-p", stderr);
                        break;
                    case "-q" or "--quiet":
                        _verbosity--;
                        break;
                    case "-S" or "--set":
                        LoadPresets(conversions, NextArg(args, ref i, "-S", stderr), file: true, stderr);
                        break;
                    case "-s" or "--preset":
                        LoadPresets(conversions, NextArg(args, ref i, "-s", stderr), file: false, stderr);
                        break;
                    case "-t" or "--to":
                    {
                        string to = NextArg(args, ref i, "-t", stderr);
                        if (cur == null)
                        {
                            if (_verbosity >= Verbosity.Normal)
                            {
                                stderr.WriteLine($"{ErrPrefix}-t: Unit conversions must be specified as: " +
                                                 "-u <uom> -t <uom> [-e <expr>] [-F <fmt>]");
                            }
                            return ExitNoUom;
                        }
                        cur.SetAttribute("to", to);
                        break;
                    }
                    case "-U" or "--uom":
                        uomFname = NextArg(args, ref i, "-U", stderr);
                        break;
                    case "-u" or "--from":
                    {
                        string from = NextArg(args, ref i, "-u", stderr);
                        cur = convDoc.CreateElement("convert");
                        cur.SetAttribute("from", from);
                        conversions.AppendChild(cur);
                        break;
                    }
                    case "-v" or "--verbose":
                        _verbosity++;
                        break;
                    case "-," or "--dump-uom":
                        dumpUom = true;
                        break;
                    case "-." or "--dump-uomdisplay":
                        dumpUomDisp = true;
                        break;
                    default:
                        if (a.Length > 1 && a[0] == '-' && a != "-")
                        {
                            stderr.WriteLine($"{ErrPrefix}Unknown option: {a}");
                            return ExitNoUom;
                        }
                        files.Add(a);
                        break;
                }
            }

            // Load .uom configuration file (or built-in copy).
            XmlDocument? uom = null;
            if (!dumpUom)
            {
                if (uomFname.Length != 0)
                {
                    uom = TryReadDoc(uomFname);
                }
                else if (Csdb.FindConfig(Csdb.UomFileName, out string found))
                {
                    uom = TryReadDoc(found);
                }
            }
            uom ??= EmbeddedResources.LoadXml("uom/uom.xml");

            // Load .uomdisplay configuration file (or built-in copy).
            XmlDocument? uomDisp = null;
            if (!dumpUomDisp)
            {
                if (uomDispFname.Length != 0)
                {
                    uomDisp = TryReadDoc(uomDispFname);
                }
                else if (Csdb.FindConfig(Csdb.UomDisplayFileName, out string found))
                {
                    uomDisp = TryReadDoc(found);
                }
            }
            uomDisp ??= EmbeddedResources.LoadXml("uom/uomdisplay.xml");

            // The display preformatting feature (-p) is recognised but not yet
            // applied by this port. Warn so behaviour is not silently dropped.
            if (_dispFmt != null && uomDisp.DocumentElement != null && _verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{WrnPrefix}Preformatting (-p {_dispFmt}) is not supported by this port; " +
                                 "quantities will be converted but not display-formatted.");
            }

            // Narrow the configuration down to the requested conversions.
            if (conversions.HasChildNodes && uom.DocumentElement != null)
            {
                SelectUoms(uom.DocumentElement, conversions, stderr);
            }

            if (dumpUom)
            {
                WriteDoc(uom, stdout);
            }
            else if (dumpUomDisp)
            {
                WriteDoc(uomDisp, stdout);
            }
            else if (files.Count > 0)
            {
                foreach (string f in files)
                {
                    if (list)
                    {
                        ConvertUomsList(f, uom, overwrite, stdout, stderr);
                    }
                    else
                    {
                        ConvertUoms(f, uom, overwrite, stdout, stderr);
                    }
                }
            }
            else if (list)
            {
                ConvertUomsList(null, uom, overwrite, stdout, stderr);
            }
            else
            {
                ConvertUoms(null, uom, overwrite: false, stdout, stderr);
            }

            return 0;
        }
        catch (ExitException ex)
        {
            return ex.Code;
        }
    }

    /* ---- configuration set handling -------------------------------------- */

    /// <summary>
    /// Reduce <paramref name="uom"/> to the conversions requested in
    /// <paramref name="conversions"/>, copying the per-rule <c>formula</c>/
    /// <c>format</c> overrides across. Mirrors <c>select_uoms</c>.
    /// </summary>
    private void SelectUoms(XmlElement uom, XmlElement conversions, TextWriter stderr)
    {
        // First pass: keep only the .uom rules that were requested, applying any
        // formula/format overrides from the requested conversion onto them.
        XmlNode? node = uom.FirstChild;
        while (node != null)
        {
            XmlNode? next = node.NextSibling;

            if (node is XmlElement rule)
            {
                string uomFrom = rule.GetAttribute("from");
                string uomTo = rule.GetAttribute("to");
                bool match = false;

                XmlNode? c = conversions.FirstChild;
                while (c != null)
                {
                    XmlNode? cn = c.NextSibling;
                    if (c is XmlElement conv)
                    {
                        string convertFrom = conv.GetAttribute("from");
                        string convertTo = conv.HasAttribute("to") ? conv.GetAttribute("to") : convertFrom;

                        if (convertFrom.Length != 0 && uomFrom == convertFrom && uomTo == convertTo)
                        {
                            if (conv.HasAttribute("formula"))
                            {
                                rule.SetAttribute("formula", conv.GetAttribute("formula"));
                            }
                            if (conv.HasAttribute("format"))
                            {
                                rule.SetAttribute("format", conv.GetAttribute("format"));
                            }
                            conversions.RemoveChild(conv);
                            match = true;
                            break;
                        }
                    }
                    c = cn;
                }

                if (!match)
                {
                    uom.RemoveChild(rule);
                }
            }
            else
            {
                uom.RemoveChild(node);
            }

            node = next;
        }

        // Second pass: any requested conversions that defined their own formula
        // (i.e. not found in the .uom file) get appended; the rest warn.
        XmlNode? leftover = conversions.FirstChild;
        while (leftover != null)
        {
            XmlNode? next = leftover.NextSibling;
            if (leftover is XmlElement conv)
            {
                if (conv.HasAttribute("formula"))
                {
                    uom.AppendChild(uom.OwnerDocument.ImportNode(conv, true));
                }
                else if (_verbosity >= Verbosity.Normal)
                {
                    string from = conv.GetAttribute("from");
                    if (conv.HasAttribute("to"))
                    {
                        stderr.WriteLine($"{WrnPrefix}No conversion defined for {from} -> {conv.GetAttribute("to")}.");
                    }
                    else
                    {
                        stderr.WriteLine($"{WrnPrefix}No target UOM given for {from}.");
                    }
                }
            }
            conversions.RemoveChild(leftover);
            leftover = next;
        }
    }

    /// <summary>Load a predefined or custom set of conversions. Mirrors <c>load_presets</c>.</summary>
    private void LoadPresets(XmlElement convs, string preset, bool file, TextWriter stderr)
    {
        XmlDocument? doc;
        if (file)
        {
            doc = TryReadDoc(preset);
        }
        else
        {
            string resource = preset switch
            {
                "SI" => "uom/presets/SI.xml",
                "imperial" => "uom/presets/imperial.xml",
                "US" => "uom/presets/US.xml",
                _ => "",
            };
            if (resource.Length == 0)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{WrnPrefix}No such preset: {preset}.");
                }
                return;
            }
            doc = EmbeddedResources.LoadXml(resource);
        }

        if (doc?.DocumentElement == null)
        {
            if (_verbosity >= Verbosity.Normal)
            {
                stderr.WriteLine($"{WrnPrefix}Could not read set of conversions: {preset}");
            }
            return;
        }

        foreach (XmlNode child in doc.DocumentElement.ChildNodes)
        {
            if (child is XmlElement el && el.Name == "convert")
            {
                convs.AppendChild(convs.OwnerDocument.ImportNode(el, true));
            }
        }
    }

    /* ---- conversion ------------------------------------------------------ */

    private void ConvertUomsList(string? path, XmlDocument uom, bool overwrite, TextWriter stdout, TextWriter stderr)
    {
        TextReader reader;
        if (path != null)
        {
            try
            {
                reader = new StreamReader(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (_verbosity >= Verbosity.Normal)
                {
                    stderr.WriteLine($"{WrnPrefix}Could not read list: {path}");
                }
                return;
            }
        }
        else
        {
            reader = Console.In;
        }

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.TrimEnd('\t', '\r', '\n');
                if (line.Length == 0)
                {
                    continue;
                }
                ConvertUoms(line, uom, overwrite, stdout, stderr);
            }
        }
        finally
        {
            if (path != null)
            {
                reader.Dispose();
            }
        }
    }

    /// <summary>Convert UOM for a single object. Mirrors <c>convert_uoms</c>.</summary>
    private void ConvertUoms(string? path, XmlDocument uom, bool overwrite, TextWriter stdout, TextWriter stderr)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            stderr.WriteLine($"{InfPrefix}Converting units in {path ?? "-"}...");
        }

        XmlDocument doc;
        try
        {
            if (path != null)
            {
                doc = XmlUtils.ReadDoc(path);
            }
            else
            {
                doc = XmlUtils.NewDocument();
                doc.Load(Console.OpenStandardInput());
            }
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            return;
        }

        if (doc.DocumentElement == null)
        {
            return;
        }

        ApplyConversions(doc.DocumentElement, uom, allowDuplicate: _duplicate);

        if (overwrite && path != null)
        {
            XmlUtils.SaveDoc(doc, path);
        }
        else
        {
            WriteDoc(doc, stdout);
        }
    }

    /// <summary>
    /// Walk the document and convert every quantity value/tolerance element,
    /// then rewrite the unit-of-measure attributes. Equivalent to applying the
    /// stylesheet that the C tool generates from the .uom file.
    /// </summary>
    private void ApplyConversions(XmlElement root, XmlDocument uom, bool allowDuplicate)
    {
        var rules = BuildRuleTable(uom);
        if (rules.Count == 0)
        {
            return;
        }

        if (allowDuplicate)
        {
            // Duplicate mode: leave originals intact and append a parenthesised,
            // converted copy of each <quantity> that actually changes. Mirrors
            // the dupl/undupl XSLT passes which protect the original quantity's
            // units from conversion and keep only duplicates where a conversion
            // occurred.
            var quantities = new List<XmlElement>();
            CollectQuantities(root, quantities);
            foreach (XmlElement quantity in quantities)
            {
                if (quantity.ParentNode == null)
                {
                    continue;
                }
                var clone = (XmlElement)quantity.CloneNode(true);
                bool converted = ConvertInPlace(clone, rules);
                if (!converted)
                {
                    continue;
                }

                (string prefix, string postfix) = DuplicateAffixes();
                XmlDocument owner = quantity.OwnerDocument;
                var dupWrapper = owner.CreateElement("s1kd-uom_DUPLICATE");
                dupWrapper.AppendChild(owner.CreateTextNode(prefix));
                dupWrapper.AppendChild(clone);
                dupWrapper.AppendChild(owner.CreateTextNode(postfix));
                quantity.ParentNode.InsertAfter(dupWrapper, quantity);
            }
        }
        else
        {
            ConvertInPlace(root, rules);
        }
    }

    /// <summary>
    /// Convert the value/tolerance text and rewrite UOM attributes inside
    /// <paramref name="root"/>. Returns whether any value was actually converted
    /// (i.e. a from != to rule applied).
    /// </summary>
    private bool ConvertInPlace(XmlElement root, Dictionary<string, Rule> rules)
    {
        var valueNodes = new List<XmlElement>();
        var attrOwners = new List<XmlElement>();
        CollectTargets(root, valueNodes, attrOwners);

        bool anyConverted = false;

        foreach (XmlElement el in valueNodes)
        {
            string? uomName = ResolveUom(el);
            if (uomName == null || !rules.TryGetValue(uomName, out Rule rule) || rule.Formula == null)
            {
                continue;
            }

            if (!TryParseDouble(el.InnerText.Trim(), out double value))
            {
                continue;
            }

            double result = FormulaEvaluator.Evaluate(rule.Formula, value);
            el.InnerText = XsltFormatNumber.Format(result, rule.Format ?? _format ?? "0.##");

            if (rule.From != rule.To)
            {
                anyConverted = true;
            }
        }

        // Rewrite the unit attributes per the from->to mapping.
        foreach (XmlElement el in attrOwners)
        {
            RewriteUomAttribute(el, "quantityUnitOfMeasure", rules);
            RewriteUomAttribute(el, "qtyuom", rules);
            RewriteUomAttribute(el, "quantityTypeSpecifics", rules);
        }

        return anyConverted;
    }

    private static void CollectQuantities(XmlNode node, List<XmlElement> quantities)
    {
        if (node is XmlElement el && el.Name == "quantity")
        {
            quantities.Add(el);
            // Do not descend into nested quantities (rare); avoid duplicating twice.
            return;
        }
        foreach (XmlNode child in node.ChildNodes)
        {
            CollectQuantities(child, quantities);
        }
    }

    /// <summary>Build a from -> conversion table for value conversion.</summary>
    private static Dictionary<string, Rule> BuildRuleTable(XmlDocument uom)
    {
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        if (uom.DocumentElement == null)
        {
            return rules;
        }
        foreach (XmlNode node in uom.DocumentElement.ChildNodes)
        {
            if (node is XmlElement el && el.Name == "convert")
            {
                string from = el.GetAttribute("from");
                if (from.Length == 0 || rules.ContainsKey(from))
                {
                    continue;
                }
                string to = el.HasAttribute("to") ? el.GetAttribute("to") : from;
                string? formula = el.HasAttribute("formula") ? el.GetAttribute("formula") : null;
                string? format = el.HasAttribute("format") ? el.GetAttribute("format") : null;
                rules[from] = new Rule(from, to, formula, format);
            }
        }
        return rules;
    }

    private static void RewriteUomAttribute(XmlElement el, string attr, Dictionary<string, Rule> rules)
    {
        if (el.HasAttribute(attr))
        {
            string v = el.GetAttribute(attr);
            if (rules.TryGetValue(v, out Rule rule))
            {
                el.SetAttribute(attr, rule.To);
            }
        }
    }

    /// <summary>
    /// Gather the value/tolerance elements to convert and the elements whose
    /// UOM attributes need rewriting. Matches
    /// <c>quantityValue|qtyvalue|quantityTolerance|qtytolerance|quantity[not(*)]</c>.
    /// </summary>
    private static void CollectTargets(XmlNode node, List<XmlElement> values, List<XmlElement> attrOwners)
    {
        if (node is XmlElement el)
        {
            switch (el.Name)
            {
                case "quantityValue":
                case "qtyvalue":
                case "quantityTolerance":
                case "qtytolerance":
                    values.Add(el);
                    break;
                case "quantity":
                    if (!HasElementChild(el))
                    {
                        values.Add(el);
                    }
                    break;
            }

            if (el.HasAttribute("quantityUnitOfMeasure") ||
                el.HasAttribute("qtyuom") ||
                el.HasAttribute("quantityTypeSpecifics"))
            {
                attrOwners.Add(el);
            }
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            CollectTargets(child, values, attrOwners);
        }
    }

    private static bool HasElementChild(XmlElement el)
    {
        foreach (XmlNode child in el.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Resolve the unit of measure that applies to a value element. Mirrors the
    /// XSLT: own <c>@quantityUnitOfMeasure|@qtyuom</c>, else the parent group's,
    /// else the enclosing quantity's <c>@quantityTypeSpecifics</c>.
    /// </summary>
    private static string? ResolveUom(XmlElement el)
    {
        if (el.HasAttribute("quantityUnitOfMeasure"))
        {
            return el.GetAttribute("quantityUnitOfMeasure");
        }
        if (el.HasAttribute("qtyuom"))
        {
            return el.GetAttribute("qtyuom");
        }

        if (el.ParentNode is XmlElement parent)
        {
            if (parent.Name == "quantityGroup" && parent.HasAttribute("quantityUnitOfMeasure"))
            {
                return parent.GetAttribute("quantityUnitOfMeasure");
            }
            if (parent.Name == "qtygrp" && parent.HasAttribute("qtyuom"))
            {
                return parent.GetAttribute("qtyuom");
            }
        }

        // ancestor-or-self::quantity/@quantityTypeSpecifics
        for (XmlNode? a = el; a != null; a = a.ParentNode)
        {
            if (a is XmlElement ae && ae.Name == "quantity" && ae.HasAttribute("quantityTypeSpecifics"))
            {
                return ae.GetAttribute("quantityTypeSpecifics");
            }
        }

        return null;
    }

    /// <summary>Compute the prefix/postfix around a duplicate quantity.</summary>
    private (string prefix, string postfix) DuplicateAffixes()
    {
        if (_duplFmt == null)
        {
            return (" (", ")");
        }

        // The custom format is "<prefix>%s<postfix>" where %s marks where the
        // converted quantity goes. Backslash escapes \n and \t.
        var prefix = new StringBuilder();
        var postfix = new StringBuilder();
        int i = 0;
        for (; i < _duplFmt.Length; i++)
        {
            char ch = _duplFmt[i];
            if (ch == '\\' && i + 1 < _duplFmt.Length)
            {
                prefix.Append(Unescape(_duplFmt[++i]));
            }
            else if (ch == '%')
            {
                // skip the conversion specifier (e.g. %s)
                if (i + 1 < _duplFmt.Length)
                {
                    i++;
                }
                i++;
                break;
            }
            else
            {
                prefix.Append(ch);
            }
        }
        for (; i < _duplFmt.Length; i++)
        {
            char ch = _duplFmt[i];
            if (ch == '\\' && i + 1 < _duplFmt.Length)
            {
                postfix.Append(Unescape(_duplFmt[++i]));
            }
            else
            {
                postfix.Append(ch);
            }
        }
        return (prefix.ToString(), postfix.ToString());
    }

    private static char Unescape(char c) => c switch
    {
        'n' => '\n',
        't' => '\t',
        _ => c,
    };

    /* ---- helpers --------------------------------------------------------- */

    private readonly record struct Rule(string From, string To, string? Formula, string? Format);

    private static bool TryParseDouble(string s, out double d) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    private static XmlDocument? TryReadDoc(string path)
    {
        try
        {
            return XmlUtils.ReadDoc(path);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteDoc(XmlDocument doc, TextWriter stdout)
    {
        stdout.Write(XmlUtils.ToXmlString(doc));
        stdout.Write('\n');
    }

    private string NextArg(IReadOnlyList<string> args, ref int i, string opt, TextWriter stderr)
    {
        if (++i >= args.Count)
        {
            stderr.WriteLine($"{ErrPrefix}{opt} requires an argument");
            throw new ExitException(ExitNoUom);
        }
        return args[i];
    }

    private static void ShowVersion(TextWriter stdout)
    {
        stdout.WriteLine("s1kd-uom (s1kd-tools) 1.20.0");
    }

    private static void ShowHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: s1kd-uom [-dflqv,.h?] [-D <fmt>] [-F <fmt>] [-u <uom> -t <uom> [-e <expr>] [-F <fmt>] ...] [-p <fmt> [-P <path>]] [-s <name>|-S <path> ...] [-U <path>] [<object>...]");
        stdout.WriteLine();
        stdout.WriteLine("  -D, --duplicate-format <fmt>  Custom format for duplicate quantities (-d).");
        stdout.WriteLine("  -d, --duplicate               Include conversions as duplicate quantities in parenthesis.");
        stdout.WriteLine("  -e, --formula <expr>          Specify formula for a conversion.");
        stdout.WriteLine("  -F, --format <fmt>            Number format for converted values.");
        stdout.WriteLine("  -f, --overwrite               Overwrite input objects.");
        stdout.WriteLine("  -h, -?, --help                Show help/usage message.");
        stdout.WriteLine("  -l, --list                    Treat input as list of CSDB objects.");
        stdout.WriteLine("  -P, --uomdisplay <path>       Use custom UOM display file.");
        stdout.WriteLine("  -p, --preformat <fmt>         Preformat quantity data.");
        stdout.WriteLine("  -q, --quiet                   Quiet mode.");
        stdout.WriteLine("  -S, --set <path>              Apply a custom set of conversions.");
        stdout.WriteLine("  -s, --preset <name>           Apply a predefined set of conversions.");
        stdout.WriteLine("  -t, --to <uom>                UOM to convert to.");
        stdout.WriteLine("  -U, --uom <path>              Use custom .uom file.");
        stdout.WriteLine("  -u, --from <uom>              UOM to convert from.");
        stdout.WriteLine("  -v, --verbose                 Verbose output.");
        stdout.WriteLine("  -,, --dump-uom                Dump default .uom file.");
        stdout.WriteLine("  -., --dump-uomdisplay         Dump default UOM preformatting file.");
        stdout.WriteLine("  --version                     Show version information.");
        stdout.WriteLine("  <object>                      CSDB object to convert quantities in.");
    }
}

/// <summary>
/// Evaluates the small XPath-1.0 arithmetic expressions used in <c>.uom</c>
/// conversion formulas (e.g. <c>$value * (9 div 5) + 32</c>). Supports the
/// variables <c>$value</c> and <c>$pi</c>, numbers (including scientific
/// notation), the operators <c>+ - * div</c> and parentheses. This replaces the
/// libxslt <c>format-number(&lt;formula&gt;, …)</c> evaluation done in the C
/// tool's generated stylesheet.
/// </summary>
public static class FormulaEvaluator
{
    private const double Pi = 3.14159265359;

    public static double Evaluate(string expr, double value)
    {
        var p = new Parser(expr, value);
        double r = p.ParseExpression();
        p.ExpectEnd();
        return r;
    }

    private sealed class Parser(string text, double value)
    {
        private readonly string _t = text;
        private readonly double _value = value;
        private int _pos;

        public double ParseExpression() => ParseAdditive();

        // additive: multiplicative (('+'|'-') multiplicative)*
        private double ParseAdditive()
        {
            double left = ParseMultiplicative();
            while (true)
            {
                SkipWs();
                if (Peek('+'))
                {
                    _pos++;
                    left += ParseMultiplicative();
                }
                else if (Peek('-'))
                {
                    _pos++;
                    left -= ParseMultiplicative();
                }
                else
                {
                    return left;
                }
            }
        }

        // multiplicative: unary (('*'|'div') unary)*
        private double ParseMultiplicative()
        {
            double left = ParseUnary();
            while (true)
            {
                SkipWs();
                if (Peek('*'))
                {
                    _pos++;
                    left *= ParseUnary();
                }
                else if (MatchWord("div"))
                {
                    left /= ParseUnary();
                }
                else if (MatchWord("mod"))
                {
                    left %= ParseUnary();
                }
                else
                {
                    return left;
                }
            }
        }

        private double ParseUnary()
        {
            SkipWs();
            if (Peek('-'))
            {
                _pos++;
                return -ParseUnary();
            }
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWs();
            if (Peek('('))
            {
                _pos++;
                double r = ParseAdditive();
                SkipWs();
                if (Peek(')'))
                {
                    _pos++;
                }
                return r;
            }
            if (Peek('$'))
            {
                _pos++;
                string name = ReadName();
                return name switch
                {
                    "value" => _value,
                    "pi" => Pi,
                    _ => 0.0,
                };
            }
            return ReadNumber();
        }

        private double ReadNumber()
        {
            SkipWs();
            int start = _pos;
            while (_pos < _t.Length)
            {
                char c = _t[_pos];
                if (char.IsDigit(c) || c == '.')
                {
                    _pos++;
                }
                else if ((c == 'e' || c == 'E') && _pos > start)
                {
                    _pos++;
                    if (_pos < _t.Length && (_t[_pos] == '+' || _t[_pos] == '-'))
                    {
                        _pos++;
                    }
                }
                else
                {
                    break;
                }
            }
            string s = _t[start.._pos];
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? d
                : double.NaN;
        }

        private string ReadName()
        {
            int start = _pos;
            while (_pos < _t.Length && (char.IsLetterOrDigit(_t[_pos]) || _t[_pos] == '_' || _t[_pos] == '-'))
            {
                _pos++;
            }
            return _t[start.._pos];
        }

        private bool MatchWord(string word)
        {
            SkipWs();
            if (_pos + word.Length > _t.Length)
            {
                return false;
            }
            if (string.CompareOrdinal(_t, _pos, word, 0, word.Length) != 0)
            {
                return false;
            }
            // Ensure it's a whole word.
            int after = _pos + word.Length;
            if (after < _t.Length && (char.IsLetterOrDigit(_t[after]) || _t[after] == '_'))
            {
                return false;
            }
            _pos = after;
            return true;
        }

        private bool Peek(char c) => _pos < _t.Length && _t[_pos] == c;

        private void SkipWs()
        {
            while (_pos < _t.Length && char.IsWhiteSpace(_t[_pos]))
            {
                _pos++;
            }
        }

        public void ExpectEnd() => SkipWs();
    }
}

/// <summary>
/// Implements the subset of XSLT 1.0 <c>format-number</c> picture strings used
/// by s1kd-uom (e.g. <c>0.##</c>). The pattern uses <c>0</c> for a required
/// digit and <c>#</c> for an optional digit; the number is rounded to the count
/// of fractional digits in the pattern. The behaviour matches .NET's custom
/// numeric format for these patterns, which is identical to XSLT for this
/// subset.
/// </summary>
public static class XsltFormatNumber
{
    public static string Format(double value, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            pattern = "0.##";
        }
        try
        {
            return value.ToString(pattern, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
