using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    // The central fallback authorization policy applies to reports too.
    [HttpGet("sla-summary")]
    public Task<IReadOnlyList<SlaSummaryDto>> Summary([FromQuery] SlaSummaryQuery query,
        [FromServices] ISlaReportQueries reports, CancellationToken ct) =>
        reports.GetSummaryAsync(query, ct);
}
