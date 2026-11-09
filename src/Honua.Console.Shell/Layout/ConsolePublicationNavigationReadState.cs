using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Layout;

public sealed class ConsolePublicationNavigationReadState
{
    public bool Loading { get; init; }
    public OperateSectionResult<HonuaAdminServicePage>? ServicesRead { get; init; }
    public OperateSectionResult<ConsoleFolderCatalog>? FoldersRead { get; init; }
    public bool CanConnectEnvironment { get; init; }
    public EventCallback Retry { get; init; }
}
