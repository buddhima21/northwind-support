using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Counts workloads and checks specializations in SQL, without loading ticket histories
/// or running a separate query per agent. The domain policy owns eligibility and choice.
/// </summary>
public sealed class AgentAssignmentQueries(SupportDbContext db) : IAgentAssignmentQueries
{
    public async Task<IReadOnlyList<AssignmentCandidate>> GetCandidatesAsync(int categoryId, CancellationToken ct) =>
        await db.Set<Agent>()
            .AsNoTracking()
            .OrderBy(agent => agent.Id)
            .Select(agent => new AssignmentCandidate(
                agent.Id,
                agent.FullName,
                agent.IsActive,
                agent.MaxOpenTickets,
                db.Set<Ticket>().Count(ticket =>
                    ticket.AssignedAgentId == agent.Id
                    && ticket.Status != TicketStatus.Resolved
                    && ticket.Status != TicketStatus.Closed),
                agent.Specializations.Any(specialization => specialization.CategoryId == categoryId)))
            .ToListAsync(ct);
}
