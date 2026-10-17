using SupportDesk.Application.Contracts.Auth;

namespace SupportDesk.Application.Abstractions;

public interface IAgentAuthenticationService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthenticatedAgentDto?> GetActiveAgentAsync(int agentId, CancellationToken ct);
}
