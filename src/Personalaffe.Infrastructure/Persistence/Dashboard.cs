using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The few rows behind each tile (<see cref="IDashboard"/>).
/// </summary>
/// <remarks>
/// <para>
/// Four small queries, each with a limit, each projecting straight into the
/// record a tile draws — no entity is loaded and nothing is tracked, because
/// nothing here is going to be written back.
/// </para>
/// <para>
/// <strong>Every one of them reads through the query filter.</strong> These are
/// ordinary LINQ over the context, so what is in the Trash is absent without
/// this file saying so (<c>Configurations/RecoverableContent.cs</c>) — which is
/// the opposite arrangement to <see cref="Search"/>, whose hand-written SQL has
/// to say it four times, and the reason to keep the dashboard in LINQ.
/// </para>
/// </remarks>
public sealed class Dashboard(PersonalaffeDbContext context) : IDashboard
{
    public async Task<IReadOnlyList<OpenTask>> OpenTasksAsync(
        int limit, CancellationToken cancellationToken) =>
        await context.Tasks
            .Where(task => task.CompletedAt == null)
            .Join(
                context.TaskLists,
                task => task.ListId,
                list => list.Id,
                (task, list) => new { task, list })
            // A date first, and the undated after it. Postgres sorts nulls last
            // on an ascending column anyway; saying it out loud is what keeps
            // the order a decision rather than a default somebody inherits.
            .OrderBy(both => both.task.DueOn == null)
            .ThenBy(both => both.task.DueOn)
            .ThenBy(both => both.task.Position)
            .ThenBy(both => both.task.Id)
            .Take(limit)
            .Select(both => new OpenTask(
                both.task.Id,
                both.task.Title,
                both.list.Id,
                both.list.Name,
                both.task.DueOn,
                both.task.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RecentPage>> RecentPagesAsync(
        int limit, CancellationToken cancellationToken) =>
        await context.Pages
            .OrderByDescending(page => page.UpdatedAt)
            .ThenBy(page => page.Id)
            .Take(limit)
            .Select(page => new RecentPage(page.Id, page.Title, page.ParentId, page.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RecentEntry>> RecentEntriesAsync(
        int limit, CancellationToken cancellationToken) =>
        await context.ScratchpadEntries
            // Newest change first, and not newest capture: an entry that was
            // just pasted into again is the one somebody is working with, and
            // its expiry runs from that moment too (ADR 0005).
            .OrderByDescending(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.Id)
            .Take(limit)
            .Select(entry => new RecentEntry(entry.Id, entry.Text, entry.Pinned, entry.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RecentFile>> RecentFilesAsync(
        int limit, CancellationToken cancellationToken) =>
        await context.Files
            .OrderByDescending(file => file.UpdatedAt)
            .ThenBy(file => file.Id)
            .Take(limit)
            .Select(file => new RecentFile(file.Id, file.Name, file.FolderId, file.Size, file.UpdatedAt))
            .ToListAsync(cancellationToken);
}
