using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.Application.Abstractions;

public interface ITicketEscalationQueries
{
    Task<IReadOnlyList<TicketEscalationDto>> GetHistoryAsync(int ticketId, CancellationToken ct);
    Task<TicketEscalationDto?> GetByIdAsync(int id, CancellationToken ct);
}
