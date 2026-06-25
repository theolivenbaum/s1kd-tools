namespace S1kdTools.Tools;

/// <summary>
/// A single s1kd tool. Each implementation corresponds to one of the original
/// <c>s1kd-*</c> executables and exposes a process-free entry point so it can be
/// unit tested.
/// </summary>
public interface ITool
{
    /// <summary>Command name without the <c>s1kd-</c> prefix (e.g. "metadata").</summary>
    string Name { get; }

    /// <summary>One-line description shown in the top-level help listing.</summary>
    string Description { get; }

    /// <summary>Tool version (matches the upstream tool's VERSION define).</summary>
    string Version { get; }

    /// <summary>
    /// Run the tool. Returns the process exit code. IO is injected so callers
    /// (and tests) can capture output instead of touching the console.
    /// </summary>
    int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr);
}
