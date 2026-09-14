using Personalaffe.Domain.Files;

namespace Personalaffe.UnitTests;

/// <summary>
/// Where a file's bytes are, and the property the whole application's safety
/// rests on: the address comes from an id and never from a name.
/// </summary>
public sealed class StorageAddressTests
{
    [Fact]
    public void An_address_is_the_id_fanned_out_two_levels()
    {
        var id = Guid.Parse("0199f0c4-1234-7abc-8def-0123456789ab");

        Assert.Equal("files/01/99/0199f0c412347abc8def0123456789ab", StorageAddress.Of(id));
    }

    [Fact]
    public void No_id_can_produce_an_address_that_leaves_the_storage_root()
    {
        // The argument is not that these ids are safe; it is that an address is
        // thirty-two hexadecimal characters and three slashes whatever the id
        // is, so there is no input to this that a path could be smuggled
        // through. A thousand random ones say so out loud.
        foreach (var id in Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).Append(Guid.Empty))
        {
            var address = StorageAddress.Of(id);

            Assert.StartsWith("files/", address, StringComparison.Ordinal);
            Assert.False(address.Contains("..", StringComparison.Ordinal));
            Assert.False(address.Contains('\\'));
            Assert.Equal(4, address.Split('/').Length);
            Assert.Equal(id, StorageAddress.IdAt(address));
        }
    }

    [Fact]
    public void An_arriving_upload_is_beside_the_stored_files_and_not_among_them()
    {
        var id = Guid.NewGuid();

        Assert.StartsWith($"{StorageAddress.Incoming}/", StorageAddress.Arriving(id), StringComparison.Ordinal);
        Assert.EndsWith(".part", StorageAddress.Arriving(id), StringComparison.Ordinal);

        // The tidy-up tells one from the other by where it is, without reading a
        // row: what is in `incoming` is by definition unfinished.
        Assert.Null(StorageAddress.IdAt(StorageAddress.Arriving(id)));
    }

    [Theory]
    [InlineData("files/01/99/not-a-guid")]
    [InlineData("files/0199f0c412347abc8def0123456789ab")]
    [InlineData("files/zz/99/0199f0c412347abc8def0123456789ab")]
    [InlineData("somewhere/else/entirely/0199f0c412347abc8def0123456789ab")]
    [InlineData("")]
    public void Anything_this_product_did_not_write_is_not_one_of_ours(string address)
    {
        // What the tidy-up asks before it deletes something. A name it does not
        // recognise is an operator's own file, and a sweep that removed what it
        // did not recognise would eventually remove something that mattered.
        Assert.Null(StorageAddress.IdAt(address));
    }
}
