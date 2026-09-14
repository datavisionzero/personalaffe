namespace Personalaffe.Domain.Files;

/// <summary>
/// A stored personal file with a name and a place in the folder hierarchy
/// (<c>CONTEXT.md</c>, File).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The word is <em>file</em>, and the type carries a prefix for the
/// reason <see cref="WorkspaceApplication"/> does</strong>: it is a fact about
/// C# and not about the product. <c>System.IO.File</c> is in scope in every
/// file that touches a byte, and a type called <c>File</c> beside it would make
/// half this module read as a question about which one is meant.
/// </para>
/// <para>
/// <strong>The row is the file and the bytes are somewhere else.</strong> What
/// is here is metadata — the name, the place, the size, the media type — and
/// the bytes are at <see cref="StorageAddress.Of"/>, on the volume. The two
/// stores are the whole of what this application has to keep in step, and the
/// order it does it in is written down in <c>docs/adr/0006</c>: bytes first,
/// then the row, so that a crash between them leaves bytes nobody points at
/// rather than a row whose file is missing.
/// </para>
/// <para>
/// <strong>Its id is its address.</strong> A name is a label and changes as
/// often as the owner likes; the id is made at the first upload and never
/// changes, which is what makes <c>/api/files/{id}/content</c> a reference a
/// rename cannot break (<c>docs/mvp-plan.md</c>, PERSONAL-E6).
/// </para>
/// </remarks>
public sealed class StoredFile : IRecoverable
{
    /// <summary>What a file is called when nothing said what kind it is.</summary>
    /// <remarks>
    /// The instance never guesses from the name. A <c>.png</c> holding a script
    /// is a thing that exists, and the guess would be the instance's word for
    /// what the bytes are — which is exactly the claim a download must not
    /// make. What is stored is what the caller declared, and the download makes
    /// it an attachment whatever it says.
    /// </remarks>
    public const string UnknownMediaType = "application/octet-stream";

    /// <summary>How long a media type may be.</summary>
    public const int MediaTypeMaxLength = 255;

    private StoredFile()
    {
    }

    /// <summary>Made at the first upload, and never again.</summary>
    public Guid Id { get; private init; }

    /// <summary>What the owner called it (<see cref="FileName"/>).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The folder it is in, or nothing for one at the top.</summary>
    public Guid? FolderId { get; private set; }

    /// <summary>How many bytes are stored.</summary>
    public long Size { get; private set; }

    /// <summary>What the caller said the bytes are, never what the instance guessed.</summary>
    public string MediaType { get; private set; } = UnknownMediaType;

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When it was last changed, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Actor? DeletedBy { get; set; }

    /// <summary>Where the bytes are, relative to the storage root.</summary>
    public string Address => StorageAddress.Of(Id);

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>
    /// A file whose bytes are already on the volume.
    /// </summary>
    /// <param name="id">
    /// The id the bytes were written under. It is made by the caller rather
    /// than here, because the bytes are written first and have to be written
    /// somewhere: the address is the id, so the id exists before the row does.
    /// </param>
    /// <exception cref="Refusal"><c>validation</c>: that is not a name, or not a size.</exception>
    public static StoredFile Stored(
        Guid id, string? name, Guid? folder, long size, string? mediaType, DateTimeOffset now) => new()
    {
        Id = id,
        Name = FileName.Accepted(name),
        FolderId = folder,
        Size = Sized(size),
        MediaType = AcceptedMediaType(mediaType),
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Renames it, moves it, or both, and says whether that changed anything.
    /// </summary>
    /// <exception cref="Refusal"><c>validation</c>: that is not a name.</exception>
    public bool Change(string? name, Guid? folder, DateTimeOffset now)
    {
        var wanted = FileName.Accepted(name);

        if (string.Equals(Name, wanted, StringComparison.Ordinal) && FolderId == folder)
        {
            return false;
        }

        Name = wanted;
        FolderId = folder;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// New bytes for the same file: the id, the name and the place stay, and
    /// the size and the media type are whatever was just written.
    /// </summary>
    /// <remarks>
    /// <strong>This is not a version and there is no history behind it.</strong>
    /// VISION §6.5 rules complex file versioning out of the MVP, and a store
    /// that quietly kept every replaced upload would be a quota the owner
    /// cannot see. What replacing means is that every link to this file now
    /// answers with the new bytes, which is the point of the link being to the
    /// id.
    /// </remarks>
    public void Replace(long size, string? mediaType, DateTimeOffset now)
    {
        Size = Sized(size);
        MediaType = AcceptedMediaType(mediaType);
        UpdatedAt = now;
    }

    /// <summary>Moves the version on for a change the module made itself.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>
    /// The media type as it is stored: what the caller declared, without its
    /// parameters, or <see cref="UnknownMediaType"/> where that was not a media
    /// type at all.
    /// </summary>
    /// <remarks>
    /// Parameters go — <c>text/plain; charset=utf-8</c> is stored as
    /// <c>text/plain</c> — because the charset of a file this instance never
    /// reads is not a fact it has any business asserting, and it is one more
    /// thing a download would have to echo correctly. A type it cannot parse is
    /// not a refusal: a caller who sent nothing usable has still sent bytes
    /// worth keeping, and the answer to "what is this" is allowed to be "some
    /// bytes".
    /// </remarks>
    private static string AcceptedMediaType(string? declared)
    {
        var said = (declared ?? string.Empty).Split(';')[0].Trim();

        if (said.Length is 0 or > MediaTypeMaxLength)
        {
            return UnknownMediaType;
        }

        var slash = said.IndexOf('/', StringComparison.Ordinal);

        if (slash <= 0 || slash == said.Length - 1)
        {
            return UnknownMediaType;
        }

        foreach (var character in said)
        {
            // Everything outside printable ASCII, and the delimiters a header
            // uses to mean something else. A media type is a token, a slash and
            // a token; anything that is not is a header somebody assembled
            // wrongly, and storing it would put it back in a response.
            if (character <= ' ' || character >= (char)127 || character is '"' or ',' or '\\')
            {
                return UnknownMediaType;
            }
        }

        return said.ToLowerInvariant();
    }

    private static long Sized(long size) => size >= 0
        ? size
        : throw Refusal.Validation("size", "A file is not a negative number of bytes.");
}
