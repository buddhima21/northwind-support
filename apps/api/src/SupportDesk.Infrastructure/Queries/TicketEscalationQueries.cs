using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

public sealed class TicketEscalationQueries(SupportDbContext db) : ITicketEscalationQueries
{
    public async Task<IReadOnlyList<TicketEscalationDto>> GetHistoryAsync(int ticketId, CancellationToken ct) =>
        await Project(Rows().Where(row => row.History.TicketId == ticketId)
            .OrderByDescending(row => row.History.EscalatedAtUtc).ThenByDescending(row => row.History.Id)).ToListAsync(ct);

    public Task<TicketEscalationDto?> GetByIdAsync(int id, CancellationToken ct) =>
        Project(Rows().Where(row => row.History.Id == id)).FirstOrDefaultAsync(ct);

    private IQueryable<HistoryWithAgents> Rows() =>
        from history in db.Set<TicketEscalation>().AsNoTracking()
        from fromAgent in db.Set<Agent>().Where(a => a.Id == history.FromAgentId).DefaultIfEmpty()
        from toAgent in db.Set<Agent>().Where(a => a.Id == history.ToAgentId).DefaultIfEmpty()
        select new HistoryWithAgents { History = history, FromAgent = fromAgent, ToAgent = toAgent };

    private static IQueryable<TicketEscalationDto> Project(IQueryable<HistoryWithAgents> source) =>
        source.Select(row => new TicketEscalationDto(row.History.Id, row.History.TicketId,
            row.History.FromPriority, row.History.ToPriority,
            row.FromAgent == null ? null : new AgentSummaryDto(row.FromAgent.Id, row.FromAgent.FullName),
            row.ToAgent == null ? null : new AgentSummaryDto(row.ToAgent.Id, row.ToAgent.FullName),
            row.History.FromDueAtUtc, row.History.ToDueAtUtc, row.History.Reason,
            row.History.EscalatedBy, row.History.EscalatedAtUtc));

    private sealed class HistoryWithAgents
    {
        public required TicketEscalation History { get; init; }
        public Agent? FromAgent { get; init; }
        public Agent? ToAgent { get; init; }
    }
}
