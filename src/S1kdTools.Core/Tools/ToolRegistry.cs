namespace S1kdTools.Tools;

/// <summary>
/// Central registry of available tools. As tools are ported they are added
/// here; the CLI uses this to dispatch commands and build its help listing.
/// </summary>
public static class ToolRegistry
{
    private static readonly ITool[] AllTools =
    {
        new LsTool(),
        new MetadataTool(),
    };

    /// <summary>All registered tools, ordered by name.</summary>
    public static IReadOnlyList<ITool> Tools { get; } =
        AllTools.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();

    /// <summary>Resolve a tool by its command name (with or without the s1kd- prefix).</summary>
    public static ITool? Resolve(string name)
    {
        string normalized = name.StartsWith("s1kd-", StringComparison.Ordinal) ? name[5..] : name;
        return Tools.FirstOrDefault(t => t.Name == normalized);
    }
}
