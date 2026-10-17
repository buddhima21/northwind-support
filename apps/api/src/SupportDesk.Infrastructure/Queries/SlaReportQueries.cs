using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

public sealed class SlaReportQueries(SupportDbContext db, IClock clock) : ISlaReportQueries
{
    public async Task<IReadOnlyList<SlaSummaryDto>> GetSummaryAsync(SlaSummaryQuery query, CancellationToken ct)
    {
        var tickets = db.Set<Ticket>().AsNoTracking();
        if (query.From is { } from) tickets = tickets.Where(t => t.CreatedAtUtc >= from.UtcDateTime);
        if (query.To is { } to) tickets = tickets.Where(t => t.CreatedAtUtc < to.UtcDateTime);

        var rows = Project(tickets, clock.UtcNow);
        var byAgent = string.Equals(query.GroupBy, "agent", StringComparison.OrdinalIgnoreCase);
        var labelled = from row in rows
                       from agent in db.Set<Agent>().Where(a => a.Id == row.AgentId).DefaultIfEmpty()
                       select new { Row = row, AgentName = agent == null ? null : agent.FullName };

        // Nothing is materialized until after GROUP BY, conditional counts and AVG.
        return await labelled.GroupBy(x => new
            {
                AgentId = byAgent ? x.Row.AgentId : null,
                AgentName = byAgent ? x.AgentName : null,
                Priority = byAgent ? null : (TicketPriority?)x.Row.Priority
            })
            .OrderBy(g => g.Key.AgentId).ThenBy(g => g.Key.Priority)
            .Select(g => new SlaSummaryDto(g.Key.AgentId, g.Key.AgentName, g.Key.Priority,
                g.Count(),
                g.Count(x => x.Row.Status == SlaStatus.Breached),
                g.Count(x => x.Row.Status == SlaStatus.AtRisk),
                g.Count(x => x.Row.Status == SlaStatus.WithinSla),
                g.Count(x => x.Row.Status == SlaStatus.Met),
                g.Count(x => x.Row.Status == SlaStatus.NotApplicable),
                (double)g.Count(x => x.Row.Status == SlaStatus.Breached) / g.Count(),
                g.Average(x => x.Row.ResolutionHours)))
            .ToListAsync(ct);
    }

    private static IQueryable<ReportRow> Project(IQueryable<Ticket> tickets, DateTime now)
    {
        var status = SlaEvaluator.For<Ticket>(t => t.DueAtUtc, t => t.ResolvedAtUtc, t => t.AtRiskAtUtc, now);
        Expression<Func<Ticket, SlaStatus, ReportRow>> template = (t, sla) => new ReportRow
        {
            AgentId = t.AssignedAgentId,
            Priority = t.Priority,
            Status = sla,
            // Day numbers plus time-of-day components translate on SQL Server and SQLite.
            // This avoids DATEDIFF millisecond overflow for tickets older than 24 days.
            ResolutionHours = t.ResolvedAtUtc == null ? null :
                (DateOnly.FromDateTime(t.ResolvedAtUtc.Value).DayNumber - DateOnly.FromDateTime(t.CreatedAtUtc).DayNumber) * 24.0
                + t.ResolvedAtUtc.Value.Hour - t.CreatedAtUtc.Hour
                + (t.ResolvedAtUtc.Value.Minute - t.CreatedAtUtc.Minute) / 60.0
                + (t.ResolvedAtUtc.Value.Second - t.CreatedAtUtc.Second) / 3600.0
                + (t.ResolvedAtUtc.Value.Millisecond - t.CreatedAtUtc.Millisecond) / 3600000.0
        };

        // Inline the existing domain SLA expression rather than duplicating its rules in SQL.
        var body = new Substitute(new Dictionary<ParameterExpression, Expression>
        {
            [template.Parameters[0]] = status.Parameters[0],
            [template.Parameters[1]] = status.Body
        }).Visit(template.Body)!;
        return tickets.Select(Expression.Lambda<Func<Ticket, ReportRow>>(body, status.Parameters));
    }

    private sealed class ReportRow
    {
        public int? AgentId { get; init; }
        public TicketPriority Priority { get; init; }
        public SlaStatus Status { get; init; }
        public double? ResolutionHours { get; init; }
    }

    private sealed class Substitute(IReadOnlyDictionary<ParameterExpression, Expression> replacements) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            replacements.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
    }
}
