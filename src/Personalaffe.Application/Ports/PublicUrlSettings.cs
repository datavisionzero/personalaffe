namespace Personalaffe.Application.Ports;

/// <summary>
/// The address this instance is reached at, when the operator has said what it
/// is.
/// </summary>
/// <remarks>
/// <para>
/// Optional, and the product works without it: what it buys is a stricter CSRF
/// check. With it, a browser write's <c>Origin</c> is compared to this address
/// whole. Without it, the scheme is left out of the comparison — behind a proxy
/// that terminates TLS, the request reaches the instance as <c>http</c> unless
/// the proxy is trusted to say otherwise, and comparing that against the
/// browser's <c>https</c> would refuse every write on an installation whose
/// operator had not set the variable.
/// </para>
/// <para>
/// It is never used to build a link. An instance that is reached at two
/// addresses is reached at two addresses, and a value here would make one of
/// them wrong.
/// </para>
/// </remarks>
public sealed record PublicUrlSettings(Uri? Url)
{
    public const string Variable = "PERSONALAFFE_PUBLIC_URL";

    public static PublicUrlSettings FromVariables(string? value)
    {
        var written = value?.Trim();

        if (string.IsNullOrEmpty(written))
        {
            return new PublicUrlSettings(Url: null);
        }

        return Uri.TryCreate(written, UriKind.Absolute, out var url)
            && url.Scheme is "http" or "https"
                ? new PublicUrlSettings(url)
                : throw new ArgumentException(
                    $"{Variable} is {written}, which is not an address this instance can be reached at: "
                    + "scheme and host, like https://workspace.example.com.");
    }
}
