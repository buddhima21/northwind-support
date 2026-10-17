using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Authentication;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;

namespace SupportDesk.ApiTests;

public sealed class AuthApiTests : IAsyncLifetime
{
    private const string Email = "alex.turner@northwind-support.example";
    private readonly AuthApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        await db.Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<SupportDbSeeder>().SeedAsync();
    }

    [Fact]
    public async Task Login_ReturnsSignedTokenWithExpectedIdentityAndOneHourExpiry()
    {
        var result = await LoginAsync("  ALEX.TURNER@NORTHWIND-SUPPORT.EXAMPLE  ");
        Assert.Equal(Email, result.User.Email);
        Assert.Equal("Alex Turner", result.User.FullName);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Equal(result.User.Id.ToString(), token.Subject);
        Assert.Equal(result.ExpiresAtUtc, token.ValidTo);
        Assert.InRange(result.ExpiresAtUtc - _factory.Clock.UtcNow,
            TimeSpan.FromMinutes(60) - TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(60));
        Assert.DoesNotContain("Password", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        var me = await _client.GetFromJsonAsync<AuthenticatedAgentDto>("/api/auth/me");
        Assert.Equal(result.User, me);
    }

    [Fact]
    public async Task Login_UnknownEmailWrongPasswordAndInactiveAgentReturnTheSameGeneric401()
    {
        var requests = new[]
        {
            new LoginRequest("unknown@example.test", "WrongPassword!"),
            new LoginRequest(Email, "WrongPassword!"),
            new LoginRequest("yuki.tanaka@northwind-support.example", SupportDbSeeder.DevelopmentPassword)
        };
        foreach (var request in requests)
        {
            var response = await _client.PostAsJsonAsync("/api/auth/login", request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Invalid email or password.", body.GetProperty("detail").GetString());
            Assert.False(body.TryGetProperty("accessToken", out _));
        }
    }

    [Fact]
    public async Task Login_AgentWithoutCredentialsReturnsGeneric401()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var agent = await db.Set<Agent>().SingleAsync(a => a.Email == Email);
        db.Entry(agent).Property(a => a.PasswordHash).CurrentValue = null;
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Email, SupportDbSeeder.DevelopmentPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password.",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("invalid-email", "password")]
    [InlineData(Email, "")]
    [InlineData(null, null)]
    public async Task Login_InvalidFieldsReturn400(string? email, string? password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-token")]
    public async Task Me_RejectsMissingOrMalformedTokens(string? token)
    {
        if (token is not null)
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_RejectsTamperedSignature()
    {
        var result = await LoginAsync();
        var parts = result.AccessToken.Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', parts));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Me_RejectsExpiredToken()
    {
        _factory.Clock.Advance(TimeSpan.FromHours(-2));
        var result = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    public async Task Me_RejectsCorrectlySignedTokenWithWrongIssuerOrAudience(string invalidField)
    {
        var login = await LoginAsync();
        var original = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var settings = _factory.Services.GetRequiredService<JwtSettings>();
        var token = new JwtSecurityToken(
            issuer: invalidField == "issuer" ? "WrongIssuer" : settings.Issuer,
            audience: invalidField == "audience" ? "WrongAudience" : settings.Audience,
            claims: original.Claims.Where(claim => claim.Type is "sub" or "name" or "email"),
            notBefore: original.ValidFrom,
            expires: original.ValidTo,
            signingCredentials: new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Me_RejectsAgentDeactivatedAfterLogin()
    {
        var result = await LoginAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        (await db.Set<Agent>().SingleAsync(a => a.Email == Email)).Deactivate();
        await db.SaveChangesAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
    }

    private async Task<LoginResponse> LoginAsync(string email = Email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, SupportDbSeeder.DevelopmentPassword));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}

internal sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public FixedClock Clock { get; } = new(DateTime.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "NorthwindSupport.Tests",
            ["Jwt:Audience"] = "NorthwindSupport.Tests.Client",
            ["Jwt:SigningKey"] = "TestOnly_SigningKey_NotARealSecret_AtLeast32Bytes!",
            ["Jwt:LifetimeMinutes"] = "60",
            ["ConnectionStrings:Default"] = "Server=unused;Database=unused"
        }));
        builder.ConfigureServices(services =>
        {
            _connection.Open();
            services.RemoveAll<DbContextOptions<SupportDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SupportDbContext>>();
            services.AddDbContext<SupportDbContext>(options => options.UseSqlite(_connection));
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
