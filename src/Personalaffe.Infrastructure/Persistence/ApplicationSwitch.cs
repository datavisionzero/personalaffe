using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>Which applications are switched on (<see cref="IApplicationSwitch"/>).</summary>
/// <remarks>
/// <para>
/// The four rows are created by the migration that creates the table, so
/// nothing here inserts and nothing has to decide what a missing row means. A
/// database somebody deleted a row out of by hand answers with one application
/// missing, and <see cref="ReadAsync(WorkspaceApplication, CancellationToken)"/>
/// says so rather than assuming.
/// </para>
/// <para>
/// One application is read with <c>FindAsync</c> and not a query, which is what
/// makes asking the switch cheap enough to ask every time. It answers from the
/// change tracker when the row is already loaded, so a request that reads all
/// four — the Trash fanning out over its contributors — and then asks about each
/// of them makes one round trip rather than five. The context is scoped to the
/// request, so nothing is cached past the answer it was read for.
/// </para>
/// </remarks>
public sealed class ApplicationSwitch(PersonalaffeDbContext context) : IApplicationSwitch
{
    public async Task<IReadOnlyList<ApplicationState>> ReadAsync(CancellationToken cancellationToken) =>
        [.. (await context.Applications.ToListAsync(cancellationToken)).OrderBy(state => state.Application)];

    public async Task<ApplicationState> ReadAsync(
        WorkspaceApplication application, CancellationToken cancellationToken) =>
        await context.Applications.FindAsync([application], cancellationToken)
        ?? throw new InvalidOperationException(
            $"The row for {application} is missing from the application switch. The migration that "
            + "creates the table creates all four; a database this is true of has been edited by hand.");

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The application", cancellationToken);
}
