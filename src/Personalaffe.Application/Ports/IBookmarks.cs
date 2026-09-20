using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Application.Ports;

public interface IBookmarks
{
    Task<IReadOnlyList<Bookmark>> ListAsync(int offset, int limit, CancellationToken token);
    Task<IReadOnlyList<BookmarkFolder>> FoldersAsync(int offset, int limit, CancellationToken token);
    Task<Bookmark?> FindAsync(Guid id, CancellationToken token);
    Task<BookmarkFolder?> FindFolderAsync(Guid id, CancellationToken token);
    Task<bool> PrivateAsync(Guid? folder, CancellationToken token);
    Task CheckPlacementAsync(Guid? moving, Guid? parent, CancellationToken token);
    Task<bool> FolderNameTakenAsync(string name, Guid? parent, Guid? except, CancellationToken token);
    void Add(Bookmark bookmark);
    void Add(BookmarkFolder folder);
    Task SaveAsync(CancellationToken token);
    Task DeleteAsync(Bookmark bookmark, CancellationToken token);
    Task DeleteAsync(BookmarkFolder folder, CancellationToken token);
}
