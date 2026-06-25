using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace S1kdTools.Xslt;

// -----------------------------------------------------------------------------
// EXSLT shim for XslCompiledTransform.
//
// libxslt (the C dependency the s1kd-tools were originally built on) supports
// the common EXSLT modules natively. .NET's XslCompiledTransform is an XSLT 1.0
// engine that does NOT provide EXSLT functions, but it lets stylesheets call
// public methods on objects registered via
// XsltArgumentList.AddExtensionObject(namespaceUri, obj): each public instance
// method becomes a callable function in that namespace.
//
// IMPORTANT — method naming. The .NET extension-function binder resolves an
// XPath function call against the CLR method whose name matches the function's
// local name. To make binding robust we name each method EXACTLY as the EXSLT
// local name whenever that name is a legal C# identifier (e.g. "replace",
// "tokenize", "max", "distinct"). This is why the methods below are lowercase:
// they are deliberately spelled to match the XSLT function names verbatim.
//
// Hyphenated EXSLT names ("date-time", "node-set", "object-type",
// "month-in-year", …) are NOT legal C# identifiers and cannot be bound this way.
// For those:
//   * exsl:node-set() and exsl:object-type() are supported NATIVELY by
//     XslCompiledTransform, so no shim is needed (and we must not shadow them).
//     ExsltCommon below exposes identifier-friendly aliases (nodeSet/objectType)
//     for programmatic callers, but stylesheets should keep using the native
//     exsl: functions.
//   * date:date-time() is exposed here under the identifier-friendly name
//     "datetime"; stylesheets that need the hyphenated form can wrap it.
//
// Node-sets. EXSLT functions returning node-sets (str:split, str:tokenize,
// set:difference, …) must return XPathNodeIterator/XPathNavigator because that
// is how XslCompiledTransform marshals node-sets back into the stylesheet. The
// string-list helpers build a throw-away document whose <token> children carry
// the values, mirroring the element-per-item shape libxslt produces.
// -----------------------------------------------------------------------------

/// <summary>
/// Builds and runs <see cref="XslCompiledTransform"/> transforms with the EXSLT
/// shim registered. See the file header for the naming/marshalling rules.
/// </summary>
public static class Exslt
{
    /// <summary>EXSLT "strings" module namespace.</summary>
    public const string StringsNamespace = "http://exslt.org/strings";

    /// <summary>EXSLT "math" module namespace.</summary>
    public const string MathNamespace = "http://exslt.org/math";

    /// <summary>EXSLT "dates-and-times" module namespace.</summary>
    public const string DatesNamespace = "http://exslt.org/dates-and-times";

    /// <summary>EXSLT "sets" module namespace.</summary>
    public const string SetsNamespace = "http://exslt.org/sets";

    /// <summary>EXSLT "common" module namespace.</summary>
    public const string CommonNamespace = "http://exslt.org/common";

    /// <summary>
    /// Creates a fresh <see cref="XsltArgumentList"/> with every supported EXSLT
    /// module registered.
    /// </summary>
    public static XsltArgumentList CreateArgumentList()
    {
        var args = new XsltArgumentList();
        Register(args);
        return args;
    }

    /// <summary>
    /// Registers all supported EXSLT extension objects on <paramref name="args"/>.
    /// Each module is added under its EXSLT namespace URI, matching the prefixes a
    /// stylesheet binds (<c>str:</c>, <c>math:</c>, <c>date:</c>, <c>set:</c>,
    /// <c>exsl:</c>).
    /// </summary>
    public static void Register(XsltArgumentList args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.AddExtensionObject(StringsNamespace, new ExsltStrings());
        args.AddExtensionObject(MathNamespace, new ExsltMath());
        args.AddExtensionObject(DatesNamespace, new ExsltDates());
        args.AddExtensionObject(SetsNamespace, new ExsltSets());
        args.AddExtensionObject(CommonNamespace, new ExsltCommon());
    }

