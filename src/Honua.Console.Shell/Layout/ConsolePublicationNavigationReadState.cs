using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Layout;

public sealed class ConsolePublicationNavigationReadState
{
    public bool Loading { get; init; }
    public OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>? ServicesRead { get; init; }
    public bool CanConnectEnvironment { get; init; }
}
