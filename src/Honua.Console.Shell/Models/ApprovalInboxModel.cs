namespace Honua.Console.Shell.Models;

public enum ApprovalTicketType
{
    Other,

    PublishData,

    DataImport,

    ServerUpgrade,

    AccessConfig,

    Deliverable,
}

public static class ApprovalTicketPresentation
{
    public static string Label(ApprovalTicketType type) => type switch
    {
        ApprovalTicketType.PublishData => "Publish / update data",
        ApprovalTicketType.DataImport => "Import data",
        ApprovalTicketType.ServerUpgrade => "Server upgrade",
        ApprovalTicketType.AccessConfig => "Access / config",
        ApprovalTicketType.Deliverable => "Deliverable request",
        _ => "Other",
    };

    public static string Description(ApprovalTicketType type) => type switch
    {
        ApprovalTicketType.PublishData =>
            "Promote metadata, services, and layers across environments as a governed, rollback-able release.",
        ApprovalTicketType.DataImport =>
            "Import an agent-proposed dataset into the catalog — review provenance, profile, and validation before it runs.",
        ApprovalTicketType.ServerUpgrade =>
            "Upgrade (or roll back) the target Honua server version through a governed deploy operation.",
        ApprovalTicketType.AccessConfig =>
            "Change access, identity, or server/service configuration through a governed admin operation.",
        ApprovalTicketType.Deliverable =>
            "Author and publish an agent-proposed deliverable — a map, analysis, dashboard, or app — reviewing the plan, preview, and risk before it runs.",
        _ => "Agent-proposed operations that do not map onto a known GIS-desk ticket type.",
    };

    public static ApprovalTicketType Classify(ConsoleProposalSummary proposal) => Classify(proposal.Kind);

    public static ApprovalTicketType Classify(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.MetadataRelease => ApprovalTicketType.PublishData,
        ConsoleProposalKind.Seed => ApprovalTicketType.PublishData,
        ConsoleProposalKind.DataImport => ApprovalTicketType.DataImport,
        ConsoleProposalKind.Deploy => ApprovalTicketType.ServerUpgrade,
        ConsoleProposalKind.AdminConfigChange => ApprovalTicketType.AccessConfig,
        ConsoleProposalKind.Map => ApprovalTicketType.Deliverable,
        ConsoleProposalKind.Analysis => ApprovalTicketType.Deliverable,
        ConsoleProposalKind.Dashboard => ApprovalTicketType.Deliverable,
        ConsoleProposalKind.App => ApprovalTicketType.Deliverable,
        _ => ApprovalTicketType.Other,
    };
}

public sealed record ApprovalInboxItem(
    ApprovalTicketType TicketType,
    ConsoleProposalSummary Proposal)
{
    public string ProposalId => Proposal.ProposalId;

    public ConsoleProposalSource Source => Proposal.Source;

    public bool IsAwaitingApproval => Proposal.IsAwaitingApproval;
}

public sealed record ApprovalInboxSnapshot(IReadOnlyList<ApprovalInboxItem> Items)
{
    public static ApprovalInboxSnapshot Empty { get; } = new([]);

    public int AwaitingApprovalCount => Items.Count(item => item.IsAwaitingApproval);

    public int TotalCount => Items.Count;

    public IReadOnlyList<ApprovalTicketType> PresentTicketTypes =>
        Items.Select(item => item.TicketType).Distinct().OrderBy(type => (int)type).ToArray();

    public IReadOnlyList<ApprovalInboxItem> ForTicketType(ApprovalTicketType type) =>
        Items.Where(item => item.TicketType == type).ToArray();

    public IReadOnlyList<ConsoleProposalSource> PresentSources =>
        Items.Select(item => item.Source).Distinct().OrderBy(source => (int)source).ToArray();
}
