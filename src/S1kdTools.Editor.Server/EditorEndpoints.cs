using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

        api.MapGet("/documents", (CsdbLibrary library) => library.List());

        // What an author can add, each entry carrying the block it projects as. The
        // preview is built by the same template call an insert command makes and run
        // through the same stylesheet, so a palette card is drawn by the front-end's
        // own block renderer and cannot promise a shape that dropping it would not
        // produce.
        api.MapGet("/palette", (CsdbLibrary library) => EditPalette.Build(library.Profile));

        api.MapGet("/documents/{id}", (string id, CsdbLibrary library) =>
            Guarded(() => library.Read(id)));

        api.MapPost("/documents/{id}/commands",
            (string id, CommandsRequest request, CsdbLibrary library) =>
                Guarded(() => library.Apply(id, request.Commands)));

        api.MapPut("/documents/{id}/xml", (string id, XmlRequest request, CsdbLibrary library) =>
            Guarded(() => library.SetXml(id, request.Xml)));

        api.MapPost("/documents/{id}/undo", (string id, CsdbLibrary library) =>
            Guarded(() => library.Undo(id)));

        api.MapPost("/documents/{id}/redo", (string id, CsdbLibrary library) =>
            Guarded(() => library.Redo(id)));

        api.MapPost("/documents/{id}/revert", (string id, CsdbLibrary library) =>
            Guarded(() => library.Revert(id)));

        api.MapPost("/documents/{id}/save", (string id, CsdbLibrary library) =>
            Guarded(() => library.Save(id)));

        api.MapGet("/documents/{id}/check", (string id, CsdbLibrary library, DocumentCheck check) =>
            Guarded(() =>
            {
                EditorState state = library.Read(id);
                return check.Check(state.Xml, state.Schema, state.Title);
            }));

        // The page, laid out from what the editor holds rather than from what is on
        // disk: an author who has just moved a warning wants to see it move.
        //
        // No caching. The preview is of a document being typed into, and the one
        // thing a stale page must never do is look like the current one.
        // [FromServices] is not decoration. An optional service is not registered
        // when there is no presentation directory, and minimal APIs infer an
        // unregistered complex parameter as the request body — so without this the
        // endpoint is fine on a server that has stylesheets and throws at start-up
        // on one that does not.
        api.MapGet("/documents/{id}/pdf", (string id, CsdbLibrary library,
            [FromServices] EditorPresentation? presentation) => Guarded(() =>
        {
            if (presentation is null)
            {
                return Results.NotFound(new ErrorResponse(
                    "This server was started without presentation stylesheets, so it " +
                    "cannot lay a data module out."));
            }

            EditorState state = library.Read(id);
            byte[] pdf = presentation.RenderPdf(state.Xml, state.Schema, state.Title);
            return Results.File(pdf, "application/pdf", $"{state.Code}.pdf");
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
