namespace Personalaffe.Application.Ports;

/// <summary>
/// The storage root as an absolute path, worked out once by the host.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StorageSettings.Root"/> may be relative — it is <c>storage</c> by
/// default, and a development checkout means it — and what it is relative to is
/// the host's content root, which is the host's business and not
/// Infrastructure's. Resolving it once, at startup, is what keeps the place the
/// startup check proves writable and the place the store writes into from ever
/// being two different directories.
/// </para>
/// <para>
/// It is a record over a string rather than a string so that the container can
/// tell it from every other string somebody might register.
/// </para>
/// </remarks>
public sealed record StorageRoot(string Path);
