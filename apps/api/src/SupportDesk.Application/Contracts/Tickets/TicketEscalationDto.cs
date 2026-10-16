using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

public sealed record EscalateTicketRequest(string Reason, string EscalatedBy);

public sealed record TicketEscalationDto(int Id, int TicketId,
    TicketPriority FromPriority, TicketPriority ToPriority,
    AgentSummaryDto? FromAgent, AgentSummaryDto? ToAgent,
    DateTime? FromDueAtUtc, DateTime ToDueAtUtc,
    string Reason, string EscalatedBy, DateTime EscalatedAtUtc);
