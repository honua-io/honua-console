using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Pages;

public partial class OperatePublishLayerPage
{
    [SupplyParameterFromQuery(Name = "schema")]
    public string? ImportedSchema { get; set; }

    [SupplyParameterFromQuery(Name = "table")]
    public string? ImportedTable { get; set; }

    private (string? Schema, string? Table)? _importSelection;
    private string? _importSelectionNotice;
    private int _connectionChoiceVersion;

    protected override Task OnParametersSetAsync()
    {
        var selection = (Schema: ImportedSchema, Table: ImportedTable);
        if (_importSelection == selection) return Task.CompletedTask;
        _importSelection = selection;
        _connectionChoiceVersion++;
        _connectionId = string.Empty;
        ClearTableChoice();
        _tableLoad.Reset();
        _importSelectionNotice = string.IsNullOrWhiteSpace(selection.Schema) || string.IsNullOrWhiteSpace(selection.Table)
            ? null
            : $"Choose the connection containing {selection.Schema}.{selection.Table}.";
        return Task.CompletedTask;
    }

    private void SelectImportedTable()
    {
        var table = _tableLoad.Tables.FirstOrDefault(table =>
            table.Schema == ImportedSchema && table.Table == ImportedTable);
        if (table is null) return;
        _tableKey = table.QualifiedName;
        OnTableChanged();
        if (string.IsNullOrWhiteSpace(_serviceName)) _serviceName = table.Table;
    }
}
