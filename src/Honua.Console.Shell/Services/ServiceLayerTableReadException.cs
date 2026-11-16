using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// The server did not return a connection's table list. Carries the endpoint state so a picker can
// say the read failed (and offer a retry) instead of showing the connection as having no tables.
public sealed class ServiceLayerTableReadException : Exception
{
    public ServiceLayerTableReadException()
        : this(new OperateCapabilityState("Connections", "Unavailable", string.Empty, "The table list could not be read."))
    {
    }

    public ServiceLayerTableReadException(string message)
        : this(new OperateCapabilityState("Connections", "Unavailable", string.Empty, message))
    {
    }

    public ServiceLayerTableReadException(string message, Exception innerException)
        : base(message, innerException)
    {
        State = new OperateCapabilityState("Connections", "Unavailable", string.Empty, message);
    }

    public ServiceLayerTableReadException(OperateCapabilityState state)
        : base(state?.Detail)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public OperateCapabilityState State { get; }
}
