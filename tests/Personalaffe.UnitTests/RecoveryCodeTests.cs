using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>The codes on the owner's piece of paper.</summary>
public sealed class RecoveryCodeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_set_is_ten_codes_and_ten_pieces_of_text()
    {
        var owner = Guid.CreateVersion7();
        var (codes, text) = RecoveryCode.Issue(owner, Noon);

        Assert.Equal(RecoveryCode.Count, codes.Count);
        Assert.Equal(RecoveryCode.Count, text.Count);
        Assert.Equal(RecoveryCode.Count, text.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Equal(owner, code.OwnerId));
        Assert.All(codes, code => Assert.False(code.Used));
    }

    [Fact]
    public void A_code_is_readable_off_paper_and_hashed_from_what_is_readable()
    {
        var (codes, text) = RecoveryCode.Issue(Guid.CreateVersion7(), Noon);

        var written = text[0];

        Assert.Matches("^[A-Z0-9]{5}-[A-Z0-9]{5}$", written);

        // No I, L, O or U, and no digits that look like them: this is typed off
        // paper, months later, probably in a hurry.
        Assert.DoesNotContain(written, character => character is 'I' or 'L' or 'O' or 'U' or '0' or '1');

        Assert.Equal(codes[0].CodeHash, RecoveryCode.Hash(written));
    }

    [Theory]
    [InlineData("abcde-fghjk")]
    [InlineData("ABCDEFGHJK")]
    [InlineData(" abcde fghjk ")]
    public void How_it_was_typed_is_not_part_of_it(string typed) =>
        Assert.Equal(RecoveryCode.Hash("ABCDE-FGHJK"), RecoveryCode.Hash(typed));

    [Fact]
    public void Spending_one_keeps_the_moment_it_was_spent()
    {
        var (codes, _) = RecoveryCode.Issue(Guid.CreateVersion7(), Noon);

        codes[0].Use(Noon.AddDays(1));
        codes[0].Use(Noon.AddDays(2));

        Assert.True(codes[0].Used);
        Assert.Equal(Noon.AddDays(1), codes[0].UsedAt);
    }
}
