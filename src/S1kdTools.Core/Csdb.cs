using System.Globalization;

namespace S1kdTools;

/// <summary>
/// Helpers for working with CSDB (Common Source Database) objects and the
/// S1000D file-naming conventions. Ported from <c>tools/common/s1kd_tools.c</c>.
/// </summary>
public static class Csdb
{
    /// <summary>Default configuration file names (mirrors the DEFAULT_*_FNAME defines).</summary>
    public const string DefaultsFileName = ".defaults";
    public const string DmTypesFileName = ".dmtypes";
    public const string FmTypesFileName = ".fmtypes";
    public const string IcnCatalogFileName = ".icncatalog";
    public const string AcronymsFileName = ".acronyms";
    public const string IndexFlagsFileName = ".indexflags";
    public const string BrexMapFileName = ".brexmap";
    public const string BrSeverityLevelsFileName = ".brseveritylevels";
    public const string UomFileName = ".uom";
    public const string UomDisplayFileName = ".uomdisplay";
    public const string ExternalPubsFileName = ".externalpubs";
    public const string DispTextFileName = ".disptext";

    private static bool IsXml(string name) =>
        name.Length >= 4 && name.AsSpan(name.Length - 4).Equals(".XML", StringComparison.OrdinalIgnoreCase);

    private static bool Prefixed(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Determine if the file is a data module (DMC-…).</summary>
    public static bool IsDataModule(string name) => Prefixed(name, "DMC-") && IsXml(name);

    /// <summary>Determine if the file is a publication module (PMC-…).</summary>
    public static bool IsPublicationModule(string name) => Prefixed(name, "PMC-") && IsXml(name);

    /// <summary>Determine if the file is a comment (COM-…).</summary>
    public static bool IsComment(string name) => Prefixed(name, "COM-") && IsXml(name);

    /// <summary>Determine if the file is an ICN metadata file (IMF-…).</summary>
    public static bool IsIcnMetadataFile(string name) => Prefixed(name, "IMF-") && IsXml(name);

    /// <summary>Determine if the file is a data dispatch note (DDN-…).</summary>
    public static bool IsDataDispatchNote(string name) => Prefixed(name, "DDN-") && IsXml(name);

    /// <summary>Determine if the file is a data management list (DML-…).</summary>
    public static bool IsDataManagementList(string name) => Prefixed(name, "DML-") && IsXml(name);

    /// <summary>Determine if the file is an ICN (ICN-…).</summary>
    public static bool IsIcn(string name) => Prefixed(name, "ICN-");

    /// <summary>Determine if the file is a SCORM content package (SMC-/SME-…).</summary>
    public static bool IsScormContentPackage(string name) =>
        (Prefixed(name, "SMC-") || Prefixed(name, "SME-")) && IsXml(name);

    /// <summary>Determine if the file is a data update file (UPF-/UPE-…).</summary>
    public static bool IsDataUpdateFile(string name) =>
        (Prefixed(name, "UPF-") || Prefixed(name, "UPE-")) && IsXml(name);

    /// <summary>
    /// Case-insensitive match of <paramref name="value"/> against a pattern
    /// using <c>?</c> as a single-character wildcard. The pattern need only be a
    /// prefix of the value (mirrors <c>strmatch</c>).
    /// </summary>
    public static bool StrMatch(string pattern, string value)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '?')
            {
                continue;
            }
            if (i >= value.Length ||
                char.ToLowerInvariant(pattern[i]) != char.ToLowerInvariant(value[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryParseDouble(string s, out double d) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    /// <summary>
    /// Test whether a value falls within an S1000D range (<c>a~c</c> is
    /// equivalent to <c>a|b|c</c>). Numeric comparison is used when all parts
    /// parse as numbers, otherwise lexicographic. Mirrors <c>is_in_range</c>.
    /// </summary>
    public static bool IsInRange(string value, string range)
    {
        int tilde = range.IndexOf('~');
        if (tilde < 0)
        {
            return string.Equals(value, range, StringComparison.Ordinal);
        }

        string first = range[..tilde];
        string last = range[(tilde + 1)..];

        if (TryParseDouble(first, out double f) &&
            TryParseDouble(last, out double l) &&
            TryParseDouble(value, out double v))
        {
            return v - f >= 0 && v - l <= 0;
        }

        return string.CompareOrdinal(value, first) >= 0 &&
               string.CompareOrdinal(value, last) <= 0;
    }

    /// <summary>
    /// Test whether a value is in an S1000D set (<c>a|b|c</c>, where each member
    /// may itself be a range). Mirrors <c>is_in_set</c>.
    /// </summary>
    public static bool IsInSet(string value, string set)
    {
        if (set.IndexOf('|') < 0)
        {
            return IsInRange(value, set);
        }

        foreach (string part in set.Split('|'))
        {
            if (IsInRange(value, part))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Search up the directory tree from <paramref name="startDir"/> for a
    /// configuration file, returning its full path if found. Mirrors
    /// <c>find_config</c>.
    /// </summary>
    public static bool FindConfig(string name, out string path, string? startDir = null)
    {
        string dir = Path.GetFullPath(startDir ?? Directory.GetCurrentDirectory());
        while (true)
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }

            string? parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir)
            {
                path = name;
                return false;
            }
            dir = parent;
        }
    }

    /// <summary>
    /// Given a list of CSDB object file paths sorted by base name, return only
    /// the latest issue of each (the C tool relies on the input being sorted so
    /// that later issues of the same code follow earlier ones). Mirrors
    /// <c>extract_latest_csdb_objects</c>.
    /// </summary>
    public static List<string> ExtractLatestObjects(IReadOnlyList<string> files)
    {
        var latest = new List<string>();
        for (int i = 0; i < files.Count; i++)
        {
            string base1 = Path.GetFileName(files[i]);
            if (i == 0)
            {
                latest.Add(files[i]);
                continue;
            }

            string base2 = Path.GetFileName(files[i - 1]);
            int us = base1.IndexOf('_');
            string code1 = us < 0 ? base1 : base1[..us];
            string code2 = us < 0 || code1.Length > base2.Length ? base2 : base2[..us];

            if (!string.Equals(code1, code2, StringComparison.Ordinal))
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

    /// <summary>Compare two paths by their base names, case-insensitively.</summary>
    public static int CompareBaseName(string a, string b) =>
        string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);
}
