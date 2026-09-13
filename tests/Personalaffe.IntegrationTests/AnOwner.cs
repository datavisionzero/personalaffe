using System.Net;
using System.Net.Http.Json;
using Personalaffe.Api.Http;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The two lines every test behind the door starts with: claim the instance,
/// and sign in.
/// </summary>
/// <remarks>
/// The client it hands back is a browser's: it keeps the session cookie and it
/// sends what a browser write has to send (<see cref="CsrfProtection"/>), so
/// that a test about something else is not also a test about the guard.
/// </remarks>
internal static class AnOwner
{
    public const string Address = "owner@example.com";
    public const string Secret = "correct horse battery staple";

    /// <summary>What the browser this stands for calls itself.</summary>
    public const string Browser = "a-browser-in-a-test/1.0";

    /// <summary>Claims the instance. The instance has an owner afterwards and nobody is signed in.</summary>
    public static async Task SetUpAsync(AnInstance instance, CancellationToken cancellationToken)
    {
        using var client = instance.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/setup", new { email = Address, password = Secret }, cancellationToken);

        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"Setting the instance up answered {response.StatusCode}.");
        }
    }

    /// <summary>A client that has signed in, and behaves like the browser it stands for.</summary>
    public static async Task<HttpClient> SignedInAsync(
        AnInstance instance, CancellationToken cancellationToken)
    {
        await SetUpAsync(instance, cancellationToken);

        var client = AsABrowser(instance);

        using var response = await client.PostAsJsonAsync(
            "/api/session", new { email = Address, password = Secret }, cancellationToken);

        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            client.Dispose();
            throw new InvalidOperationException($"Signing in answered {response.StatusCode}.");
        }

        return client;
    }

    /// <summary>A client that keeps cookies and proves its writes, without having signed in.</summary>
    public static HttpClient AsABrowser(AnInstance instance)
    {
        var client = instance.CreateClient();

        client.DefaultRequestHeaders.Add(CsrfProtection.Header, "1");
        client.DefaultRequestHeaders.Add("User-Agent", Browser);
        client.DefaultRequestHeaders.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        return client;
    }
}
