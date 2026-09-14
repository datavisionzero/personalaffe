using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.UnitTests;

/// <summary>
/// The rules of a stored file and of a folder: what changes the version, what a
/// media type is, and what a file's address is made of.
/// </summary>
public sealed class StoredFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    [Fact]
    public void A_file_keeps_the_id_its_bytes_were_written_under()
    {
        var id = Guid.CreateVersion7(Now);
        var file = StoredFile.Stored(id, "plan.md", folder: null, 12, "text/markdown", Now);

        // The bytes go first and have to be written somewhere, so the id exists
        // before the row does. It is also the reference Knowledge links to.
        Assert.Equal(id, file.Id);
        Assert.Equal(StorageAddress.Of(id), file.Address);
    }

    [Fact]
    public void Renaming_and_moving_leave_the_address_alone()
    {
        var file = StoredFile.Stored(Guid.CreateVersion7(Now), "plan.md", null, 12, "text/markdown", Now);
        var address = file.Address;
        var folder = Guid.CreateVersion7(Now);

        file.Change("der Plan.md", folder, Later);

        Assert.Equal(address, file.Address);
        Assert.Equal("der Plan.md", file.Name);
        Assert.Equal(folder, file.FolderId);
        Assert.Equal(Later, file.UpdatedAt);
    }

    [Fact]
    public void A_change_to_what_is_already_stored_is_not_a_change()
    {
        var file = StoredFile.Stored(Guid.CreateVersion7(Now), "plan.md", null, 12, "text/markdown", Now);

        Assert.False(file.Change("plan.md", null, Later));
        Assert.Equal(Now, file.UpdatedAt);
    }

    [Fact]
    public void Replacing_the_bytes_changes_the_size_and_nothing_about_where_it_is()
    {
        var file = StoredFile.Stored(Guid.CreateVersion7(Now), "plan.md", null, 12, "text/markdown", Now);

        file.Replace(4096, "application/pdf", Later);

        Assert.Equal(4096, file.Size);
        Assert.Equal("application/pdf", file.MediaType);
        Assert.Equal("plan.md", file.Name);
        Assert.Equal(Later, file.UpdatedAt);
    }

    [Theory]
    [InlineData("text/plain; charset=utf-8", "text/plain")]
    [InlineData("IMAGE/PNG", "image/png")]
    [InlineData("  application/pdf  ", "application/pdf")]
    public void A_media_type_is_stored_without_its_parameters_and_in_lower_case(
        string declared, string stored)
    {
        Assert.Equal(stored, StoredFile.Stored(Guid.NewGuid(), "a", null, 1, declared, Now).MediaType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("text/")]
    [InlineData("/plain")]
    [InlineData("text/plain\r\nX-Injected: yes")]
    [InlineData("text/\"plain\"")]
    public void Anything_that_is_not_a_media_type_is_some_bytes(string? declared)
    {
        // Not a refusal: a caller who sent nothing usable has still sent bytes
        // worth keeping. What must not happen is that the string ends up in a
        // response header meaning something the caller chose.
        Assert.Equal(
            StoredFile.UnknownMediaType,
            StoredFile.Stored(Guid.NewGuid(), "a", null, 1, declared, Now).MediaType);
    }

    [Fact]
    public void A_file_starts_out_of_the_trash_and_says_who_put_it_in()
    {
        var file = StoredFile.Stored(Guid.CreateVersion7(Now), "plan.md", null, 12, null, Now);
        var (access, _) = AgentAccess.Grant("the archivist", Permissions.Full, Now);
        var agent = Caller.Agent(access);

        Assert.False(file.IsDeleted());

        file.Delete(agent, Later);

        Assert.True(file.IsDeleted());
        Assert.Equal(Later, file.DeletedAt);
        Assert.Equal("the archivist", file.DeletedBy?.Name);
        Assert.Equal(access.Id, file.DeletedBy?.Id);
    }

    [Fact]
    public void A_folder_is_made_at_the_top_or_inside_another_one()
    {
        var top = Folder.Make("Reisen", parent: null, Now);
        var inside = Folder.Make("2026", top.Id, Now);

        Assert.Null(top.ParentId);
        Assert.Equal(top.Id, inside.ParentId);
        Assert.Equal(ContentVersion.Of(Now), inside.Version);
    }

    [Fact]
    public void A_folder_that_is_neither_renamed_nor_moved_does_not_move_its_version()
    {
        var folder = Folder.Make("Reisen", null, Now);

        Assert.False(folder.Change("Reisen", null, Later));
        Assert.Equal(Now, folder.UpdatedAt);

        Assert.True(folder.Change("Reisen", Guid.NewGuid(), Later));
        Assert.Equal(Later, folder.UpdatedAt);
    }

    [Fact]
    public void A_name_that_is_not_a_name_is_refused_wherever_it_arrives()
    {
        Assert.Throws<Refusal>(() => Folder.Make("../elsewhere", null, Now));
        Assert.Throws<Refusal>(() => StoredFile.Stored(Guid.NewGuid(), "a/b", null, 1, null, Now));
        Assert.Throws<Refusal>(() => Folder.Make("Reisen", null, Now).Change("..", null, Later));
    }

    [Fact]
    public void A_file_is_not_a_negative_number_of_bytes()
    {
        var refusal = Assert.Throws<Refusal>(
            () => StoredFile.Stored(Guid.NewGuid(), "a", null, -1, null, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }
}
