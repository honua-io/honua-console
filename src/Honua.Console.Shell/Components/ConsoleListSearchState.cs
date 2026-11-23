using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Components;

internal sealed class ConsoleListSearchState
{
    public string Query { get; private set; } = string.Empty;

    public int Page { get; set; } = 1;

    public void Search(ChangeEventArgs args)
    {
        Query = args.Value?.ToString()?.Trim() ?? string.Empty;
        Page = 1;
    }
}
