using System.Xml;
using S1kdTools.Presentation;

namespace S1kdTools.Presentation.Tests;

/// <summary>
/// Locates the sample CSDB objects that ship with this test project — one per
/// <see cref="CsdbObjectType"/> — and the directory the rendered samples are
/// written to.
/// </summary>
public static class SampleObjects
{
    /// <summary>The <c>Samples</c> directory copied next to the test assembly.</summary>
    public static string Directory { get; } =
        Path.Combine(AppContext.BaseDirectory, "Samples");

    /// <summary>
    /// Where rendered samples are written: <c>samples/out/presentation</c> in the
    /// repository when the tests run from a working copy, otherwise a directory
    /// beside the test assembly.
    /// </summary>
    public static string OutputDirectory { get; } = ResolveOutputDirectory();

    /// <summary>Every object type paired with its sample file, for use as xUnit theory data.</summary>
    public static TheoryData<CsdbObjectType> AllTypes()
    {
        var data = new TheoryData<CsdbObjectType>();
        foreach (CsdbObjectTypeInfo info in CsdbObjectTypes.Catalogue)
        {
            data.Add(info.Type);
        }
        return data;
    }

    /// <summary>The sample file for <paramref name="type"/>.</summary>
    public static string PathFor(CsdbObjectType type) =>
        Path.Combine(Directory, CsdbObjectTypes.Info(type).Schema + ".xml");

    /// <summary>Load the sample object for <paramref name="type"/>.</summary>
    public static XmlDocument Load(CsdbObjectType type) =>
        S1000DPresentation.Load(PathFor(type));

    /// <summary>The options the samples are rendered with: the shipped ICN is resolvable.</summary>
    public static PresentationOptions Options { get; } = new()
    {
        GraphicsDirectories = [Directory],
    };

    private static string ResolveOutputDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "S1kdTools.slnx")))
        {
            dir = dir.Parent;
        }

        string target = dir != null
            ? Path.Combine(dir.FullName, "samples", "out", "presentation")
            : Path.Combine(AppContext.BaseDirectory, "out");

        System.IO.Directory.CreateDirectory(target);
        return target;
    }
}
