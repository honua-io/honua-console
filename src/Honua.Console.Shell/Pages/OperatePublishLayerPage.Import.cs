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

    protected override async Task OnParametersSetAsync()
    {
        var selection = (Schema: ImportedSchema, Table: ImportedTable);
        if (_importSelection == selection) return;
        _importSelection = selection;
        _importSelectionNotice = null;
        if (string.IsNullOrWhiteSpace(selection.Schema) || string.IsNullOrWhiteSpace(selection.Table)) return;

        var choiceVersion = _connectionChoiceVersion;
        var candidates = await Task.WhenAll(_connections.Select(ReadImportCandidateAsync));
        if (_importSelection != selection || _connectionChoiceVersion != choiceVersion) return;
        var matches = candidates.Where(candidate => candidate.Load.Tables.Any(table =>
            table.Schema == selection.Schema && table.Table == selection.Table)).ToArray();
        if (_connectionStates.Count > 0 || candidates.Any(candidate => candidate.Load.Unavailable) || matches.Length != 1)
        {
            _importSelectionNotice = $"Choose the connection containing {selection.Schema}.{selection.Table}. The imported table could not be matched to one available connection.";
            return;
        }

        ClearTableChoice();
        _connectionId = matches[0].ConnectionId;
        _tableLoad = matches[0].Load;
        _tableKey = _tableLoad.Tables.Single(table => table.Schema == selection.Schema && table.Table == selection.Table).QualifiedName;
        OnTableChanged();
        if (string.IsNullOrWhiteSpace(_serviceName)) _serviceName = selection.Table;
    }

    private async Task<(string ConnectionId, ServiceLayerTableLoad Load)> ReadImportCandidateAsync(
        Models.OperateConnectionSummary connection)
    {
        var load = new ServiceLayerTableLoad();
        await load.LoadAsync(PublishOperation, connection.Id);
        return (connection.Id, load);
    }
}
