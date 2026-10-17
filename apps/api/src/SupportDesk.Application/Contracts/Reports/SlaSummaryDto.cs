using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Reports;

public sealed record SlaSummaryDto(
    int? AgentId,
    string? AgentName,
    TicketPriority? Priority,
    int TotalTickets,
    int Breached,
    int AtRisk,
    int WithinSla,
    int Met,
    int NotApplicable,
    double BreachRate,
    double? AverageResolutionHours);
