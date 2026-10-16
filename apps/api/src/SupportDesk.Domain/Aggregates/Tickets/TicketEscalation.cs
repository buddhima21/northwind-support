using SupportDesk.Domain.Common;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>An append-only snapshot of one escalation; no mutation methods are exposed.</summary>
public sealed class TicketEscalation : Entity
{
    private TicketEscalation() { }

    internal TicketEscalation(int ticketId, TicketPriority fromPriority, TicketPriority toPriority,
        int? fromAgentId, int? toAgentId, DateTime? fromDueAtUtc, DateTime toDueAtUtc,
        string reason, string escalatedBy, DateTime escalatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(escalatedBy);
        reason = reason.Trim();
        escalatedBy = escalatedBy.Trim();
        if (reason.Length is < 5 or > 500)
            throw new ArgumentException("Reason must contain 5 to 500 characters.", nameof(reason));
        if (escalatedBy.Length > 100)
            throw new ArgumentException("EscalatedBy must contain at most 100 characters.", nameof(escalatedBy));

        TicketId = ticketId;
        FromPriority = fromPriority;
        ToPriority = toPriority;
        FromAgentId = fromAgentId;
        ToAgentId = toAgentId;
        FromDueAtUtc = fromDueAtUtc;
        ToDueAtUtc = toDueAtUtc;
        Reason = reason;
        EscalatedBy = escalatedBy;
        EscalatedAtUtc = escalatedAtUtc;
    }

    public int TicketId { get; private set; }
    public TicketPriority FromPriority { get; private set; }
    public TicketPriority ToPriority { get; private set; }
    public int? FromAgentId { get; private set; }
    public int? ToAgentId { get; private set; }
    public DateTime? FromDueAtUtc { get; private set; }
    public DateTime ToDueAtUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string EscalatedBy { get; private set; } = string.Empty;
    public DateTime EscalatedAtUtc { get; private set; }
}
