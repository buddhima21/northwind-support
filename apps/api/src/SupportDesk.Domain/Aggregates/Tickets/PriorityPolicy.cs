using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Aggregates.Tickets;

public sealed record PriorityDecision(TicketPriority AppliedPriority, string Reason);

/// <summary>Uses category flags, never category names, to determine a ticket's priority.</summary>
public static class PriorityPolicy
{
    public static PriorityDecision Determine(TicketPriority? requestedPriority, Category category)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (requestedPriority is { } requested && !Enum.IsDefined(requested))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPriority));
        }

        if (category.ForcesCriticalPriority)
        {
            return new PriorityDecision(TicketPriority.Critical,
                $"Category '{category.Name}' is always treated as Critical");
        }

        return requestedPriority is { } priority
            ? new PriorityDecision(priority, $"Requested priority {priority} was respected")
            : new PriorityDecision(TicketPriority.Medium, "No priority requested; defaulted to Medium");
    }
}
