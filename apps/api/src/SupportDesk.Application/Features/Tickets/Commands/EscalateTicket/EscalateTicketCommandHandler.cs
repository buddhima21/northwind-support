using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketCommandHandler(ITicketRepository tickets, ICustomerRepository customers,
    ICategoryRepository categories, IAgentAssignmentQueries assignmentQueries,
    ITicketEscalationQueries historyQueries, SlaPolicy slaPolicy, IUnitOfWork unitOfWork,
    IClock clock, GetTicketQueryHandler getTicket)
{
    public async Task<TicketDetailDto> HandleAsync(int id, EscalateTicketRequest request,
        int authenticatedAgentId, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(authenticatedAgentId);
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);
        var nextPriority = ticket.NextEscalationPriority();
        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct)
            ?? throw new NotFoundException("Customer", ticket.CustomerId);
        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? throw new NotFoundException("Category", ticket.CategoryId);
        var candidates = await assignmentQueries.GetCandidatesAsync(ticket.CategoryId, ct);
        var current = candidates.FirstOrDefault(candidate => candidate.Id == ticket.AssignedAgentId);
        var assignment = current is not null && AssignmentPolicy.IsEligible(category, current)
            ? new AssignmentDecision(current, "Current agent remains eligible")
            : AssignmentPolicy.Select(category, candidates);
        var now = clock.UtcNow;
        var sla = slaPolicy.Calculate(nextPriority, customer.Tier, now);
        var actor = $"agent:{authenticatedAgentId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var history = ticket.Escalate(sla, assignment, request.Reason, actor, now);
        // The tracked ticket and newly appended history are committed in the same EF transaction.
        await unitOfWork.SaveChangesAsync(ct);
        var result = await getTicket.HandleAsync(id, ct);
        result.Triage = new TriageResultDto(nextPriority,
            $"Escalated from {history.FromPriority} to {history.ToPriority}", sla.WindowHours, sla.Reason, assignment.Reason);
        result.Escalation = await historyQueries.GetByIdAsync(history.Id, ct)
            ?? throw new NotFoundException("Escalation", history.Id);
        return result;
    }
}
