using System.Reflection;

namespace S1kdTools.Tools;

/// <summary>
/// Central registry of available tools. Tools are discovered automatically by
/// reflection: any public, non-abstract <see cref="ITool"/> with a parameterless
/// constructor in this assembly is registered. This lets new tools be added as
/// standalone files without editing a shared list.
/// </summary>
public static class ToolRegistry
{
    /// <summary>All registered tools, ordered by name.</summary>
    public static IReadOnlyList<ITool> Tools { get; } = DiscoverTools();

    private static ITool[] DiscoverTools()
    {
        return typeof(ToolRegistry).Assembly
            .GetTypes()
            .Where(t => typeof(ITool).IsAssignableFrom(t)
                        && t is { IsAbstract: false, IsInterface: false }
                        && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (ITool)Activator.CreateInstance(t)!)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Resolve a tool by its command name (with or without the s1kd- prefix).</summary>
    public static ITool? Resolve(string name)
    {
        string normalized = name.StartsWith("s1kd-", StringComparison.Ordinal) ? name[5..] : name;
        return Tools.FirstOrDefault(t => t.Name == normalized);
    }
}
