namespace Personalaffe.Domain;

/// <summary>The two independently throttled proofs that can reopen a locked browser.</summary>
public enum UnlockCredential
{
    Pin,
    Password,
}

/// <summary>What the server knows about the additional lock on one browser session.</summary>
public sealed record InactivityLockState(bool Enabled, bool Locked, DateTimeOffset? LocksAt);
