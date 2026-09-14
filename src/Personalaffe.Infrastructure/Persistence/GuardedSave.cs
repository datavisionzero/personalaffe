using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The other half of the guard on a write (<c>docs/api.md</c>, The guarded
/// write): the half that is atomic.
/// </summary>
/// <remarks>
/// <para>
/// Checking the version an act is holding against the one it read a moment ago
/// catches everything except the case the guard exists for — two writes
/// arriving at once, both of which read the same version and both of which pass
/// the check. What closes that window is the database: a lasting object
/// declares <c>UpdatedAt</c> a concurrency token in its configuration, so EF
/// writes <c>update … where id = … and updated_at = …</c> and nothing is
/// changed when somebody else got there first.
/// </para>
/// <para>
/// This turns that into the product's word for it. A module calls it instead of
/// <c>SaveChangesAsync</c> for every change to content it guards, and the two
/// lines of configuration and this call are the whole of what a module inherits.
/// </para>
/// </remarks>
public static class GuardedSave
{
    /// <summary>
    /// Saves what is tracked, and refuses as <c>stale</c> if the row moved
    /// underneath it.
    /// </summary>
    /// <param name="context">The context holding the change.</param>
    /// <param name="what">
    /// What the caller was changing, as a sentence starts it — "The page", "The
    /// task". It is the first words of the refusal.
    /// </param>
    /// <param name="cancellationToken">The request's.</param>
    /// <exception cref="Refusal"><c>stale</c>: somebody else wrote first.</exception>
    public static async Task SaveAsync(DbContext context, string what, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Everything this context was tracking is now suspect: the entries
            // still carry the values the losing write wanted, and a later read
            // through the same context would hand them out as if they were
            // stored. Detaching is what makes the refusal mean what it says.
            foreach (var entry in context.ChangeTracker.Entries().ToArray())
            {
                entry.State = EntityState.Detached;
            }

            throw Refusal.Stale(
                $"{what} has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.");
        }
    }
}
