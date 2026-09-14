using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// One instance at a time, said in Postgres: a session-level advisory lock, the
/// same way <see cref="SchemaMigrator"/> says it.
/// </summary>
/// <remarks>
/// The connection is opened by hand and kept open for the work, because the
/// lock belongs to the session that took it — one taken on a connection the
/// pool hands back is a lock nobody holds. It needs no table, which is the
/// other reason the migrator uses one: there is no chicken and egg with a
/// schema that might not exist yet.
/// </remarks>
public sealed class ExclusiveWork(PersonalaffeDbContext context) : IExclusiveWork
{
    public async Task<bool> TryAsync(
        string name, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var key = KeyOf(name);

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await AskAsync($"select pg_try_advisory_lock({key}) as \"Value\"", cancellationToken))
            {
                return false;
            }

            try
            {
                await work(cancellationToken);
            }
            finally
            {
                // CancellationToken.None: a cancelled sweep still gives the lock
                // back, on the connection that is about to be closed.
                await AskAsync($"select pg_advisory_unlock({key}) as \"Value\"", CancellationToken.None);
            }

            return true;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> AskAsync(string sql, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<bool>(sql).SingleAsync(cancellationToken);

    /// <summary>
    /// A stable number for a name, so that two instances of the same build ask
    /// for the same lock and two different pieces of work do not collide. FNV-1a
    /// folded to 63 bits: advisory lock keys are signed, and a negative one
    /// reads like a mistake in a log.
    /// </summary>
    private static string KeyOf(string name)
    {
        var hash = 14695981039346656037UL;

        foreach (var octet in Encoding.UTF8.GetBytes(name))
        {
            hash = (hash ^ octet) * 1099511628211UL;
        }

        return ((long)(hash & long.MaxValue)).ToString(CultureInfo.InvariantCulture);
    }
}
