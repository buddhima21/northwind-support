namespace SupportDesk.Application.Contracts.Reports;

public sealed record SlaSummaryQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string GroupBy { get; init; } = "agent";
}
