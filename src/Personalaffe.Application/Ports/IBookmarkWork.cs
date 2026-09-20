namespace Personalaffe.Application.Ports;

/// <summary>One atomic saved-link operation, including its visibility checks.</summary>
public interface IBookmarkWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
