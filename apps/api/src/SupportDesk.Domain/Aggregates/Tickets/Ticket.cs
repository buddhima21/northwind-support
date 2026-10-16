using SupportDesk.Domain.Common;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// A support request raised by a customer. Every change to a ticket goes through one of its
/// methods, so its rules cannot be bypassed.
/// </summary>
/// <remarks>
/// The customer, the category and the assigned agent are other aggregates and are referenced
/// by id only.
/// </remarks>
public sealed class Ticket : AggregateRoot
{
    private readonly List<TicketEscalation> _escalations = [];

    private Ticket()
    {
        // For EF Core.
    }

    /// <summary>Human-friendly identifier shown to customers, e.g. TCK-0042.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int CustomerId { get; private set; }

    public int CategoryId { get; private set; }

    /// <summary>Null while the ticket is waiting for an owner.</summary>
    public int? AssignedAgentId { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When the team has committed to responding by. Null when no SLA applies.</summary>
    public DateTime? DueAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public DateTime? SlaStartedAtUtc { get; private set; }

    public DateTime? AtRiskAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, incremented by every ticket mutation.</summary>
    public int Revision { get; private set; }

    public IReadOnlyCollection<TicketEscalation> Escalations => _escalations;

    /// <summary>True while the ticket still needs work from an agent.</summary>
    public bool IsOpen => Status is not (TicketStatus.Resolved or TicketStatus.Closed);

    /// <summary>
    /// Raises a new ticket for a customer. It starts as <see cref="TicketStatus.New"/>, with no
    /// owner and no due date.
    /// </summary>
    public static Ticket Raise(
        string reference,
        string title,
        string description,
        int customerId,
        int categoryId,
        TicketPriority priority,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Ticket
        {
            Reference = reference,
            Title = title.Trim(),
            Description = description.Trim(),
            CustomerId = customerId,
            CategoryId = categoryId,
            Priority = priority,
            Status = TicketStatus.New,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Creates a new ticket with decisions already evaluated by the shared policies.
    /// Raise remains available for historical seed data and test fixtures without an SLA.
    /// </summary>
    public static Ticket RaiseTriaged(
        string reference,
        string title,
        string description,
        int customerId,
        int categoryId,
        PriorityDecision priority,
        SlaDecision sla,
        AssignmentDecision assignment,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(sla);
        ArgumentNullException.ThrowIfNull(assignment);
        if (!Enum.IsDefined(priority.AppliedPriority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }
        if (nowUtc.Kind != DateTimeKind.Utc || sla.DueAtUtc.Kind != DateTimeKind.Utc
            || sla.Window <= TimeSpan.Zero || sla.DueAtUtc != nowUtc.Add(sla.Window)
            || sla.AtRiskAtUtc.Kind != DateTimeKind.Utc || sla.AtRiskAtUtc < nowUtc || sla.AtRiskAtUtc > sla.DueAtUtc)
        {
            throw new ArgumentException("The SLA must be a positive window starting at the UTC creation time.", nameof(sla));
        }
        if (assignment.Agent is { Id: <= 0 })
        {
            throw new ArgumentException("An assigned agent must have a persisted ID.", nameof(assignment));
        }

        var ticket = Raise(reference, title, description, customerId, categoryId,
            priority.AppliedPriority, nowUtc);
        ticket.DueAtUtc = sla.DueAtUtc;
        ticket.SlaStartedAtUtc = nowUtc;
        ticket.AtRiskAtUtc = sla.AtRiskAtUtc;
        ticket.AssignedAgentId = assignment.Agent?.Id;
        return ticket;
    }

    public TicketPriority NextEscalationPriority()
    {
        if (Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException($"Ticket {Reference} is {Status} and cannot be escalated.");
        }
        return Priority switch
        {
            TicketPriority.Low => TicketPriority.Medium,
            TicketPriority.Medium => TicketPriority.High,
            TicketPriority.High => TicketPriority.Critical,
            _ => throw new BusinessRuleViolationException($"Ticket {Reference} is already Critical and cannot be escalated further.")
        };
    }

    public TicketEscalation Escalate(SlaDecision sla, AssignmentDecision assignment,
        string reason, string escalatedBy, DateTime nowUtc)
    {
        var next = NextEscalationPriority();
        ArgumentNullException.ThrowIfNull(sla);
        ArgumentNullException.ThrowIfNull(assignment);
        if (nowUtc.Kind != DateTimeKind.Utc || sla.DueAtUtc.Kind != DateTimeKind.Utc
            || sla.Window <= TimeSpan.Zero || sla.DueAtUtc != nowUtc.Add(sla.Window)
            || sla.AtRiskAtUtc.Kind != DateTimeKind.Utc || sla.AtRiskAtUtc < nowUtc || sla.AtRiskAtUtc > sla.DueAtUtc)
        {
            throw new ArgumentException("The SLA must start at the UTC escalation time.", nameof(sla));
        }
        if (assignment.Agent is { Id: <= 0 })
        {
            throw new ArgumentException("An assigned agent must have a persisted ID.", nameof(assignment));
        }
        // Constructing history validates the reason/actor before any ticket fields change.
        var history = new TicketEscalation(Id, Priority, next, AssignedAgentId, assignment.Agent?.Id,
            DueAtUtc, sla.DueAtUtc, reason, escalatedBy, nowUtc);
        Priority = next;
        DueAtUtc = sla.DueAtUtc;
        SlaStartedAtUtc = nowUtc;
        AtRiskAtUtc = sla.AtRiskAtUtc;
        AssignedAgentId = assignment.Agent?.Id;
        UpdatedAtUtc = nowUtc;
        Revision++;
        _escalations.Add(history);
        return history;
    }

    /// <summary>
    /// Moves the ticket through its lifecycle, keeping the resolution timestamp in step.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">The ticket is closed.</exception>
    public void ChangeStatus(TicketStatus status, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed && status != TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is closed and cannot be moved to {status}.");
        }

        ResolvedAtUtc = status switch
        {
            TicketStatus.Resolved => nowUtc,
            TicketStatus.Closed => ResolvedAtUtc ?? nowUtc,
            _ => null
        };

        Status = status;
        UpdatedAtUtc = nowUtc;
        Revision++;
    }

    /// <summary>
    /// Hands the ticket to an agent, or takes it back off them when <paramref name="agentId"/>
    /// is null. Whether the agent may take it is checked by the caller, which can see agents.
    /// </summary>
    public void AssignTo(int? agentId, DateTime nowUtc)
    {
        AssignedAgentId = agentId;
        UpdatedAtUtc = nowUtc;
        Revision++;
    }
}
