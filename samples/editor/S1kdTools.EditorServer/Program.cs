using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.Extensions.FileProviders;
using S1kdTools.Editing;
using S1kdTools.EditorServer.Api;

// -----------------------------------------------------------------------------
// A back-end for the S1000D WYSIWYG editor.
//
// The editor is a browser application and S1000D is a server-side problem: the
// projection is XSLT over a DOM, the page preview is an XSL-FO layout, and the
// business-rule check is a BREX evaluation. All three are S1kdTools.Core, and none
// of them belongs in a browser. So the front-end holds no S1000D knowledge at all
// - it draws blocks and posts commands - and everything that knows what a data
// module is lives behind these endpoints.
//
// The document of record is the XML, and every endpoint answers with the whole
// state projected from it. See Api/Contracts.cs for why that is not a wasteful
// choice here.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    // EditMode reads as "text"/"attr"/"none" on the wire rather than as 0/1/2: the
    // front-end switches on it, and a number would make its code unreadable and
    // its meaning positional.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

SampleLayout layout = SampleLayout.Locate(builder.Configuration["csdb"], builder.Configuration["app"]);

builder.Services.AddSingleton(new CsdbLibrary(layout.Csdb, layout.Working));
builder.Services.AddSingleton(new Presentation(layout.Presentation, layout.Csdb));
builder.Services.AddSingleton<DocumentCheck>();

WebApplication app = builder.Build();

// -----------------------------------------------------------------------------
// the API
// -----------------------------------------------------------------------------

RouteGroupBuilder api = app.MapGroup("/api");

api.MapGet("/documents", (CsdbLibrary library) => library.List());

api.MapGet("/documents/{id}", (string id, CsdbLibrary library) => library.Read(id));

api.MapPost("/documents/{id}/commands", (string id, CommandsRequest request, CsdbLibrary library) =>
    library.Apply(id, request.Commands));

api.MapPut("/documents/{id}/xml", (string id, XmlRequest request, CsdbLibrary library) =>
    library.SetXml(id, request.Xml));

api.MapPost("/documents/{id}/undo", (string id, CsdbLibrary library) => library.Undo(id));
api.MapPost("/documents/{id}/redo", (string id, CsdbLibrary library) => library.Redo(id));
api.MapPost("/documents/{id}/revert", (string id, CsdbLibrary library) => library.Revert(id));
api.MapPost("/documents/{id}/save", (string id, CsdbLibrary library) => library.Save(id));

api.MapGet("/documents/{id}/check", (string id, CsdbLibrary library, DocumentCheck check) =>
{
    EditorState state = library.Read(id);
    return check.Check(state.Xml, state.Schema, state.Title);
});

// The page, laid out from what the editor holds rather than from what is on disk:
// an author who has just moved a warning wants to see it move.
//
// No caching. The preview is of a document being typed into, and the one thing a
// stale page must never do is look like the current one.
api.MapGet("/documents/{id}/pdf", (string id, CsdbLibrary library, Presentation presentation) =>
{
    EditorState state = library.Read(id);
    byte[] pdf = presentation.RenderPdf(state.Xml, state.Schema, state.Title);
    return Results.File(pdf, "application/pdf", $"{state.Code}.pdf");
});

// -----------------------------------------------------------------------------
// errors
// -----------------------------------------------------------------------------

// The messages these produce are the ones the author reads - "Nothing is at
// /dmodule[1]/…", the parser's line and column - so they are passed through rather
// than replaced with a status code and a shrug.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    Exception? error = context.Features
        .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

    (int status, string message) = error switch
    {
        KeyNotFoundException e => (StatusCodes.Status404NotFound, e.Message),
        EditCommandException e => (StatusCodes.Status400BadRequest, e.Message),
        XmlException e => (StatusCodes.Status400BadRequest, e.Message),
        FileNotFoundException e => (StatusCodes.Status404NotFound, e.Message),
        _ => (StatusCodes.Status500InternalServerError, error?.Message ?? "Unknown error."),
    };

    context.Response.StatusCode = status;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(new ErrorResponse(message));
}));

// -----------------------------------------------------------------------------
// the front-end
// -----------------------------------------------------------------------------

// Served straight out of the Transpose compiler's output folder rather than copied
// into this project's: `dotnet build` the front-end and refresh the browser. A
// copy step between the two would be one more thing to have forgotten when the
// page does not change.
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
