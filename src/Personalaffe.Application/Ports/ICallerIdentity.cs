using Personalaffe.Domain;

namespace Personalaffe.Application.Ports;

/// <summary>
/// Whoever this request is being made by, answered by the adapter that admitted
/// them.
/// </summary>
/// <remarks>
/// An act asks this rather than taking a caller as an argument, so that no
/// endpoint can forget to pass one and no act can be called for somebody it was
/// not called for. Asking it outside a request, or on the one operation that is
/// outside the door, is a bug and throws.
/// </remarks>
public interface ICallerIdentity
{
    Caller Caller { get; }
}
