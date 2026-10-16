using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Queries.GetEscalations;

public sealed class GetEscalationsQueryHandler(ITicketRepository tickets, ITicketEscalationQueries history)
{
    public async Task<IReadOnlyList<TicketEscalationDto>> HandleAsync(int id, CancellationToken ct)
    {
        if (await tickets.GetByIdAsync(id, ct) is null) throw new NotFoundException("Ticket", id);
        return await history.GetHistoryAsync(id, ct);
    }
}
