namespace Personalaffe.Application.Ports;

/// <summary>
/// Which PostgreSQL the instance is, and how that string may be spoken about.
/// </summary>
/// <remarks>
/// <para>
/// The connection string is the one secret the foundation already carries, and
/// it is the one an unhandled exception is most likely to put in a log: it
/// appears in Npgsql's own messages, in a health answer somebody adds without
/// thinking, and in whatever a stack trace formats. <see cref="Redacted"/> is
/// what everything that has to name the database says instead, and the instance
/// never writes the raw value anywhere.
/// </para>
/// <para>
/// Reading it here rather than where it is used is what lets a missing or
/// malformed value stop the start with one line naming the variable, instead of
/// a stack trace out of the first request.
/// </para>
/// </remarks>
public sealed record DatabaseSettings(string ConnectionString)
{
    /// <summary>
    /// The connection string's name in configuration — as an environment
    /// variable, <c>ConnectionStrings__Postgres</c>.
    /// </summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>What an operator sets, spelled the way they set it.</summary>
    public const string Variable = "ConnectionStrings__" + ConnectionStringName;

    /// <summary>
    /// The keywords whose value is a credential. Npgsql's own spellings, all of
    /// them, because a keyword this does not know is a password printed in
    /// full.
    /// </summary>
    private static readonly string[] Secret =
        ["password", "pwd", "sslpassword", "ssl password", "ssl key password"];

    private const string Mask = "***";

    /// <summary>
    /// The connection string with every credential replaced by
    /// <c>***</c> — what is written to a log, an error message or a health
    /// answer.
    /// </summary>
    public string Redacted { get; } = Redact(ConnectionString);

    /// <summary>
    /// Reads the settings, or throws the <see cref="ArgumentException"/> that
    /// stops the start. The message names the variable and says what is wrong
    /// with it, and it never contains the value.
    /// </summary>
    public static DatabaseSettings FromConnectionString(string? connectionString)
    {
        var value = (connectionString ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            throw new ArgumentException(
                $"{Variable} is not set. It is the PostgreSQL this instance keeps its data in, "
                + "for example Host=db;Port=5432;Database=personalaffe;Username=personalaffe;Password=…");
        }

        if (!Pairs(value).Any(pair => IsHost(pair.Key)))
        {
            throw new ArgumentException(
                $"{Variable} names no host. A connection string is keyword=value pairs separated by "
                + "semicolons, and one of them is Host= (or Server=).");
        }

        return new DatabaseSettings(value);
    }

    private static bool IsHost(string keyword) =>
        keyword is "host" or "server" or "data source" or "datasource";

    private static string Redact(string connectionString) =>
        string.Join(
            ';',
            connectionString
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(part =>
                {
                    var separator = part.IndexOf('=');
                    if (separator < 0)
                    {
                        return part;
                    }

                    var keyword = Normalized(part[..separator]);
                    return Secret.Contains(keyword)
                        ? $"{part[..separator].Trim()}={Mask}"
                        : part.Trim();
                }));

    private static IEnumerable<(string Key, string Value)> Pairs(string connectionString) =>
        connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.IndexOf('=') is var separator and >= 0
                ? (Normalized(part[..separator]), part[(separator + 1)..].Trim())
                : (Normalized(part), string.Empty));

    /// <summary>
    /// Npgsql reads keywords without regard to case or the spaces and
    /// underscores inside them: <c>User ID</c>, <c>user_id</c> and
    /// <c>userid</c> are one keyword, and so are the three spellings of a
    /// password.
    /// </summary>
    private static string Normalized(string keyword) =>
        keyword.Trim().Replace("_", " ", StringComparison.Ordinal).ToLowerInvariant();
}