    /// <summary>
    /// Compiles a stylesheet from a stream with script support enabled and
    /// document()/filesystem resolution disabled.
    /// </summary>
    public static XslCompiledTransform Load(Stream stylesheet)
    {
        ArgumentNullException.ThrowIfNull(stylesheet);
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };
        using XmlReader reader = XmlReader.Create(stylesheet, readerSettings);
        return Load(reader);
    }

    /// <summary>
    /// Compiles a stylesheet from a reader. <see cref="XsltSettings.EnableScript"/>
    /// is on so stylesheets carrying inline <c>msxsl:script</c> still load;
    /// document() stays off and a null resolver blocks filesystem access.
    /// </summary>
    public static XslCompiledTransform Load(XmlReader stylesheet)
    {
        ArgumentNullException.ThrowIfNull(stylesheet);
        var xslt = new XslCompiledTransform();
        xslt.Load(stylesheet, new XsltSettings(enableDocumentFunction: false, enableScript: true), stylesheetResolver: null);
        return xslt;
    }

    /// <summary>
    /// Runs <paramref name="xslt"/> over <paramref name="input"/> with all EXSLT
    /// modules registered, plus any stylesheet parameters in
    /// <paramref name="params_"/> (added in the null namespace, as the C tools do),
    /// and returns the result as an <see cref="XmlDocument"/>.
    /// </summary>
    public static XmlDocument Transform(
        XslCompiledTransform xslt,
        IXPathNavigable input,
        params (string name, object value)[] params_)
    {
        ArgumentNullException.ThrowIfNull(xslt);
        ArgumentNullException.ThrowIfNull(input);

        XsltArgumentList args = CreateArgumentList();
        if (params_ is not null)
        {
            foreach ((string name, object value) in params_)
            {
                args.AddParam(name, string.Empty, value);
            }
        }

        var output = new XmlDocument { PreserveWhitespace = true };
        using (var ms = new MemoryStream())
        {
            var writerSettings = new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Auto,
                OmitXmlDeclaration = true,
            };
            using (XmlWriter writer = XmlWriter.Create(ms, writerSettings))
            {
                xslt.Transform(input, args, writer);
            }

            ms.Position = 0;
            var readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            };
            using XmlReader resultReader = XmlReader.Create(ms, readerSettings);
            output.Load(resultReader);
        }

        return output;
    }

    /// <summary>
    /// Builds a node-set (an <see cref="XPathNodeIterator"/> over the
    /// <c>&lt;token&gt;</c> children of a fresh document) from string values.
    /// This is the shape EXSLT string/set functions use to hand node-sets back to
    /// <see cref="XslCompiledTransform"/>.
    /// </summary>
    internal static XPathNodeIterator BuildNodeSet(IEnumerable<string> values)
    {
        var doc = new XmlDocument();
        XmlElement root = doc.CreateElement("tokens");
        doc.AppendChild(root);
        foreach (string value in values)
        {
            XmlElement token = doc.CreateElement("token");
            token.AppendChild(doc.CreateTextNode(value ?? string.Empty));
            root.AppendChild(token);
        }

        return doc.CreateNavigator()!.Select("/tokens/token");
    }
}

