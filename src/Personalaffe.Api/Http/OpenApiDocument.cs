using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Personalaffe.Api.Hosting;

namespace Personalaffe.Api.Http;

/// <summary>
/// The document at <c>/api/openapi/v1.json</c>: captured into
/// <c>docs/api/openapi.json</c>, checked in, and compared by a test against
/// what a running instance serves. Both clients are generated from it, so it
/// has to describe the shape of the API and nothing about the machine that
/// happened to serve it.
/// </summary>
/// <remarks>
/// <c>info.version</c> is the instance's version, not an API version — the API
/// carries none. The trunk builds as <c>0.0.0-dev</c>, which is what the
/// checked-in document says and what CI's capture serves; a released image says
/// its tag, which is the right thing for the copy an operator reads.
/// </remarks>
public static class OpenApiDocument
{
    /// <summary>
    /// A CLR type ending in <c>Shape</c> is the contract's shape of the Domain
    /// type of the same name, so the suffix is dropped from the schema id and
    /// both generated clients see the name the document uses. Nothing carries
    /// the suffix yet; the rule is here so that the first type to need it does
    /// not have to also decide this.
    /// </summary>
    private const string ShapeSuffix = "Shape";

    public static IServiceCollection AddPersonalaffeOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.CreateSchemaReferenceId = info =>
            {
                var id = OpenApiOptions.CreateDefaultSchemaReferenceId(info);
                return id is not null && id.EndsWith(ShapeSuffix, StringComparison.Ordinal)
                    ? id[..^ShapeSuffix.Length]
                    : id;
            };

            options.AddSchemaTransformer((schema, context, _) =>
            {
                // Every enum travels as its name (JsonStringEnumConverter), and
                // every timestamp as RFC 3339 (Rfc3339); the generator sees
                // neither converter, so the types are said here.
                var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
                var nullable = type != context.JsonTypeInfo.Type || !type.IsValueType;

                if (type.IsEnum)
                {
                    schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
                }

                if (type == typeof(DateTimeOffset))
                {
                    schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
                    schema.Format = "date-time";
                }

                Plain(schema);
                return Task.CompletedTask;
            });

            options.AddDocumentTransformer((document, _, _) =>
            {
                // The generators read the document, and two things the .NET
                // generator writes are true of JSON and useless to them: a
                // number that "may also arrive as a string", and a body that
                // "may also be null". Both are undone here, for every parameter
                // and every body.
                foreach (var operation in document.Paths.Values
                             .Where(path => path.Operations is not null)
                             .SelectMany(path => path.Operations!.Values))
                {
                    foreach (var parameter in operation.Parameters ?? [])
                    {
                        Plain(parameter.Schema);
                    }

                    if (operation.RequestBody is OpenApiRequestBody body)
                    {
                        foreach (var content in body.Content?.Values ?? Enumerable.Empty<OpenApiMediaType>())
                        {
                            content.Schema = Unwrapped(content.Schema);
                        }

                        body.Required = true;
                    }
                }

                document.Info.Title = "personalaffe";
                document.Info.Version = InstanceVersion.Value;
                document.Info.Description =
                    "The HTTP surface of one personalaffe instance. Every instance is at its own address; "
                    + "every endpoint is under `/api`, and every answer carries the instance's version in "
                    + "`Personalaffe-Version`. Errors are one problem document whose relative `type` ends in "
                    + "the code a client switches on.";

                // Whoever captured it was at some address; nobody else is.
                document.Servers?.Clear();

                return Task.CompletedTask;
            });
        });

    /// <summary>
    /// A number, an integer or a boolean is that and not "also a string": the
    /// string half is how ASP.NET reads a query value, not what the value is.
    /// The pattern it adds for the same reason goes with it.
    /// </summary>
    private static void Plain(IOpenApiSchema? schema)
    {
        if (schema is not OpenApiSchema concrete || concrete.Type is not { } type)
        {
            return;
        }

        if (type.HasFlag(JsonSchemaType.String)
            && (type & (JsonSchemaType.Integer | JsonSchemaType.Number | JsonSchemaType.Boolean)) != 0)
        {
            concrete.Type = type & ~JsonSchemaType.String;
            concrete.Pattern = null;
        }
    }

    /// <summary>
    /// A body of <c>oneOf [null, X]</c> is a body of <c>X</c>: an empty body is
    /// an empty object, and a generated client should send one.
    /// </summary>
    private static IOpenApiSchema? Unwrapped(IOpenApiSchema? schema)
    {
        if (schema is OpenApiSchema { OneOf: { Count: 2 } alternatives }
            && alternatives.Any(alternative => alternative is OpenApiSchema { Type: JsonSchemaType.Null }))
        {
            return alternatives.Single(alternative => alternative is not OpenApiSchema { Type: JsonSchemaType.Null });
        }

        return schema;
    }
}
