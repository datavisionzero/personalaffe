using System.Text;
using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.UnitTests;

/// <summary>
/// What a file or a folder may be called: the rules that decide whether the
/// owner can navigate their own tree.
/// </summary>
public sealed class FileNameTests
{
    [Fact]
    public void A_name_is_kept_as_it_was_typed_apart_from_the_space_around_it()
    {
        Assert.Equal("Reisekosten 2026.pdf", FileName.Accepted("  Reisekosten 2026.pdf  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Nothing_and_whitespace_alone_are_not_names(string? asked)
    {
        var refusal = Assert.Throws<Refusal>(() => FileName.Accepted(asked));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains("name", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_limit_is_bytes_of_utf8_and_not_characters()
    {
        // Every one of these is three bytes, so 85 of them are 255 — the limit
        // exactly — and 86 are over it while still being 86 characters. A limit
        // counted in characters would take both.
        var justFits = string.Concat(Enumerable.Repeat("あ", FileName.MaxBytes / 3));
        var justOver = justFits + "あ";

        Assert.Equal(FileName.MaxBytes, Encoding.UTF8.GetByteCount(justFits));
        Assert.Equal(justFits, FileName.Accepted(justFits));

        var refusal = Assert.Throws<Refusal>(() => FileName.Accepted(justOver));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains(FileName.MaxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), refusal.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void The_two_names_every_filesystem_has_already_taken_are_refused(string asked)
    {
        Assert.Equal(RefusalCode.Validation, Assert.Throws<Refusal>(() => FileName.Accepted(asked)).Code);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("notes/plan.md")]
    [InlineData("C:\\Windows\\system32")]
    [InlineData("a\\b")]
    public void A_path_is_not_a_name(string asked)
    {
        var refusal = Assert.Throws<Refusal>(() => FileName.Accepted(asked));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains("path", refusal.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("two\nlines.txt")]
    [InlineData("tab\there.txt")]
    [InlineData("nul\0inside.txt")]
    [InlineData("carriage\rreturn")]
    public void A_control_character_is_not_something_a_name_has_in_it(string asked)
    {
        Assert.Equal(RefusalCode.Validation, Assert.Throws<Refusal>(() => FileName.Accepted(asked)).Code);
    }

    [Fact]
    public void Everything_else_a_person_types_is_a_name()
    {
        // Colons, quotation marks, asterisks and question marks are refused by
        // Windows and taken by every other filesystem. Nothing here writes a
        // name to a disk (StorageAddress), so refusing them would be this
        // product borrowing somebody else's limitation.
        foreach (var name in new[] { "Q1: the plan", "\"quoted\"", "why?.txt", "*star*", "a|b", "日本語.md" })
        {
            Assert.Equal(name, FileName.Accepted(name));
        }
    }

    [Fact]
    public void Two_names_that_differ_only_in_case_are_one_name()
    {
        Assert.True(FileName.Same("Notes", "notes"));
        Assert.True(FileName.Same("PLAN.MD", "plan.md"));
        Assert.False(FileName.Same("notes", "notes 2"));
    }

    [Fact]
    public void The_field_a_refusal_names_is_the_one_the_caller_sent()
    {
        var refusal = Assert.Throws<Refusal>(() => FileName.Accepted("", "restore_as"));

        Assert.Contains("restore_as", refusal.Detail, StringComparison.Ordinal);
    }
}
