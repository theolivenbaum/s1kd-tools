using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using S1kdTools.Editing;

namespace S1kdTools.Editor.Server;

/// <summary>
/// The editor's back-end, as two calls on a web application.
///
/// <code>
/// builder.Services.AddS1kdEditor(new EditorOptions
/// {
///     CsdbDirectory = "csdb",
///     PresentationDirectory = "presentation",
/// });
///
/// app.MapS1kdEditor();
/// </code>
///
/// S1000D is a server-side problem — the projection is XSLT over a DOM, the page
/// preview is an XSL-FO layout, the business-rule check is a BREX evaluation — so
/// everything that knows what a data module is lives behind these endpoints and the
/// browser draws blocks. <c>S1kdTools.Editor</c> is the browser half and speaks
/// exactly this protocol.
///
/// <b>Every editing endpoint answers with the whole state rather than a delta.</b>
/// A block's path is only valid against the revision it was projected from, so a
/// client patching a model it already holds would be reasoning about paths the
/// server has renumbered. A data module's projection is a few tens of kilobytes of
/// JSON; a class of bug is worth more than that.
/// </summary>
public static class EditorEndpoints
{
    /// <summary>
    /// Register the editor's services.
    ///
    /// The library holds a CSDB and its open sessions, so the registrations are
    /// singletons — which is also the honest shape: one session per object, shared,
    /// the way a real authoring system checks a module out to one author. An
    /// application wanting a session per user registers its own
    /// <see cref="CsdbLibrary"/> per scope instead.
    /// </summary>
    public static IServiceCollection AddS1kdEditor(this IServiceCollection services,
        EditorOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(new CsdbLibrary(
            options.CsdbDirectory,
            options.WorkingDirectory ?? options.CsdbDirectory,
            options.Profile));

        // Registered only when there is one, rather than registered as null: a
        // service that resolves to null is a trap for everything downstream, and
        // "no page preview" is a supported way to run rather than a broken one.
        IResourceResolver? stylesheets =
            options.PresentationStylesheets ?? Folder(options.PresentationDirectory);

        if (stylesheets is not null)
        {
            services.AddSingleton(new EditorPresentation(stylesheets,
                options.Graphics
                ?? ResourceResolvers.Directory(
                    [options.GraphicsDirectory ?? options.CsdbDirectory],
                    EditorPresentation.GraphicExtensions)));
        }

        services.AddSingleton(provider =>
            new DocumentCheck(provider.GetService<EditorPresentation>()));

        // What the endpoints are made of, registered so an application can map its
        // own instead of - or alongside - the ones MapS1kdEditor defines.
        services.AddSingleton(provider => new EditorOperations(
            provider.GetRequiredService<CsdbLibrary>(),
            provider.GetRequiredService<DocumentCheck>(),
            provider.GetService<EditorPresentation>()));

        // The model's own enums read as words on the wire: a front-end switches on
        // EditMode, and a number would make its code unreadable and its meaning
        // positional.
        services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            json.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        return services;
    }

    private static IResourceResolver? Folder(string? directory) =>
        directory is null ? null : ResourceResolvers.Directory([directory]);

    /// <summary>Map the editor's endpoints under <see cref="EditorOptions.RoutePrefix"/>.</summary>
    public static RouteGroupBuilder MapS1kdEditor(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<EditorOptions>();
        RouteGroupBuilder api = endpoints.MapGroup(options.RoutePrefix);

        // Every one of these is a single call on EditorOperations and nothing
        // else. That is deliberate and worth keeping: an application that wants its
        // editor reached some other way - its own paths, a controller, an
        // authorization filter per operation - takes that class and maps it, and
        // there is nothing here for it to have to re-derive.
        api.MapGet("/documents", (EditorOperations editor) => editor.List());

        // What an author can add, each entry carrying the block it projects as. The
        // preview is built by the same template call an insert command makes and run
        // through the same stylesheet, so a palette card is drawn by the front-end's
        // own block renderer and cannot promise a shape that dropping it would not
        // produce.
        api.MapGet("/palette", (EditorOperations editor) => editor.Palette());

        api.MapGet("/documents/{id}", (string id, EditorOperations editor) =>
            Guarded(() => editor.Read(id)));

        // The same catalogue, narrowed to what this object can actually take - the
        // one a front-end should ask for once a document is open.
        api.MapGet("/documents/{id}/palette", (string id, EditorOperations editor) =>
            Guarded(() => editor.Palette(id)));

        api.MapPost("/documents/{id}/commands",
            (string id, CommandsRequest request, EditorOperations editor) =>
                Guarded(() => editor.Apply(id, request.Commands)));

        api.MapPut("/documents/{id}/xml", (string id, XmlRequest request, EditorOperations editor) =>
            Guarded(() => editor.SetXml(id, request.Xml)));

        api.MapPost("/documents/{id}/undo", (string id, EditorOperations editor) =>
            Guarded(() => editor.Undo(id)));

        api.MapPost("/documents/{id}/redo", (string id, EditorOperations editor) =>
            Guarded(() => editor.Redo(id)));

        api.MapPost("/documents/{id}/revert", (string id, EditorOperations editor) =>
            Guarded(() => editor.Revert(id)));

        api.MapPost("/documents/{id}/save", (string id, EditorOperations editor) =>
            Guarded(() => editor.Save(id)));

        api.MapGet("/documents/{id}/check", (string id, EditorOperations editor) =>
            Guarded(() => editor.Check(id)));

        // The page, laid out from what the editor holds rather than from what is on
        // disk. No caching: the preview is of a document being typed into, and the
        // one thing a stale page must never do is look like the current one.
        api.MapGet("/documents/{id}/pdf", (string id, EditorOperations editor) => Guarded(() =>
        {
            EditorPdf? pdf = editor.RenderPdf(id);

            return pdf is null
                ? Results.NotFound(new ErrorResponse(
                    "This server was started without presentation stylesheets, so it " +
                    "cannot lay a data module out."))
                : Results.File(pdf.Content, "application/pdf", pdf.FileName);
        }));

        return api;
    }

    /// <summary>
    /// Turn the exceptions this library raises into the answers they mean.
    ///
    /// Here rather than in an exception-handling middleware, because a host has its
    /// own opinion about unhandled exceptions and these are not unhandled — "nothing
    /// is at that path" and "line 12 is not well-formed" are answers. Their messages
    /// are written for the author to read, so they are passed through rather than
    /// replaced with a status code and a shrug.
    /// </summary>
    private static IResult Guarded<T>(Func<T> work)
    {
        try
        {
            T value = work();
            return value is IResult result ? result : Results.Ok(value);
        }
        catch (KeyNotFoundException e)
        {
            return Results.NotFound(new ErrorResponse(e.Message));
        }
        catch (EditCommandException e)
        {
            return Results.BadRequest(new ErrorResponse(e.Message));
        }
        catch (XmlException e)
        {
            return Results.BadRequest(new ErrorResponse(e.Message));
        }
        catch (FileNotFoundException e)
        {
            return Results.NotFound(new ErrorResponse(e.Message));
        }
    }
}
