using System.Globalization;

namespace Personalaffe.Domain.Appearance;

/// <summary>
/// The colours an instance's mark may be drawn in.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A closed set, and not a colour somebody types.</strong>
/// <c>--brand</c> is not only the mark: it is the active settings tab, the link
/// colour, the version string and the soft background behind them, in a light
/// theme and a dark one. Each colour therefore needs three values that hold up
/// in both, and a free hex cannot promise that — it can only promise that an
/// instance can be made unreadable in one click.
/// </para>
/// <para>
/// The words are stored; the <c>oklch</c> triples behind them live in the web
/// application's token layer, which is the only place that knows what a colour
/// looks like (<c>src/web/src/index.css</c>). Nothing here is CSS, and nothing
/// in the database is.
/// </para>
/// <para>
/// <see cref="Violet"/> is first because it is what every instance already is:
/// the values behind it are the ones the product shipped with, so an owner who
/// sets nothing sees no change.
/// </para>
/// </remarks>
public enum MarkColour
{
    /// <summary>What the product has always been.</summary>
    Violet,

    /// <summary>Blue.</summary>
    Blue,

    /// <summary>Teal.</summary>
    Teal,

    /// <summary>Green.</summary>
    Green,

    /// <summary>Amber.</summary>
    Amber,

    /// <summary>Red.</summary>
    Red,

    /// <summary>Pink.</summary>
    Pink,
}

/// <summary>
/// The two shapes an instance's mark may be drawn as.
/// </summary>
/// <remarks>
/// Two, because the mark's job is to be told apart from the two or three other
/// instances beside it at a glance, and shape and colour together do that at
/// any size. A third would be a decision somebody makes; a picture somebody
/// uploads is a different ticket and a relationship with Files and the Trash.
/// </remarks>
public enum MarkShape
{
    /// <summary>A square with rounded corners. What the product has always drawn.</summary>
    Square,

    /// <summary>A circle.</summary>
    Circle,
}

/// <summary>
/// What this instance is called and what its mark looks like
/// (<c>CONTEXT.md</c>, Instance appearance).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One row, and it exists before anybody sets it.</strong> The
/// migration seeds an instance that is unnamed, the way the weather place is
/// seeded nowhere (<see cref="Weather.WeatherPlace"/>): reading it is a read
/// rather than a read that might have to insert, and it has a version to be
/// written against from the first moment.
/// </para>
/// <para>
/// <strong>It says nothing about the owner.</strong> The title is a word the
/// owner wrote about their installation and the colour is one of seven; who the
/// owner is, what address they sign in with and when they were set up are not
/// here and are not outside the door. That is what lets this be read without a
/// credential — which it has to be, because the sign-in screen and the browser
/// tab are exactly where "which instance is this" is worth answering, and both
/// are drawn before anybody has signed in.
/// </para>
/// <para>
/// <strong>The letters in the mark are not here.</strong> They are derived from
/// the title by whoever draws it, so there is nothing to keep in step with a
/// rename and nothing stored that a rename could contradict.
/// </para>
/// </remarks>
public sealed class InstanceAppearance
{
    /// <summary>
    /// How long a title may be. One line in a sidebar header and in a browser
    /// tab, both of which are narrow; a name that does not fit either is not
    /// doing the one job this setting has.
    /// </summary>
    public const int TitleMaxLength = 40;

    private InstanceAppearance()
    {
    }

    /// <summary>
    /// Always true, and the key of the row: one appearance, held to one by the
    /// schema, the way the owner is (<see cref="Owner.Singleton"/>).
    /// </summary>
    public bool Singleton { get; private init; }

    /// <summary>
    /// What the owner calls this instance, or nothing — which means the product
    /// name, and is what an instance nobody has touched has.
    /// </summary>
    /// <remarks>
    /// It is text and only ever text. Nothing renders it as Markdown or as
    /// HTML, and nothing assembles it into a <c>data:</c> URI: a title
    /// containing <c>&lt;script&gt;</c> is a title containing
    /// <c>&lt;script&gt;</c>, on every screen and in the console.
    /// </remarks>
    public string? Title { get; private set; }

    /// <summary>The colour the mark is drawn in.</summary>
    public MarkColour Colour { get; private set; }

    /// <summary>The shape the mark is drawn as.</summary>
    public MarkShape Shape { get; private set; }

    /// <summary>When it was last set, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>Whether the owner has given this instance a name of its own.</summary>
    public bool Named => Title is not null;

    /// <summary>
    /// The appearance a fresh instance has: no name, and the mark the product
    /// has always drawn.
    /// </summary>
    /// <remarks>
    /// Violet and a rounded square, because those are the values already on
    /// every screen. An instance that is upgraded into this feature and never
    /// touched afterwards looks exactly as it did the day before.
    /// </remarks>
    public static InstanceAppearance Unnamed(DateTimeOffset now) => new()
    {
        Singleton = true,
        Title = null,
        Colour = MarkColour.Violet,
        Shape = MarkShape.Square,
        UpdatedAt = now,
    };

    /// <summary>
    /// Sets all three, and says whether that changed anything — setting an
    /// appearance to what it already is is not a write.
    /// </summary>
    /// <exception cref="Refusal">
    /// <c>validation</c>: a title longer than a line, or a colour or a shape
    /// that is not one of the ones there are.
    /// </exception>
    public bool Set(string? title, MarkColour colour, MarkShape shape, DateTimeOffset now)
    {
        var named = Accepted(title);

        Known(colour);
        Known(shape);

        if (named == Title && colour == Colour && shape == Shape)
        {
            return false;
        }

        Title = named;
        Colour = colour;
        Shape = shape;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// The title as it is stored: trimmed, and nothing at all where what was
    /// sent was nothing but space.
    /// </summary>
    /// <remarks>
    /// Whitespace is stored as none rather than refused, because an owner
    /// clearing the field means "go back to the product name" and there is no
    /// reading of a box holding three spaces that is worth an error message.
    /// </remarks>
    private static string? Accepted(string? title)
    {
        var named = (title ?? string.Empty).Trim();

        if (named.Length == 0)
        {
            return null;
        }

        if (named.Length > TitleMaxLength)
        {
            throw Refusal.Validation(
                "title",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A title is at most {TitleMaxLength} characters, and this one is {named.Length}."));
        }

        // One line. A sidebar header and a browser tab are both one, and a
        // title carrying a newline would be a title that draws differently in
        // each of the four places it appears.
        if (named.Any(character => char.IsControl(character)))
        {
            throw Refusal.Validation(
                "title", "A title is one line of plain text, with no line breaks in it.");
        }

        return named;
    }

    private static void Known(MarkColour colour)
    {
        if (!Enum.IsDefined(colour))
        {
            throw Refusal.Validation(
                "colour",
                "The colours are "
                + string.Join(", ", Enum.GetValues<MarkColour>().Select(Wire.Spell))
                + ".");
        }
    }

    private static void Known(MarkShape shape)
    {
        if (!Enum.IsDefined(shape))
        {
            throw Refusal.Validation(
                "shape",
                "The shapes are "
                + string.Join(", ", Enum.GetValues<MarkShape>().Select(Wire.Spell))
                + ".");
        }
    }
}

/// <summary>
/// How a colour and a shape are spelled everywhere outside this assembly: the
/// lower-case word, which is what the contract carries and what the column
/// holds.
/// </summary>
internal static class Wire
{
    public static string Spell<T>(T value)
        where T : struct, Enum =>
        value.ToString()!.ToLowerInvariant();
}
