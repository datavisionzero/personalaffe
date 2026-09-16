using Personalaffe.Domain;
using Personalaffe.Domain.Search;

namespace Personalaffe.UnitTests;

/// <summary>
/// What somebody typed, taken apart: the one place in this product where a
/// caller's text reaches a query language, and the rules that make that safe
/// and useful at once.
/// </summary>
public sealed class NeedleTests
{
    [Fact]
    public void Words_are_lowercased_and_kept_in_order()
    {
        Assert.Equal(["architecture", "decision"], Needle.Of("Architecture Decision").Words);
    }

    [Fact]
    public void What_was_typed_is_kept_as_it_was_typed()
    {
        // The words are for the index; the text is what a client draws back
        // over the field it was typed into.
        Assert.Equal("Architecture Decision", Needle.Of("  Architecture Decision  ").Text);
    }

    [Theory]
    [InlineData("budget-2026.final.pdf")]
    [InlineData("budget 2026 final pdf")]
    [InlineData("budget/2026:final,pdf")]
    public void Punctuation_is_a_boundary_however_it_is_spelled(string typed)
    {
        // The file this is looking for is indexed the same way, which is the
        // whole of why a file name is findable by any word in it.
        Assert.Equal(["budget", "2026", "final", "pdf"], Needle.Of(typed).Words);
    }

    [Fact]
    public void Nothing_that_could_be_an_operator_survives()
    {
        // Everything a tsquery would read as syntax — & | ! ( ) : * ' — is a
        // boundary here, so the store has nothing left to quote.
        var needle = Needle.Of("architecture & !decision | (storage:*) 'quoted'");

        Assert.Equal(["architecture", "decision", "storage", "quoted"], needle.Words);
    }

    [Theory]
    [InlineData("Größe")]
    [InlineData("naïve")]
    [InlineData("συνάντηση")]
    public void Letters_of_every_script_are_letters(string typed)
    {
        Assert.Equal([typed.ToLowerInvariant()], Needle.Of(typed).Words);
    }

    [Fact]
    public void A_single_letter_is_dropped_because_it_is_a_prefix_of_everything()
    {
        Assert.Equal(["page"], Needle.Of("a page").Words);
    }

    [Fact]
    public void A_needle_of_only_single_letters_is_nothing_to_look_for()
    {
        var refusal = Assert.Throws<Refusal>(() => Needle.Of("a b c"));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("... ---")]
    public void There_has_to_be_something_to_look_for(string? typed)
    {
        Assert.Equal(RefusalCode.Validation, Assert.Throws<Refusal>(() => Needle.Of(typed)).Code);
    }

    [Fact]
    public void More_text_than_a_search_is_refused_rather_than_cut()
    {
        var refusal = Assert.Throws<Refusal>(() => Needle.Of(new string('a', Needle.MaxLength + 1)));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Fact]
    public void Past_the_eighth_word_the_rest_are_dropped_and_the_search_still_runs()
    {
        var needle = Needle.Of(string.Join(' ', Enumerable.Range(1, 20).Select(number => $"word{number}")));

        Assert.Equal(Needle.MaxWords, needle.Words.Count);
        Assert.Equal("word1", needle.Words[0]);
    }

    [Fact]
    public void The_field_a_refusal_names_is_the_one_the_caller_sent()
    {
        var refusal = Assert.Throws<Refusal>(() => Needle.Of("", "query"));

        Assert.Contains("query", refusal.Detail, StringComparison.Ordinal);
    }
}
