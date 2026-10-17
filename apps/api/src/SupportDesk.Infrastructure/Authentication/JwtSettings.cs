using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SupportDesk.Infrastructure.Authentication;

public sealed class JwtSettings
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public int LifetimeMinutes { get; init; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience)
            || string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32
            || LifetimeMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException(
                "Jwt requires an issuer, audience, a signing key of at least 32 UTF-8 bytes, and LifetimeMinutes between 1 and 1440.");
        }
    }

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(),
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "name"
    };
}
