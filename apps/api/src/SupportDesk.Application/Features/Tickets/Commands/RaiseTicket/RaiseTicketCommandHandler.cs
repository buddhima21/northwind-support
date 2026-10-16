using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>Creates and saves a ticket with an explained priority, SLA and assignment.</summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentAssignmentQueries assignmentQueries,
    SlaPolicy slaPolicy,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    public async Task<TicketDetailDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);
        var category = await categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException("Category", request.CategoryId);

        var candidates = await assignmentQueries.GetCandidatesAsync(request.CategoryId, ct);
        var now = clock.UtcNow;
        var priority = PriorityPolicy.Determine(request.RequestedPriority, category);
        var sla = slaPolicy.Calculate(priority.AppliedPriority, customer.Tier, now);
        var assignment = AssignmentPolicy.Select(category, candidates);

        var ticket = Ticket.RaiseTriaged(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            priority,
            sla,
            assignment,
            now);

        // A single insert carries all triage decisions; there is no partly triaged row.
        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId} with priority {Priority} and agent {AgentId}.",
            ticket.Reference, ticket.CustomerId, ticket.CategoryId, ticket.Priority, ticket.AssignedAgentId);

        var result = await getTicket.HandleAsync(ticket.Id, ct);
        result.Triage = new TriageResultDto(priority.AppliedPriority, priority.Reason,
            sla.WindowHours, sla.Reason, assignment.Reason);
        return result;
    }
}
