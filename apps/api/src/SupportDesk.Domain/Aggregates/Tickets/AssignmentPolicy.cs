using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// A read-only workload snapshot. IsSpecialistForCategory is evaluated for the category
/// requested by the caller; do not reuse this snapshot for a different category.
/// </summary>
public sealed record AssignmentCandidate(
    int Id,
    string FullName,
    bool IsActive,
    int MaxOpenTickets,
    int OpenTicketCount,
    bool IsSpecialistForCategory);

public sealed record AssignmentDecision(AssignmentCandidate? Agent, string Reason);

/// <summary>Shared eligibility and deterministic selection for automatic assignment.</summary>
public static class AssignmentPolicy
{
    public static bool IsEligible(Category category, AssignmentCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate.IsActive
            && candidate.OpenTicketCount < candidate.MaxOpenTickets
            && (!category.RequiresSpecialist || candidate.IsSpecialistForCategory);
    }

    public static AssignmentDecision Select(Category category, IEnumerable<AssignmentCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(candidates);

        var agent = candidates
            .Where(candidate => IsEligible(category, candidate))
            .OrderBy(candidate => candidate.OpenTicketCount)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();

        if (agent is null)
        {
            return new AssignmentDecision(null, category.RequiresSpecialist
                ? $"No available agent specialised in {category.Name}"
                : "No active agent below their open-ticket limit");
        }

        return new AssignmentDecision(agent, category.RequiresSpecialist
            ? $"Fewest open tickets among {category.Name} specialists"
            : "Fewest open tickets among available agents");
    }
}
