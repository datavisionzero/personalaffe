namespace Personalaffe.Application.Ports;

/// <summary>
/// Where the log goes and how much of it there is. One sink — the console,
/// structured — because personalaffe runs on one machine for one person and
/// must not depend on another running affe product for its log
/// (<c>docs/codebase.md</c>).
/// </summary>
/// <remarks>
/// A record read from the environment rather than a value read where it is
/// used: a level the instance will not accept has to stop the start with the
/// one line that names the variable, and that only works if there is one place
/// that reads it.
/// </remarks>
public sealed record LogSettings(string Level)
{
    public const string LevelVariable = "PERSONALAFFE_LOG_LEVEL";

    public const string DefaultLevel = "Information";

    /// <summary>
    /// Serilog's own set, spelled the way Serilog spells it. Named here so
    /// that the refusal below can list them, rather than telling an operator
    /// that their value was wrong without saying what a right one looks like.
    /// </summary>
    public static readonly IReadOnlyList<string> Levels =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    /// <summary>
    /// Reads the settings, or throws the <see cref="ArgumentException"/> that
    /// stops the start.
    /// </summary>
    public static LogSettings FromVariables(string? level)
    {
        var chosen = (level ?? string.Empty).Trim();
        if (chosen.Length == 0)
        {
            return new LogSettings(DefaultLevel);
        }

        var match = Levels.FirstOrDefault(
            known => string.Equals(known, chosen, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? throw new ArgumentException(
                $"{LevelVariable} is \"{chosen}\", which is not a level. It is one of: {string.Join(", ", Levels)}.")
            : new LogSettings(match);
    }
}
