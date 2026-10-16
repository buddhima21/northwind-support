using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Abstractions;

/// <summary>Loads current workloads and specialization matches for automatic assignment.</summary>
public interface IAgentAssignmentQueries
{
    Task<IReadOnlyList<AssignmentCandidate>> GetCandidatesAsync(int categoryId, CancellationToken ct);
}
