using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.Application.Abstractions;

public interface ISlaReportQueries
{
    Task<IReadOnlyList<SlaSummaryDto>> GetSummaryAsync(SlaSummaryQuery query, CancellationToken ct);
}
