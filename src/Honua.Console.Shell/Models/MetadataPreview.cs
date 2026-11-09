namespace Honua.Console.Shell.Models;

public sealed record MetadataPreviewRequest(string? DatasetId, string Section, string? ServiceId);

public sealed record MetadataPreviewRead(string State)
{
    public string? Message { get; init; }
    public MetadataDatasetPreview? Dataset { get; init; }
    public IReadOnlyList<MetadataSchemaPreview> Schemas { get; init; } = [];
    public MetadataImportPreview? Import { get; init; }
}

public sealed record MetadataDatasetPreview(string DatasetId, string Title)
{
    public string? ServiceId { get; init; }
    public string Context { get; init; } = string.Empty;
    public IReadOnlyList<MetadataFieldPreview> Fields { get; init; } = [];
    public IReadOnlyList<MetadataHistoryPreview> History { get; init; } = [];
}

public sealed record MetadataFieldPreview(string Key, string Label, string Group)
{
    public string? LocalValue { get; init; }
    public string? EffectiveValue { get; init; }
    public string? InheritedValue { get; init; }
    public string? InheritedFrom { get; init; }
    public bool Editable { get; init; }
    public bool HasInheritance { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<MetadataHistoryPreview> History { get; init; } = [];
}

public sealed record MetadataHistoryPreview(string Field, string Actor, string Client)
{
    public string? When { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
    public string State { get; init; } = "Not reported";
}

public sealed record MetadataSchemaPreview(string Key, string Name, string Type)
{
    public bool Required { get; init; }
    public bool Inherits { get; init; }
    public string Visibility { get; init; } = "Not reported";
    public IReadOnlyList<string> Kinds { get; init; } = [];
    public string? Filled { get; init; }
    public string? Output { get; init; }
    public bool Retired { get; init; }
    public IReadOnlyList<string> Choices { get; init; } = [];
}

public sealed record MetadataImportPreview(string DatasetId, string FileName, string Format)
{
    public string? ServiceId { get; init; }
    public IReadOnlyList<MetadataImportRow> Rows { get; init; } = [];
    public IReadOnlyList<MetadataUnmappedPreview> Unmapped { get; init; } = [];
}

public sealed record MetadataImportRow(string Key, string Label, string SourcePath)
{
    public string? Current { get; init; }
    public string? Incoming { get; init; }
    public string? InheritedFrom { get; init; }
    public bool Changed { get; init; }
    public bool Selected { get; init; }
}

public sealed record MetadataUnmappedPreview(string SourcePath, string Content, string Reason);
