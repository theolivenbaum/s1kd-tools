using Microsoft.Extensions.FileProviders;
using S1kdTools.Editor.Server;

// -----------------------------------------------------------------------------
// The editor sample's back-end.
//
// Everything the editor does lives in the S1kdTools.Editor.Server package: the
// CSDB and its open sessions, the edit commands, the check, the page layout and
// the endpoints. What is left here is what is genuinely this sample's — which
// folders to read, and serving the front-end beside the API.
//
// That is the shape a real deployment has too. If this file is longer than yours,
// it is because of the "front-end has not been built yet" page at the bottom.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

SampleLayout layout = SampleLayout.Locate(builder.Configuration["csdb"], builder.Configuration["app"]);

builder.Services.AddS1kdEditor(new EditorOptions
{
    CsdbDirectory = layout.Csdb,
    PresentationDirectory = layout.Presentation,

    // Saving writes beside the repository rather than over the checked-in data
    // modules, so the sample can be run, edited and saved as often as you like. A
    // server that owns its CSDB leaves this unset and saves back over it.
    WorkingDirectory = layout.Working,
});

WebApplication app = builder.Build();

app.MapS1kdEditor();

// -----------------------------------------------------------------------------
// the front-end
// -----------------------------------------------------------------------------

// Served straight out of the Transpose compiler's output folder rather than copied
// into this project's: `dotnet build` the front-end and refresh the browser. A copy
// step between the two would be one more thing to have forgotten when the page does
// not change.
if (Directory.Exists(layout.App))
{
    var files = new PhysicalFileProvider(layout.App);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ServeUnknownFileTypes = true });
    app.Logger.LogInformation("Serving the editor from {Path}", layout.App);
}
else
{
    app.MapGet("/", () => Results.Content(SampleLayout.NotBuiltPage(layout.App), "text/html"));
    app.Logger.LogWarning("The editor front-end has not been built yet: {Path} does not exist.", layout.App);
}

app.Logger.LogInformation("CSDB: {Csdb}", layout.Csdb);
app.Logger.LogInformation("Presentation stylesheets: {Xsl}", layout.Presentation);
app.Logger.LogInformation("Saved copies: {Working}", layout.Working);

app.Run();

/// <summary>
/// Where the sample's parts are, found by walking up from the running assembly to
/// the repository.
///
/// A configuration file would be the usual answer, and would be wrong for a
/// sample: the one thing someone cloning the repository should not have to do is
/// tell the server where the repository is. Each path can still be overridden -
/// <c>--csdb</c> and <c>--app</c> - for anyone pointing it at their own CSDB.
/// </summary>
internal sealed record SampleLayout(string Csdb, string Presentation, string Working, string App)
{
    private const string FrontEndOutput =
        "S1kdTools.Editor.App/bin/Debug/netstandard2.0/tps";

    public static SampleLayout Locate(string? csdb, string? appOutput)
    {
        string root = RepositoryRoot();
        string editor = Path.Combine(root, "samples", "editor");

        return new SampleLayout(
            Csdb: Path.GetFullPath(csdb ?? Path.Combine(editor, "csdb")),
            Presentation: Path.Combine(editor, "presentation"),
            Working: Path.Combine(root, "samples", "out", "editor"),
            App: Path.GetFullPath(appOutput ?? Path.Combine(editor, FrontEndOutput)));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "S1kdTools.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root (no S1kdTools.slnx above the running assembly). " +
                "Pass --csdb and --app to run against your own folders.");
    }

    /// <summary>
    /// What to serve when the front-end has not been compiled. A blank page and a
    /// 404 would send someone looking for a server fault; the build command they
    /// are missing is the only useful thing to say.
    /// </summary>
    public static string NotBuiltPage(string expected) =>
        $$"""
        <!doctype html>
        <meta charset="utf-8">
        <title>s1kd editor — front-end not built</title>
        <style>
          body { font: 15px/1.6 system-ui, sans-serif; margin: 4rem auto; max-width: 46rem; padding: 0 1.5rem; }
          code, pre { font-family: ui-monospace, monospace; }
          pre { background: #f4f4f5; padding: .9rem 1.1rem; border-radius: 6px; overflow-x: auto; }
        </style>
        <h1>The editor has not been built yet</h1>
        <p>The API is running — try <a href="/api/documents">/api/documents</a> — but the
           front-end was not found at:</p>
        <pre>{{expected}}</pre>
        <p>Build it, then refresh this page:</p>
        <pre>dotnet tool update --global Transpose.Compiler
        dotnet build S1kdTools.Editor.slnx</pre>
        """;
}
