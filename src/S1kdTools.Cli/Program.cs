using System.Diagnostics;
using System.Reflection;
using S1kdTools.Tools;

// The s1kd CLI hosts every ported tool behind sub-commands:
//
//     s1kd <tool> [options] [files]
//
// For drop-in compatibility it also supports multi-call dispatch: when invoked
// via a name like "s1kd-metadata" (argv[0]), it routes straight to that tool,
// so symlinks reproduce the original per-tool executable names.

string invokedName = Path.GetFileNameWithoutExtension(
    Process.GetCurrentProcess().MainModule?.FileName ?? "s1kd");

var arguments = new List<string>(args);
ITool? tool = null;

if (invokedName.StartsWith("s1kd-", StringComparison.Ordinal))
{
    tool = ToolRegistry.Resolve(invokedName);
}

if (tool == null)
{
    if (arguments.Count == 0)
    {
        ShowTopLevelHelp();
        return 0;
    }

    string command = arguments[0];
    if (command is "-h" or "--help" or "help")
    {
        ShowTopLevelHelp();
        return 0;
    }
    if (command is "--version")
    {
        Console.WriteLine($"s1kd-tools (C# port) {Assembly.GetExecutingAssembly().GetName().Version}");
        return 0;
    }

    tool = ToolRegistry.Resolve(command);
    if (tool == null)
    {
        Console.Error.WriteLine($"s1kd: unknown tool '{command}'");
        Console.Error.WriteLine("Run 's1kd help' for the list of available tools.");
        return 127;
    }
    arguments.RemoveAt(0);
}

return tool.Run(arguments, Console.Out, Console.Error);

static void ShowTopLevelHelp()
{
    Console.WriteLine("s1kd-tools — tools for S1000D data (C# port)");
    Console.WriteLine();
    Console.WriteLine("Usage: s1kd <tool> [options] [files]");
    Console.WriteLine();
    Console.WriteLine("Available tools:");
    foreach (var t in ToolRegistry.Tools)
    {
        Console.WriteLine($"  {t.Name,-14} {t.Description}");
    }
    Console.WriteLine();
    Console.WriteLine("Run 's1kd <tool> --help' for tool-specific options.");
}
