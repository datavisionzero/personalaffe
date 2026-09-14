using Microsoft.OpenApi;

namespace Personalaffe.Api.Http;

/// <summary>
/// The two operations in this product whose body is not JSON: an upload sends
/// bytes and a download answers them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The body of an upload is the file.</strong> There is no multipart
/// envelope around it: an agent that has bytes should be able to send bytes,
/// `curl --data-binary @thing` should work, and a CLI should not have to
/// assemble a MIME document to store a file. What multipart would buy is more
/// than one file per request and fields beside them, and this API wants
/// neither — the name is a query parameter and the media type is
/// <c>Content-Type</c>, which is what that header is for.
/// </para>
/// <para>
/// The endpoints read <c>HttpRequest.Body</c> by hand, so the document would
/// otherwise say they take nothing at all. These transformers put the shape
/// back, because <strong>both clients are generated from the document</strong>:
/// without them the Go client would take no body and the TypeScript one would
/// take a string.
/// </para>
/// </remarks>
public static class FileBodies
{
    /// <summary>What an upload sends and a download answers.</summary>
    public const string OctetStream = "application/octet-stream";

    /// <summary>Says in the contract that this operation's body is the file.</summary>
    public static RouteHandlerBuilder TakesBytes(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description =
                    "The file, as the body. `Content-Type` is the media type it is stored as; anything "
                    + "that is not a media type is stored as `application/octet-stream`.",
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    [OctetStream] = new()
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                    },
                },
            };

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Says in the contract that this operation answers a zip — the Knowledge
    /// export (PERSONAL-E7).
    /// </summary>
    /// <remarks>
    /// It is here rather than in <c>KnowledgeEndpoints</c> because it is the
    /// same problem as a download's: a handler that writes bytes rather than
    /// returning a typed result leaves the document saying it answers nothing,
    /// and both clients are generated from the document.
    /// </remarks>
    public static RouteHandlerBuilder AnswersAZip(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Responses ??= [];
            operation.Responses["200"] = new OpenApiResponse
            {
                Description = "A zip of Markdown files, as an attachment.",
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    ["application/zip"] = new()
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                    },
                },
            };

            return Task.CompletedTask;
        });
    }

    /// <summary>Says in the contract that this operation answers the file itself.</summary>
    public static RouteHandlerBuilder AnswersBytes(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Responses ??= [];
            operation.Responses["200"] = new OpenApiResponse
            {
                Description =
                    "The file's bytes, as an attachment. The media type is the one it was stored with.",
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    [OctetStream] = new()
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                    },
                },
            };

            return Task.CompletedTask;
        });
    }
}
