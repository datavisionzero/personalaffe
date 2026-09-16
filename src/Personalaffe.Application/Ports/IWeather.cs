using Personalaffe.Domain.Weather;

namespace Personalaffe.Application.Ports;

/// <summary>
/// Somewhere a geocoder thinks the owner might have meant.
/// </summary>
/// <param name="Region">
/// What it is in — a state, a county — where the geocoder said, so that the
/// fourth Springfield can be told from the third.
/// </param>
public sealed record SomewhereCalled(
    string Name, string? Region, string? Country, double Latitude, double Longitude);

/// <summary>
/// The one thing in this product that asks something outside it a question.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It answers with nothing rather than throwing.</strong> A provider on
/// the other side of the internet is down sometimes, slow sometimes and gone
/// eventually, and none of that is an error in this workspace: the tile says it
/// does not know and the rest of the home page is untouched. A port that threw
/// would make every caller responsible for remembering that, and the first one
/// to forget would be a home page that fails to load because it is raining
/// somewhere.
/// </para>
/// <para>
/// <strong>Freshness is behind it, not in front of it.</strong> Whoever
/// implements this caches; the acts ask whenever they are asked. That keeps
/// "how often may a provider be troubled" one decision in one place rather
/// than a rule every caller has to know.
/// </para>
/// </remarks>
public interface IWeather
{
    /// <summary>
    /// Whether this instance will ask anybody at all. An operator can switch
    /// the outbound request off, and then this is <c>false</c> and nothing here
    /// ever opens a socket.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Who is owed the credit for what this answers, in the one sentence a tile
    /// and a console both print. A free provider is paid in attribution, and
    /// the place it is said is beside the number it produced.
    /// </summary>
    string Attribution { get; }

    /// <summary>
    /// What it is doing at <paramref name="place"/>, or nothing — because the
    /// place is nowhere, because the outbound request is switched off, or
    /// because the provider did not answer.
    /// </summary>
    Task<Reading?> ReadAsync(WeatherPlace place, CancellationToken cancellationToken);

    /// <summary>
    /// The places a geocoder thinks <paramref name="query"/> names, best first,
    /// or nothing at all where it did not answer.
    /// </summary>
    Task<IReadOnlyList<SomewhereCalled>> LookUpAsync(
        string query, CancellationToken cancellationToken);
}
