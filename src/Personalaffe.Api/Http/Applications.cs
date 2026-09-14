using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>
/// An application in an address or a query string: <c>knowledge</c>, the way
/// the contract spells it everywhere else.
/// </summary>
/// <remarks>
/// <para>
/// The value is read as text and turned into a <see cref="WorkspaceApplication"/>
/// here rather than bound as one, because the framework's binder parses an enum
/// by its C# name and case-sensitively — it would take <c>Knowledge</c> and
/// refuse <c>knowledge</c>, which is the one spelling the contract uses and the
/// only one both generated clients send.
/// </para>
/// <para>
/// Reading it by hand costs the document the parameter's type, so
/// <see cref="NamesAnApplication"/> puts it back: the document says a closed
/// set of four words, which is what a generated client needs to be worth
/// generating.
/// </para>
/// </remarks>
public static class Applications
{
    public const string Parameter = "application";

    /// <summary>The word the contract spells this application.</summary>
    public static string Wire(this WorkspaceApplication application) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(application.ToString());

    /// <summary>The application this word names.</summary>
    /// <exception cref="Refusal"><c>validation</c>: it names none of them.</exception>
    public static WorkspaceApplication Named(string? word) =>
        Chosen(word) ?? throw Unknown();

    /// <summary>
    /// The application this word names, or nothing where no word was given —
    /// which is how "all of them" is asked for.
    /// </summary>
    /// <exception cref="Refusal"><c>validation</c>: a word that names none of them.</exception>
    public static WorkspaceApplication? Chosen(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        foreach (var application in Enum.GetValues<WorkspaceApplication>())
        {
            if (string.Equals(application.Wire(), word, StringComparison.Ordinal))
            {
                return application;
            }
        }

        throw Unknown();
    }

    /// <summary>
    /// Gives the document back the type the hand-written parsing costs it: the
    /// parameter is one of four words, not any string at all.
    /// </summary>
    public static RouteHandlerBuilder NamesAnApplication(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            foreach (var parameter in operation.Parameters ?? [])
            {
                if (parameter.Name == Parameter && parameter is OpenApiParameter named)
                {
                    named.Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = [.. Enum.GetValues<WorkspaceApplication>().Select(a => (JsonNode)a.Wire())],
                    };
                }
            }

            return Task.CompletedTask;
        });
    }

    private static Refusal Unknown() => Refusal.Validation(
        Parameter,
        "The applications are "
        + string.Join(", ", Enum.GetValues<WorkspaceApplication>().Select(Wire))
        + ".");
}