/// <summary>
/// EXSLT <c>str:</c> module — <see href="http://exslt.org/strings"/>.
/// Functions: replace, split, tokenize, padding, concat, align.
/// </summary>
public sealed class ExsltStrings
{
    /// <summary>
    /// <c>str:replace</c> — replaces every occurrence of <paramref name="search"/>
    /// in <paramref name="value"/> with <paramref name="replacement"/>. (The full
    /// EXSLT node-set form is not supported; the common single-string form is.)
    /// </summary>
    public string replace(string value, string search, string replacement)
    {
        value ??= string.Empty;
        search ??= string.Empty;
        replacement ??= string.Empty;
        return search.Length == 0 ? value : value.Replace(search, replacement, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>str:split</c> — splits <paramref name="value"/> on the literal
    /// <paramref name="separator"/> and returns the pieces as a node-set. An empty
    /// separator splits into individual characters.
    /// </summary>
    public XPathNodeIterator split(string value, string separator) =>
        Exslt.BuildNodeSet(SplitCore(value ?? string.Empty, separator ?? " "));

    /// <summary><c>str:split</c> with the default separator (a single space).</summary>
    public XPathNodeIterator split(string value) => split(value, " ");

    private static IEnumerable<string> SplitCore(string value, string separator)
    {
        if (value.Length == 0)
        {
            yield break;
        }

        if (separator.Length == 0)
        {
            foreach (char c in value)
            {
                yield return c.ToString();
            }
            yield break;
        }

        foreach (string piece in value.Split(separator, StringSplitOptions.None))
        {
            yield return piece;
        }
    }

    /// <summary>
    /// <c>str:tokenize</c> — splits <paramref name="value"/> into tokens, treating
    /// each character in <paramref name="delimiters"/> as a delimiter, returning
    /// the non-empty tokens as a node-set. An empty delimiter string tokenizes
    /// into characters (EXSLT semantics).
    /// </summary>
    public XPathNodeIterator tokenize(string value, string delimiters) =>
        Exslt.BuildNodeSet(TokenizeCore(value ?? string.Empty, delimiters ?? " \t\n\r"));

    /// <summary><c>str:tokenize</c> with the default delimiter set (whitespace).</summary>
    public XPathNodeIterator tokenize(string value) => tokenize(value, " \t\n\r");

    private static IEnumerable<string> TokenizeCore(string value, string delimiters)
    {
        if (value.Length == 0)
        {
            yield break;
        }

        if (delimiters.Length == 0)
        {
            foreach (char c in value)
            {
                yield return c.ToString();
            }
            yield break;
        }

        foreach (string token in value.Split(delimiters.ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
        {
            yield return token;
        }
    }

    /// <summary>
    /// <c>str:padding</c> — a string of <paramref name="length"/> characters built
    /// by repeating/truncating <paramref name="pad"/> (default a space).
    /// </summary>
    public string padding(double length, string pad)
    {
        int count = (int)length;
        if (count <= 0)
        {
            return string.Empty;
        }

        pad = string.IsNullOrEmpty(pad) ? " " : pad;
        var sb = new StringBuilder(count);
        while (sb.Length < count)
        {
            sb.Append(pad);
        }

        return sb.ToString(0, count);
    }

    /// <summary><c>str:padding</c> with the default pad character (a space).</summary>
    public string padding(double length) => padding(length, " ");

    /// <summary>
    /// <c>str:concat</c> — concatenates the string-values of every node in
    /// <paramref name="nodeSet"/> in document order.
    /// </summary>
    public string concat(XPathNodeIterator nodeSet)
    {
        if (nodeSet is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        while (nodeSet.MoveNext())
        {
            sb.Append(nodeSet.Current?.Value ?? string.Empty);
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>str:align</c> — aligns <paramref name="value"/> in a field whose width is
    /// the length of <paramref name="paddingString"/>. <paramref name="alignment"/>
    /// is "left" (default), "right" or "center"; longer values are truncated.
    /// </summary>
    public string align(string value, string paddingString, string alignment)
    {
        value ??= string.Empty;
        paddingString ??= string.Empty;
        int width = paddingString.Length;

        if (value.Length >= width)
        {
            return value[..width];
        }

        int slack = width - value.Length;
        return (alignment ?? "left") switch
        {
            "right" => string.Concat(paddingString.AsSpan(0, slack), value),
            "center" => string.Concat(
                paddingString.AsSpan(0, slack / 2),
                value,
                paddingString.AsSpan(slack / 2, slack - (slack / 2))),
            _ => string.Concat(value, paddingString.AsSpan(value.Length, slack)),
        };
    }

    /// <summary><c>str:align</c> with left alignment.</summary>
    public string align(string value, string paddingString) => align(value, paddingString, "left");
}

/// <summary>
/// EXSLT <c>math:</c> module — <see href="http://exslt.org/math"/>.
/// Functions: max, min, abs, power, sqrt, constant.
/// </summary>
public sealed class ExsltMath
{
    /// <summary>
    /// <c>math:max</c> — the largest numeric string-value in
    /// <paramref name="nodeSet"/>; NaN if empty or containing a non-number.
    /// </summary>
    public double max(XPathNodeIterator nodeSet)
    {
        bool any = false;
        double result = double.NegativeInfinity;
        if (nodeSet is not null)
        {
            while (nodeSet.MoveNext())
            {
                if (!TryNumber(nodeSet.Current?.Value, out double n))
                {
                    return double.NaN;
                }

                any = true;
                if (n > result)
                {
                    result = n;
                }
            }
        }

        return any ? result : double.NaN;
    }

    /// <summary>
    /// <c>math:min</c> — the smallest numeric string-value in
    /// <paramref name="nodeSet"/>; NaN if empty or non-numeric.
    /// </summary>
    public double min(XPathNodeIterator nodeSet)
    {
        bool any = false;
        double result = double.PositiveInfinity;
        if (nodeSet is not null)
        {
            while (nodeSet.MoveNext())
            {
                if (!TryNumber(nodeSet.Current?.Value, out double n))
                {
                    return double.NaN;
                }

                any = true;
                if (n < result)
                {
                    result = n;
                }
            }
        }

        return any ? result : double.NaN;
    }

    /// <summary><c>math:abs</c> — absolute value.</summary>
    public double abs(double value) => Math.Abs(value);

    /// <summary><c>math:power</c> — <paramref name="baseValue"/> raised to <paramref name="power_"/>.</summary>
    public double power(double baseValue, double power_) => Math.Pow(baseValue, power_);

    /// <summary><c>math:sqrt</c> — square root.</summary>
    public double sqrt(double value) => Math.Sqrt(value);

    /// <summary>
    /// <c>math:constant</c> — a named mathematical constant rounded to
    /// <paramref name="precision"/> decimal digits. Supports the EXSLT names
    /// (E, LN2, LN10, LOG2E, LOG10E, PI, SQRT1_2, SQRT2).
    /// </summary>
    public double constant(string name, double precision)
    {
        double value = name switch
        {
            "PI" => Math.PI,
            "E" => Math.E,
            "SQRT2" => Math.Sqrt(2),
            "SQRT1_2" => Math.Sqrt(0.5),
            "LN2" => Math.Log(2),
            "LN10" => Math.Log(10),
            "LOG2E" => 1.0 / Math.Log(2),
            "LOG10E" => 1.0 / Math.Log(10),
            _ => double.NaN,
        };

        if (double.IsNaN(value))
        {
            return double.NaN;
        }

        int digits = (int)precision;
        if (digits < 0)
        {
            digits = 0;
        }
        else if (digits > 15)
        {
            digits = 15;
        }

        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    private static bool TryNumber(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}

/// <summary>
/// EXSLT <c>date:</c> module — <see href="http://exslt.org/dates-and-times"/>.
/// Functions: datetime (date:date-time), date, time, year, month
/// (month-in-year), day (day-in-month), hour, minute, second.
/// </summary>
/// <remarks>
/// EXSLT's <c>date:date-time</c> is not a legal C# identifier; it is exposed
/// here as <c>datetime</c>. Likewise <c>month-in-year</c>/<c>day-in-month</c>
/// are exposed as <c>month</c>/<c>day</c>.
/// </remarks>
public sealed class ExsltDates
{
    /// <summary>
    /// <c>date:date-time</c> (exposed as <c>datetime</c>) — current local date and
    /// time as an ISO 8601 string with timezone offset.
    /// </summary>
    public string datetime() =>
        DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>date:date</c> — the date portion of <paramref name="dateTime"/>, as
    /// <c>yyyy-MM-dd</c>.
    /// </summary>
    public string date(string dateTime) =>
        Parse(dateTime, DateTimeOffset.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary><c>date:date</c> for the current date.</summary>
    public string date() => DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>date:time</c> — the time portion of <paramref name="dateTime"/>, as
    /// <c>HH:mm:ss</c>.
    /// </summary>
    public string time(string dateTime) =>
        Parse(dateTime, DateTimeOffset.Now).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary><c>date:time</c> for the current time.</summary>
    public string time() => DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary><c>date:year</c> of <paramref name="dateTime"/>.</summary>
    public double year(string dateTime) => Parse(dateTime, DateTimeOffset.Now).Year;

    /// <summary><c>date:year</c> of the current date-time.</summary>
    public double year() => DateTimeOffset.Now.Year;

    /// <summary><c>date:month-in-year</c> (exposed as <c>month</c>, 1–12).</summary>
    public double month(string dateTime) => Parse(dateTime, DateTimeOffset.Now).Month;

    /// <summary><c>date:month-in-year</c> of the current date-time.</summary>
    public double month() => DateTimeOffset.Now.Month;

    /// <summary><c>date:day-in-month</c> (exposed as <c>day</c>, 1–31).</summary>
    public double day(string dateTime) => Parse(dateTime, DateTimeOffset.Now).Day;

    /// <summary><c>date:day-in-month</c> of the current date-time.</summary>
    public double day() => DateTimeOffset.Now.Day;

    /// <summary><c>date:hour-in-day</c> (exposed as <c>hour</c>, 0–23).</summary>
    public double hour(string dateTime) => Parse(dateTime, DateTimeOffset.Now).Hour;

    /// <summary><c>date:minute-in-hour</c> (exposed as <c>minute</c>, 0–59).</summary>
    public double minute(string dateTime) => Parse(dateTime, DateTimeOffset.Now).Minute;

    /// <summary><c>date:second-in-minute</c> (exposed as <c>second</c>, 0–59).</summary>
    public double second(string dateTime) => Parse(dateTime, DateTimeOffset.Now).Second;

    private static DateTimeOffset Parse(string? text, DateTimeOffset fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset value)
            ? value
            : fallback;
    }
}

/// <summary>
/// EXSLT <c>set:</c> module — <see href="http://exslt.org/sets"/>.
/// Functions: difference, intersection, distinct.
/// </summary>
public sealed class ExsltSets
{
    /// <summary>
    /// <c>set:difference</c> — nodes in <paramref name="nodeSet1"/> whose
    /// string-value is absent from <paramref name="nodeSet2"/>.
    /// </summary>
    public XPathNodeIterator difference(XPathNodeIterator nodeSet1, XPathNodeIterator nodeSet2)
    {
        var exclude = new HashSet<string>(StringValues(nodeSet2), StringComparer.Ordinal);
        var result = new List<string>();
        if (nodeSet1 is not null)
        {
            while (nodeSet1.MoveNext())
            {
                string v = nodeSet1.Current?.Value ?? string.Empty;
                if (!exclude.Contains(v))
                {
                    result.Add(v);
                }
            }
        }

        return Exslt.BuildNodeSet(result);
    }

    /// <summary>
    /// <c>set:intersection</c> — nodes in <paramref name="nodeSet1"/> whose
    /// string-value also occurs in <paramref name="nodeSet2"/>.
    /// </summary>
    public XPathNodeIterator intersection(XPathNodeIterator nodeSet1, XPathNodeIterator nodeSet2)
    {
        var include = new HashSet<string>(StringValues(nodeSet2), StringComparer.Ordinal);
        var result = new List<string>();
        if (nodeSet1 is not null)
        {
            while (nodeSet1.MoveNext())
            {
                string v = nodeSet1.Current?.Value ?? string.Empty;
                if (include.Contains(v))
                {
                    result.Add(v);
                }
            }
        }

        return Exslt.BuildNodeSet(result);
    }

    /// <summary>
    /// <c>set:distinct</c> — one node per distinct string-value in
    /// <paramref name="nodeSet"/>, keeping the first occurrence in document order.
    /// </summary>
    public XPathNodeIterator distinct(XPathNodeIterator nodeSet)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        if (nodeSet is not null)
        {
            while (nodeSet.MoveNext())
            {
                string v = nodeSet.Current?.Value ?? string.Empty;
                if (seen.Add(v))
                {
                    result.Add(v);
                }
            }
        }

        return Exslt.BuildNodeSet(result);
    }

    private static IEnumerable<string> StringValues(XPathNodeIterator? nodeSet)
    {
        if (nodeSet is null)
        {
            yield break;
        }

        while (nodeSet.MoveNext())
        {
            yield return nodeSet.Current?.Value ?? string.Empty;
        }
    }
}

/// <summary>
/// EXSLT <c>exsl:</c> common module — <see href="http://exslt.org/common"/>.
/// </summary>
/// <remarks>
/// <see cref="XslCompiledTransform"/> already implements <c>exsl:node-set()</c>
/// and <c>exsl:object-type()</c> natively, and stylesheets should keep using
/// those. The methods here (<c>nodeSet</c>/<c>objectType</c>) are
/// identifier-friendly aliases for programmatic callers; the hyphenated EXSLT
/// names cannot be bound as CLR methods. Registering this object alongside the
/// native functions is harmless because the native names contain hyphens and so
/// never resolve to these methods.
/// </remarks>
public sealed class ExsltCommon
{
    /// <summary>
    /// Alias for <c>exsl:node-set</c> — converts a result-tree fragment into a
    /// node-set. Prefer the native <c>exsl:node-set()</c> in stylesheets.
    /// </summary>
    public XPathNodeIterator nodeSet(object fragment)
    {
        switch (fragment)
        {
            case XPathNodeIterator iterator:
                return iterator.Clone();
            case XPathNavigator navigator:
                return navigator.Select(".");
            case null:
                return Exslt.BuildNodeSet(Array.Empty<string>());
            default:
                return Exslt.BuildNodeSet(new[] { Convert.ToString(fragment, CultureInfo.InvariantCulture) ?? string.Empty });
        }
    }

    /// <summary>
    /// Alias for <c>exsl:object-type</c> — the EXSLT type name of
    /// <paramref name="value"/>: "node-set", "string", "number", "boolean" or
    /// "RTF".
    /// </summary>
    public string objectType(object value)
    {
        return value switch
        {
            XPathNodeIterator => "node-set",
            XPathNavigator => "node-set",
            bool => "boolean",
            double or int or long or float or decimal => "number",
            string => "string",
            null => "string",
            _ => "RTF",
        };
    }
}
