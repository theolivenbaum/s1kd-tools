using System.Xml;

namespace S1kdTools.DocBook;

/// <summary>
/// XSLT extension object that resolves an S1000D graphic/object entity name to a
/// file reference. It stands in for two things the upstream stylesheets relied
/// on but that <see cref="System.Xml.Xsl.XslCompiledTransform"/> cannot provide:
///
/// <list type="bullet">
///   <item>the XSLT 1.0 <c>unparsed-entity-uri()</c> function (unsupported by
///   System.Xml), used by <c>s1kd2db.xsl</c>; and</item>
///   <item>the Java <c>InfoEntityResolver.resolve()</c> extension used by the
///   Smart Avionics <c>s1000dtodb</c> stylesheets.</item>
/// </list>
///
/// It is registered on the <see cref="System.Xml.Xsl.XsltArgumentList"/> under
/// the namespace URI <c>InfoEntityResolver</c> (matching the prefix the
/// stylesheets bind to), exposing a single <c>resolve</c> method — the patched
/// stylesheets call <c>ier:resolve(name)</c> in place of the original functions.
///
/// Resolution order mirrors the originals: an explicit info-entity map (the
/// <c>info-entity-map.txt</c> properties file the Java resolver read) wins,
/// then the unparsed (NDATA) entities declared in the source document's DTD
/// (what <c>unparsed-entity-uri()</c> returned), and finally the entity name is
/// returned unchanged.
/// </summary>
public sealed class EntityUriResolver
{
    private readonly Dictionary<string, string> _map;

    public EntityUriResolver(IReadOnlyDictionary<string, string>? unparsedEntities = null,
                             IReadOnlyDictionary<string, string>? infoEntityMap = null)
    {
        _map = new Dictionary<string, string>(StringComparer.Ordinal);
        // DTD entities first, then the info-entity map overrides them (the Java
        // resolver, when present, took priority in the stylesheet's xsl:choose).
        if (unparsedEntities != null)
            foreach (var kv in unparsedEntities)
                _map[kv.Key] = kv.Value;
        if (infoEntityMap != null)
            foreach (var kv in infoEntityMap)
                _map[kv.Key] = kv.Value;
    }

    /// <summary>
    /// Called from the stylesheets as <c>ier:resolve(name)</c>. Returns the
    /// resolved file reference, or the entity name itself when unknown.
    /// </summary>
    public string resolve(string entityName) =>
        _map.TryGetValue(entityName, out string? uri) ? uri : entityName;

    /// <summary>
    /// Collect the unparsed (NDATA) entity declarations from a document's DTD
    /// into a name → system-id map, reproducing <c>unparsed-entity-uri()</c>.
    /// </summary>
    public static Dictionary<string, string> ReadUnparsedEntities(XmlDocument doc)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        XmlNamedNodeMap? entities = doc.DocumentType?.Entities;
        if (entities != null)
        {
            foreach (XmlEntity entity in entities)
            {
                // An unparsed entity is one declared with NDATA (a notation).
                if (entity.NotationName != null)
                    map[entity.Name] = entity.SystemId ?? entity.Name;
            }
        }
        return map;
    }

    /// <summary>
    /// Parse a Java-style <c>info-entity-map.txt</c> properties file
    /// (<c>name=value</c> per line, <c>#</c>/<c>!</c> comments) into a map.
    /// </summary>
    public static Dictionary<string, string> ReadInfoEntityMap(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or '!')
                continue;
            int eq = line.IndexOfAny(new[] { '=', ':' });
            if (eq <= 0)
                continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (key.Length > 0)
                map[key] = value;
        }
        return map;
    }
}
