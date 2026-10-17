using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Authentication;

public sealed class AgentAuthenticationService(
    SupportDbContext db,
    IPasswordHasher<Agent> passwordHasher,
    JwtSettings settings,
    IClock clock) : IAgentAuthenticationService
{
    // Unknown users still perform password verification, reducing the obvious timing difference.
    private static readonly Agent DummyAgent = new("Unknown", "unknown@example.invalid", 1, DateTime.UnixEpoch);
    private static readonly string DummyHash = new PasswordHasher<Agent>()
        .HashPassword(DummyAgent, Guid.NewGuid().ToString());

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToUpperInvariant();
        var agent = await db.Set<Agent>().SingleOrDefaultAsync(a => a.Email.ToUpper() == email, ct);
        var verification = passwordHasher.VerifyHashedPassword(
            agent ?? DummyAgent, agent?.PasswordHash ?? DummyHash, request.Password);

        if (agent is null || !agent.IsActive || agent.PasswordHash is null
            || verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            agent.SetPasswordHash(passwordHasher.HashPassword(agent, request.Password));
            await db.SaveChangesAsync(ct);
        }

        var now = clock.UtcNow;
        // JWT dates have second precision; return exactly the expiry encoded in the token.
        var expires = DateTimeOffset.FromUnixTimeSeconds(
            new DateTimeOffset(now.AddMinutes(settings.LifetimeMinutes)).ToUnixTimeSeconds()).UtcDateTime;
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new(JwtRegisteredClaimNames.Sub, agent.Id.ToString(CultureInfo.InvariantCulture)),
                new("name", agent.FullName),
                new(JwtRegisteredClaimNames.Email, agent.Email),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ],
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256));

        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires,
            new AuthenticatedAgentDto(agent.Id, agent.FullName, agent.Email));
    }

    public Task<AuthenticatedAgentDto?> GetActiveAgentAsync(int agentId, CancellationToken ct) =>
        db.Set<Agent>().AsNoTracking()
            .Where(agent => agent.Id == agentId && agent.IsActive)
            .Select(agent => new AuthenticatedAgentDto(agent.Id, agent.FullName, agent.Email))
            .SingleOrDefaultAsync(ct);
}
