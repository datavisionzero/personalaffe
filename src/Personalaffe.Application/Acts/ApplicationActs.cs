using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// One application, as a caller sees it: whether the workspace has it switched
/// on, and what this caller may do in it.
/// </summary>
public sealed record TheApplication(
    WorkspaceApplication Application, bool Enabled, Permission Permission, DateTimeOffset UpdatedAt);

/// <summary>
/// The four applications, their switches, and what the caller may do in each.
/// </summary>
/// <remarks>
/// <para>
/// All four, whoever asks. The set is closed and the contract names it, so
/// hiding the three an agent cannot reach would hide nothing and cost the CLI
/// the answer to "why was that refused" — the permission is beside the switch
/// for that reason. The <em>content</em> of an application is what access
/// decides, and none of it is here.
/// </para>
/// <para>
/// This is what the shell draws its navigation from: an application that is off
/// is not offered, and one the caller cannot read is not either
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E4).
/// </para>
/// </remarks>
public sealed class ReadTheApplications(ICallerIdentity caller, IApplicationSwitch applications)
{
    public async Task<IReadOnlyList<TheApplication>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var who = caller.Caller;

        return
        [
            .. (await applications.ReadAsync(cancellationToken)).Select(state => new TheApplication(
                state.Application,
                state.Enabled,
                who.Permissions.For(state.Application),
                state.UpdatedAt)),
        ];
    }
}

/// <summary>
/// Switches one application on or off. The owner's alone.
/// </summary>
/// <remarks>
/// <para>
/// The owner's, because it decides what a workspace is: an agent that could
/// switch an application off could hide the owner's content from the owner's
/// own screens, and one that could switch it on could reach an application the
/// owner had deliberately closed. It is the same list of things agent access
/// deliberately cannot do as issuing a credential or emptying the Trash
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E2).
/// </para>
/// <para>
/// Guarded like every other write, and switching an application to the state it
/// is already in still checks the version it was read at — a write that agreed
/// with what is stored is still a write somebody made from a stale screen.
/// </para>
/// </remarks>
public sealed class SwitchTheApplication(
    ICallerIdentity caller, IApplicationSwitch applications, TimeProvider clock)
{
    public async Task<TheApplication> ExecuteAsync(
        WorkspaceApplication application,
        bool enabled,
        ContentVersion held,
        CancellationToken cancellationToken)
    {
        var who = caller.Caller.RequireOwner("switch an application on or off");
        var state = await applications.ReadAsync(application, cancellationToken);

        if (!state.Version.Matches(held))
        {
            throw Refusal.Stale(
                "The application has changed since it was read. Read it again: the write you sent would "
                + "have replaced somebody else's newer one.",
                state.Version);
        }

        if (state.Switch(enabled, clock.GetUtcNow()))
        {
            await applications.SaveAsync(cancellationToken);
        }

        return new TheApplication(
            state.Application, state.Enabled, who.Permissions.For(state.Application), state.UpdatedAt);
    }
}

/// <summary>
/// The two questions every operation inside an application asks first: is the
/// application switched on, and may this caller do this in it.
/// </summary>
/// <remarks>
/// <para>
/// Not an act — nothing calls it on its own — but it lives beside them because
/// it is what the acts of PERSONAL-E5 to PERSONAL-E8 are built on. One place,
/// so that the answer cannot differ between two applications, and the order is
/// deliberate: access first, then the switch. A caller who may not reach an
/// application learns nothing about whether the owner has it switched on, and
/// gets the same <c>forbidden</c> either way.
/// </para>
/// <para>
/// The Trash reads the switch through this too, which is what keeps a
/// switched-off application out of an aggregate view. The <em>sweep</em> does
/// not: <c>ITrash.PurgeAsync</c> takes a deadline and nothing else, and
/// retention runs whether an application is switched on or off
/// (<c>docs/codebase.md</c>).
/// </para>
/// </remarks>
public sealed class ReachingAnApplication(ICallerIdentity caller, IApplicationSwitch applications)
{
    /// <summary>The caller, for a read of <paramref name="application"/>.</summary>
    /// <exception cref="Refusal">
    /// <c>forbidden</c>: this access does not reach it.
    /// <c>disabled</c>: the workspace has it switched off.
    /// </exception>
    public Task<Caller> ToReadAsync(WorkspaceApplication application, CancellationToken cancellationToken) =>
        SwitchedOnAsync(caller.Caller.RequireRead(application), application, cancellationToken);

    /// <summary>The caller, for a change to <paramref name="application"/>.</summary>
    /// <exception cref="Refusal">
    /// <c>forbidden</c>: this access does not change it.
    /// <c>disabled</c>: the workspace has it switched off.
    /// </exception>
    public Task<Caller> ToWriteAsync(WorkspaceApplication application, CancellationToken cancellationToken) =>
        SwitchedOnAsync(caller.Caller.RequireWrite(application), application, cancellationToken);

    /// <summary>
    /// Whether <paramref name="application"/> is switched on, for the aggregate
    /// views that leave one out rather than refuse the whole request.
    /// </summary>
    public async Task<bool> SwitchedOnAsync(
        WorkspaceApplication application, CancellationToken cancellationToken) =>
        (await applications.ReadAsync(application, cancellationToken)).Enabled;

    private async Task<Caller> SwitchedOnAsync(
        Caller who, WorkspaceApplication application, CancellationToken cancellationToken) =>
        await SwitchedOnAsync(application, cancellationToken)
            ? who
            : throw ApplicationState.Off(application);
}
