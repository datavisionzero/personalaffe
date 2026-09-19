using Personalaffe.Application.Acts.Appearance;
using Personalaffe.Domain.Appearance;

namespace Personalaffe.Api.Http;

/// <summary>
/// What this instance is called and what its mark looks like.
/// </summary>
/// <param name="Title">
/// The owner's own name for this installation, or nothing — which means the
/// product name. It is text: whatever reads it writes it out literally and
/// never as Markdown, as HTML or into a <c>data:</c> URI.
/// </param>
public sealed record AppearanceResponse(
    string? Title, MarkColour Colour, MarkShape Shape, DateTimeOffset UpdatedAt)
{
    public static AppearanceResponse Of(InstanceAppearance appearance) => new(
        appearance.Title, appearance.Colour, appearance.Shape, appearance.UpdatedAt);
}

/// <summary>What the owner is setting. All three, every time.</summary>
public sealed record SetAppearanceRequest(
    string? Title, MarkColour Colour = MarkColour.Violet, MarkShape Shape = MarkShape.Square);

/// <summary>
/// The instance's appearance (<c>docs/api.md</c>, The appearance): the one
/// setting in this product whose whole purpose is recognition.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The read is outside the door and the write is the owner's
/// alone.</strong> The read has to be outside it: the sign-in screen and the
/// browser tab are where "which instance am I typing into" is worth answering,
/// and both are drawn before anybody has signed in. The write is not, because
/// the answer is public — an agent that could set it could write a word of its
/// choosing onto the sign-in screen of somebody else's instance.
/// </para>
/// <para>
/// It is the sixth operation outside the door, and the first one added after
/// PERSONAL-E2 closed that list. What it carries is held to the same rule as the other five: a word
/// the owner wrote about their installation and one of seven colours, and
/// nothing about the owner, their credential or their host.
/// </para>
/// </remarks>
public static class AppearanceEndpoints
{
    public static IEndpointRouteBuilder MapAppearance(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/appearance", async (
                HttpResponse response, ReadTheAppearance act, CancellationToken cancellationToken) =>
            {
                var appearance = await act.ExecuteAsync(cancellationToken);

                EntityTags.Write(response, appearance.Version);

                return Results.Ok(AppearanceResponse.Of(appearance));
            })
            .AllowAnonymous()
            .WithName("ReadAppearance")
            .WithSummary("What this instance is called and what its mark looks like.")
            .WithDescription(
                "Outside the door, because the sign-in screen and the browser tab are drawn before "
                + "anybody has signed in. `title` is nothing on an instance nobody has named, which "
                + "means the product name. It is plain text and is never Markdown or HTML.")
            .Produces<AppearanceResponse>();

        endpoints.MapPut("/appearance", async (
                SetAppearanceRequest body,
                HttpRequest request,
                HttpResponse response,
                SetTheAppearance act,
                CancellationToken cancellationToken) =>
            {
                var appearance = await act.ExecuteAsync(
                    body.Title,
                    body.Colour,
                    body.Shape,
                    EntityTags.Required(request),
                    cancellationToken);

                EntityTags.Write(response, appearance.Version);

                return Results.Ok(AppearanceResponse.Of(appearance));
            })
            .WithName("SetAppearance")
            .WithSummary("Name this instance and choose its mark. The owner's alone.")
            .WithDescription(
                "All three, every time. An empty or whitespace title is stored as none and the "
                + "instance goes back to the product name. Whoever can reach this instance can read "
                + "what is set here.")
            .Produces<AppearanceResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Guarded();

        return endpoints;
    }
}
