using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>
/// The one place a refusal becomes a problem document (<c>docs/api.md</c>,
/// Errors): every error is <c>application/problem+json</c> with a stable,
/// relative <c>type</c> whose last segment is the code a client switches on.
/// </summary>
/// <remarks>
/// The status of each code is here and nowhere else — not in Domain, where the
/// codes live, because a status is HTTP's word for it and the CLI has its own.
/// </remarks>
public static class Problems
{
    public const string ContentType = "application/problem+json";

    private const string TypePrefix = "/problems/";

    /// <summary>The wire spelling of a code: <c>NotFound</c> is <c>not-found</c>.</summary>
    public static string CodeOf(RefusalCode code) => JsonNamingPolicy.KebabCaseLower.ConvertName(code.ToString());

    public static string TypeOf(RefusalCode code) => TypePrefix + CodeOf(code);

    public static int StatusOf(RefusalCode code) => code switch
    {
        RefusalCode.Validation or RefusalCode.UnknownField => StatusCodes.Status400BadRequest,
        RefusalCode.Unauthenticated => StatusCodes.Status401Unauthorized,
        RefusalCode.Forbidden => StatusCodes.Status403Forbidden,
        RefusalCode.NotFound => StatusCodes.Status404NotFound,
        RefusalCode.Conflict => StatusCodes.Status409Conflict,
        RefusalCode.Stale => StatusCodes.Status412PreconditionFailed,
        RefusalCode.Internal => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A refusal code without a status."),
    };

    public static string TitleOf(RefusalCode code) => code switch
    {
        RefusalCode.Validation => "A field is missing, malformed or over its limit",
        RefusalCode.UnknownField => "The request contains a field this object does not define",
        RefusalCode.Unauthenticated => "No credential, an unknown one, or a revoked one",
        RefusalCode.Forbidden => "The caller may not do this",
        RefusalCode.NotFound => "Nothing at that address",
        RefusalCode.Stale => "The object has changed since it was read",
        RefusalCode.Conflict => "Something else already occupies that name or place",
        RefusalCode.Internal => "Something went wrong on the server",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A refusal code without a title."),
    };

    /// <summary>The document for <paramref name="code"/> on <paramref name="instance"/>.</summary>
    public static ProblemDetails Document(
        RefusalCode code,
        string? detail,
        string? instance,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var document = new ProblemDetails
        {
            Type = TypeOf(code),
            Title = TitleOf(code),
            Status = StatusOf(code),
            Detail = detail,
            Instance = instance,
        };

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                document.Extensions[key] = value;
            }
        }

        return document;
    }

    /// <summary>
    /// The document as an endpoint's result, for the refusals an endpoint makes
    /// itself rather than lets out of an act.
    /// </summary>
    /// <remarks>
    /// Written as JSON rather than through <c>Results.Problem</c>, which would
    /// hand it to the problem-details service and have a <c>trace_id</c> added
    /// on the way out. Every refusal is one document with the members its code
    /// calls for and no others — and two refusals that are meant to be
    /// indistinguishable, as sign-in's are, have to be indistinguishable to the
    /// byte.
    /// </remarks>
    public static IResult Result(
        RefusalCode code, string? detail, IReadOnlyDictionary<string, object?>? extensions = null) =>
        Written(Document(code, detail, instance: null, extensions));

    /// <summary>The <c>validation</c> document: <c>errors</c> maps field to messages.</summary>
    public static IResult Validation(IReadOnlyDictionary<string, string[]> errors) =>
        Written(Document(Refusal.Validation(errors)));

    /// <inheritdoc cref="Validation(IReadOnlyDictionary{string, string[]})"/>
    public static IResult Validation(string field, string message) =>
        Written(Document(Refusal.Validation(field, message)));

    private static IResult Written(ProblemDetails document) =>
        Results.Json(document, options: null, ContentType, document.Status);

    /// <summary>
    /// Writes a refusal's document straight to the response, for the refusals
    /// that happen before or instead of an endpoint — the fallback below, and
    /// the challenge and the forbid once there is a door (PERSONAL-E2).
    /// </summary>
    public static async Task WriteAsync(HttpContext context, RefusalCode code, string? detail)
    {
        var document = Document(code, detail, context.Request.Path);

        context.Response.StatusCode = document.Status!.Value;
        await context.Response.WriteAsJsonAsync(document, options: null, ContentType, context.RequestAborted);
    }

    private static ProblemDetails Document(Refusal refusal, string? instance = null) =>
        Document(refusal.Code, refusal.Detail, instance, refusal.Extensions);

    /// <summary>
    /// A body or a parameter the framework could not read at all — a closed set
    /// given a word outside it, a number where a string was sent, malformed
    /// JSON, or a field the object does not define. All of them are the
    /// caller's mistake, and each answers as the code that says which mistake
    /// it was, named after the field where the reader gave up.
    /// </summary>
    private static Refusal Unreadable(BadHttpRequestException exception)
    {
        if (exception.InnerException is not JsonException unreadable)
        {
            return Refusal.Validation("body", "The request body is not the JSON object this endpoint takes.");
        }

        var field = FieldOf(unreadable.Path);

        if (field is null)
        {
            return Refusal.Validation("body", "The request body is not the JSON object this endpoint takes.");
        }

        // `UnmappedMemberHandling.Disallow` is what raises this, and the reader
        // says so in the one way it says it. The test that sends an undefined
        // field is what keeps this true: a runtime that changes the sentence
        // turns that test red rather than turning the refusal quietly back into
        // `validation`.
        return unreadable.Message.Contains("could not be mapped", StringComparison.Ordinal)
            ? new Refusal(
                RefusalCode.UnknownField,
                $"{field}: this object does not define that field.",
                new Dictionary<string, object?> { ["field"] = field })
            : Refusal.Validation(
                field,
                "The value is not of the type this field takes; a closed set takes one of its words.");
    }

    /// <summary>The last segment of a JSON path — <c>$.owner.email</c> is <c>email</c>.</summary>
    private static string? FieldOf(string? path) =>
        path is null or "$" ? null : path.Split('.', '[')[^1].TrimEnd(']');

    /// <summary>
    /// What turns a <see cref="Refusal"/> thrown by an act into its document,
    /// and anything else into <c>internal</c> with nothing else in it.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole point: a refusal is something the product
    /// decided and can say out loud, and anything else is a bug whose message,
    /// stack and inner exception are the instance's business. What the caller
    /// gets for one of those is a title and a status; what the operator gets is
    /// the exception, in the log.
    /// </remarks>
    public sealed class Handler(ILogger<Handler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            var document = exception switch
            {
                Refusal refusal => Document(refusal, context.Request.Path),
                BadHttpRequestException bad => Document(Unreadable(bad), context.Request.Path),
                _ => Document(RefusalCode.Internal, detail: null, context.Request.Path),
            };

            if (document.Status == StatusCodes.Status500InternalServerError)
            {
                logger.LogError(
                    exception, "Unhandled exception on {Method} {Path}.", context.Request.Method, context.Request.Path);
            }

            context.Response.StatusCode = document.Status!.Value;
            await context.Response.WriteAsJsonAsync(document, options: null, ContentType, cancellationToken);

            return true;
        }
    }
}
