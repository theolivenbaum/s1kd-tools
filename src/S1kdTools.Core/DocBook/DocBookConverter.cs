using System.Text;
using System.Xml;
using System.Xml.Xsl;

namespace S1kdTools.DocBook;

/// <summary>
/// Which stylesheet set to use when converting S1000D to DocBook 5.
/// </summary>
public enum DocBookProfile
{
    /// <summary>
    /// <c>kibook/s1kd2db</c> — a single, self-contained stylesheet that maps
    /// S1000D constructs to their simplest DocBook 5 equivalents. The default;
    /// lean and intended for further conversion (e.g. via pandoc).
    /// </summary>
    S1kd2db,

    /// <summary>
    /// <c>kibook/S1000D-XSL-Stylesheets</c> (the Smart Avionics
    /// <c>s1000dtodb</c> stage) — a larger, presentation-oriented stylesheet set
    /// with broader S1000D coverage (IPD, fault, crew, procedures, cross-
    /// reference tables, configurable content). Also emits DocBook 5; it is the
    /// first half of that project's S1000D → DocBook → XSL-FO → PDF pipeline.
    /// </summary>
    SmartAvionics,
}

/// <summary>
/// Converts S1000D data modules to DocBook 5, the way the upstream <c>s1kd2db</c>
/// and <c>S1000D-XSL-Stylesheets</c> projects do, but entirely in-process on
/// <see cref="XslCompiledTransform"/> (no <c>xsltproc</c>, no Java, no external
/// DocBook stylesheets). DocBook is the conversion target; rendering DocBook to
/// a final format (PDF/HTML/…) is a downstream concern best handed to existing
/// DocBook tooling.
///
/// Both stylesheet sets are embedded as resources. The only modification made to
/// them is documented inline in the XSLT (a one-line shim replacing the
/// unsupported <c>unparsed-entity-uri()</c> with the <see cref="EntityUriResolver"/>
/// extension); see those files' PORT NOTE comments.
/// </summary>
public static class DocBookConverter
{
    private const string EntityResolverNamespace = "InfoEntityResolver";

    private static readonly object Gate = new();
    private static readonly Dictionary<DocBookProfile, XslCompiledTransform> Cache = new();

    private static (string dir, string file) Stylesheet(DocBookProfile profile) => profile switch
    {
        DocBookProfile.S1kd2db => ("s1kd2db", "s1kd2db.xsl"),
        DocBookProfile.SmartAvionics => ("s1000dtodb", "s1000dtodb.xsl"),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    /// <summary>Convert an S1000D document given as a string.</summary>
    public static string Convert(
        string xml,
        DocBookProfile profile = DocBookProfile.S1kd2db,
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlyDictionary<string, string>? infoEntityMap = null)
    {
        XmlDocument doc = LoadInput(new StringReader(xml));
        return Convert(doc, profile, parameters, infoEntityMap);
    }

    /// <summary>Convert an S1000D document loaded from a file path.</summary>
    public static string ConvertFile(
        string path,
        DocBookProfile profile = DocBookProfile.S1kd2db,
        IReadOnlyDictionary<string, string>? parameters = null,
        string? infoEntityMapPath = null)
    {
        using var stream = File.OpenRead(path);
        XmlDocument doc = LoadInput(stream);
        var map = infoEntityMapPath != null
            ? EntityUriResolver.ReadInfoEntityMap(File.ReadAllText(infoEntityMapPath))
            : null;
        return Convert(doc, profile, parameters, map);
    }

    /// <summary>Convert an already-parsed S1000D document.</summary>
    public static string Convert(
        XmlDocument doc,
        DocBookProfile profile = DocBookProfile.S1kd2db,
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlyDictionary<string, string>? infoEntityMap = null)
    {
        XslCompiledTransform xslt = GetTransform(profile);

        var args = new XsltArgumentList();
        var resolver = new EntityUriResolver(EntityUriResolver.ReadUnparsedEntities(doc), infoEntityMap);
        args.AddExtensionObject(EntityResolverNamespace, resolver);
        if (parameters != null)
            foreach (var kv in parameters)
                args.AddParam(kv.Key, string.Empty, kv.Value);

        // OutputSettings carries the stylesheet's own xsl:output (encoding,
        // indent, doctype), so the result matches what xsltproc would emit.
        // Force a BOM-less encoding so the output matches xsltproc (libxml2
        // emits no byte-order mark).
        XmlWriterSettings writerSettings = (xslt.OutputSettings ?? new XmlWriterSettings()).Clone();
        Encoding encoding = writerSettings.Encoding is UnicodeEncoding
            ? writerSettings.Encoding
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        writerSettings.Encoding = encoding;

        using var ms = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(ms, writerSettings))
        {
            xslt.Transform(doc, args, writer);
        }

        byte[] bytes = ms.ToArray();
        byte[] preamble = encoding.GetPreamble();
        int start = bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
        return encoding.GetString(bytes, start, bytes.Length - start);
    }

    private static XslCompiledTransform GetTransform(DocBookProfile profile)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(profile, out XslCompiledTransform? cached))
                return cached;

            (string dir, string file) = Stylesheet(profile);
            var resolver = new EmbeddedXslResolver(dir);
            using Stream sheet = EmbeddedResources.Open($"{dir}/{file}")
                ?? throw new FileNotFoundException($"Embedded stylesheet not found: {dir}/{file}");
            // A synthetic base URI lets the stylesheet's relative xsl:include
            // hrefs resolve back to embedded resources via EmbeddedXslResolver.
            var readerSettings = new XmlReaderSettings { XmlResolver = resolver };
            using XmlReader reader = XmlReader.Create(sheet, readerSettings, resolver.BaseUriFor(file).ToString());

            var xslt = new XslCompiledTransform();
            xslt.Load(reader, new XsltSettings(enableDocumentFunction: true, enableScript: false), resolver);
            Cache[profile] = xslt;
            return xslt;
        }
    }

    private static XmlDocument LoadInput(TextReader textReader)
    {
        // DtdProcessing.Parse so the internal DTD subset (the NDATA graphic
        // entities) is available; a null resolver keeps it offline — unparsed
        // entities are never expanded, only looked up by name.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(textReader, settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        return doc;
    }

    private static XmlDocument LoadInput(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(stream, settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        return doc;
    }
}
