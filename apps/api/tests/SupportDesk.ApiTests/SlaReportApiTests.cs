using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Infrastructure.Data;
using Xunit;

namespace SupportDesk.ApiTests;

public sealed class SlaReportApiTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<SupportDbSeeder>().SeedAsync();
    }

    [Fact]
    public async Task ReportIsProtectedByFallbackPolicy()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/reports/sla-summary")).StatusCode);
        await Login();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var rows = await _client.GetFromJsonAsync<SlaSummaryDto[]>("/api/reports/sla-summary?groupBy=priority", options);
        Assert.NotNull(rows);
        Assert.Equal(40, rows.Sum(r => r.TotalTickets));
        Assert.All(rows, row => Assert.Equal(row.TotalTickets,
            row.Breached + row.AtRisk + row.WithinSla + row.Met + row.NotApplicable));
    }

    [Theory]
    [InlineData("groupBy=customer")]
    [InlineData("groupBy=")]
    [InlineData("from=not-a-date")]
    [InlineData("from=2026-10-17&to=2026-10-16")]
    [InlineData("from=2026-10-17&to=2026-10-17")]
    public async Task InvalidParametersReturn400(string query)
    {
        await Login();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/reports/sla-summary?" + query)).StatusCode);
    }

    [Fact]
    public async Task EmptyRangeReturns200AndEmptyArray()
    {
        await Login();
        var result = await _client.GetFromJsonAsync<SlaSummaryDto[]>("/api/reports/sla-summary?from=9999-01-01T00:00:00Z");
        Assert.Empty(result!);
    }

    private async Task Login()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(SupportDbSeeder.AlexDevelopmentEmail, SupportDbSeeder.AlexDevelopmentPassword));
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
