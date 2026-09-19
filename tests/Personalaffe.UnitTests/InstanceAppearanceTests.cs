using Personalaffe.Domain;
using Personalaffe.Domain.Appearance;

namespace Personalaffe.UnitTests;

/// <summary>
/// What an instance is called and what its mark looks like: a title that is
/// text, a colour from a closed set, and a default that is what the product has
/// always been.
/// </summary>
public sealed class InstanceAppearanceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void A_fresh_instance_is_unnamed_and_looks_exactly_as_it_always_has()
    {
        var appearance = InstanceAppearance.Unnamed(Now);

        // An instance upgraded into this feature and never touched afterwards
        // looks the same the day after as the day before.
        Assert.Null(appearance.Title);
        Assert.False(appearance.Named);
        Assert.Equal(MarkColour.Violet, appearance.Colour);
        Assert.Equal(MarkShape.Square, appearance.Shape);
    }

    [Fact]
    public void Naming_it_is_a_write_and_carries_a_new_version()
    {
        var appearance = InstanceAppearance.Unnamed(Now);
        var before = appearance.Version;

        Assert.True(appearance.Set("Haus", MarkColour.Teal, MarkShape.Circle, Now.AddMinutes(1)));

        Assert.Equal("Haus", appearance.Title);
        Assert.True(appearance.Named);
        Assert.Equal(MarkColour.Teal, appearance.Colour);
        Assert.Equal(MarkShape.Circle, appearance.Shape);
        Assert.False(appearance.Version.Matches(before));
    }

    [Fact]
    public void Setting_it_to_what_it_already_is_changes_nothing()
    {
        var appearance = InstanceAppearance.Unnamed(Now);
        appearance.Set("Haus", MarkColour.Teal, MarkShape.Circle, Now.AddMinutes(1));

        var version = appearance.Version;

        Assert.False(appearance.Set("Haus", MarkColour.Teal, MarkShape.Circle, Now.AddMinutes(2)));
        Assert.True(appearance.Version.Matches(version));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \t")]
    public void A_title_that_is_only_space_is_stored_as_none(string title)
    {
        var appearance = InstanceAppearance.Unnamed(Now);
        appearance.Set("Haus", MarkColour.Violet, MarkShape.Square, Now.AddMinutes(1));

        // Clearing the field means "go back to the product name". There is no
        // reading of a box holding three spaces that is worth an error message.
        Assert.True(appearance.Set(title, MarkColour.Violet, MarkShape.Square, Now.AddMinutes(2)));

        Assert.Null(appearance.Title);
        Assert.False(appearance.Named);
    }

    [Fact]
    public void A_title_is_trimmed()
    {
        var appearance = InstanceAppearance.Unnamed(Now);

        appearance.Set("  Haus  ", MarkColour.Violet, MarkShape.Square, Now.AddMinutes(1));

        Assert.Equal("Haus", appearance.Title);
    }

    [Fact]
    public void A_title_of_forty_characters_is_accepted_and_forty_one_is_refused()
    {
        var appearance = InstanceAppearance.Unnamed(Now);

        appearance.Set(new string('a', 40), MarkColour.Violet, MarkShape.Square, Now.AddMinutes(1));
        Assert.Equal(new string('a', 40), appearance.Title);

        var refusal = Assert.Throws<Refusal>(() =>
            appearance.Set(new string('a', 41), MarkColour.Violet, MarkShape.Square, Now.AddMinutes(2)));

        Assert.Equal(RefusalCode.Validation, refusal.Code);

        // And the refusal did not half-apply: what is there is the title that
        // was accepted.
        Assert.Equal(new string('a', 40), appearance.Title);
    }

    [Fact]
    public void A_title_is_one_line()
    {
        var appearance = InstanceAppearance.Unnamed(Now);

        var refusal = Assert.Throws<Refusal>(() =>
            appearance.Set("Haus\nund Hof", MarkColour.Violet, MarkShape.Square, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Fact]
    public void A_title_is_text_and_is_kept_as_it_was_written()
    {
        var appearance = InstanceAppearance.Unnamed(Now);

        // Nothing here escapes, strips or renders anything: a title is text,
        // and whoever writes it out is the one that must write it out
        // literally.
        appearance.Set("<script>alert(1)</script>", MarkColour.Violet, MarkShape.Square, Now);

        Assert.Equal("<script>alert(1)</script>", appearance.Title);
    }

    [Fact]
    public void A_colour_that_is_not_one_of_them_is_refused()
    {
        var appearance = InstanceAppearance.Unnamed(Now);

        var refusal = Assert.Throws<Refusal>(() =>
            appearance.Set("Haus", (MarkColour)99, MarkShape.Square, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Null(appearance.Title);
    }

    [Fact]
    public void A_shape_that_is_not_one_of_them_is_refused()
    {
        var appearance = InstanceAppearance.Unnamed(Now);

        var refusal = Assert.Throws<Refusal>(() =>
            appearance.Set("Haus", MarkColour.Violet, (MarkShape)99, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Null(appearance.Title);
    }

    [Fact]
    public void Violet_is_the_first_colour_and_a_square_the_first_shape()
    {
        // The defaults are the values the product shipped with, and they are
        // first so that a client reading the contract's enum sees them first.
        Assert.Equal(MarkColour.Violet, Enum.GetValues<MarkColour>()[0]);
        Assert.Equal(MarkShape.Square, Enum.GetValues<MarkShape>()[0]);
    }
}
