using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Creation;

/// <summary>Exercises the actual handler, policies, repositories, SQL workload query and saved-ticket projection.</summary>
public sealed class TicketCreationIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new();
    private readonly SupportDbContext _db;
    private readonly TicketQueries _queries;
    private readonly RaiseTicketCommandHandler _handler;
    private readonly SlaPolicy _slaPolicy;

    public TicketCreationIntegrationTests()
    {
        _db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(_connection).Options);
        _queries = new TicketQueries(_db, _clock);
        _slaPolicy = new SlaPolicy(new SlaSettings
        {
            BaseWindowHours = new()
            {
                [TicketPriority.Low] = 72, [TicketPriority.Medium] = 24,
                [TicketPriority.High] = 8, [TicketPriority.Critical] = 4
            },
            PremiumCustomerMultiplier = 0.5, MinimumWindowHours = 1, AtRiskThresholdPercent = 25
        });
        _handler = new RaiseTicketCommandHandler(new TicketRepository(_db), new CustomerRepository(_db),
            new CategoryRepository(_db), new AgentAssignmentQueries(_db), _slaPolicy, _db, _clock,
            new GetTicketQueryHandler(_queries), NullLogger<RaiseTicketCommandHandler>.Instance);
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await _db.Database.EnsureCreatedAsync();
        await new SupportDbSeeder(_db, _clock, _slaPolicy, NullLogger<SupportDbSeeder>.Instance).SeedAsync();
        _db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData(3, 4, TicketPriority.Medium, TicketPriority.Critical, 2, 2)]
    [InlineData(1, 5, TicketPriority.Low, TicketPriority.Critical, 2, 3)]
    [InlineData(2, 2, TicketPriority.High, TicketPriority.High, 8, 4)]
    [InlineData(5, 3, null, TicketPriority.Medium, 24, 4)]
    [InlineData(6, 1, TicketPriority.Low, TicketPriority.Low, 36, 4)]
    public async Task Create_MatchesPdfExamples_AndPersistsTheDecisions(int customerId, int categoryId,
        TicketPriority? requested, TicketPriority expected, double hours, int agentId)
    {
        var result = await _handler.HandleAsync(Request(customerId, categoryId, requested), default);

        Assert.True(result.Id > 0);
        Assert.Equal("TCK-0041", result.Reference);
        Assert.Equal(expected, result.Priority);
        Assert.Equal(_clock.UtcNow.AddHours(hours), result.DueAtUtc);
        Assert.Equal(agentId, result.AssignedAgent?.Id);
        Assert.Equal(SlaStatus.WithinSla, result.SlaStatus);
        Assert.Equal(TicketStatus.New, result.Status);
        var triage = Assert.IsType<TriageResultDto>(result.Triage);
        Assert.Equal(expected, triage.AppliedPriority);
        Assert.Equal(hours, triage.SlaWindowHours);
        Assert.False(string.IsNullOrWhiteSpace(triage.PriorityReason));
        Assert.False(string.IsNullOrWhiteSpace(triage.SlaReason));
        Assert.False(string.IsNullOrWhiteSpace(triage.AssignmentReason));
        Assert.Equal(41, await _db.Set<Ticket>().CountAsync());

        // Force a fresh read rather than accepting tracked entities or assembled response values.
        _db.ChangeTracker.Clear();
        var stored = await _db.Set<Ticket>().AsNoTracking().SingleAsync(ticket => ticket.Id == result.Id);
        Assert.Equal(expected, stored.Priority);
        Assert.Equal(result.DueAtUtc, stored.DueAtUtc);
        Assert.Equal(agentId, stored.AssignedAgentId);
        Assert.Equal(_clock.UtcNow, stored.CreatedAtUtc);
        var reloaded = await _queries.GetDetailAsync(result.Id, default);
        Assert.NotNull(reloaded);
        Assert.Equal(result.Priority, reloaded.Priority);
        Assert.Equal(result.DueAtUtc, reloaded.DueAtUtc);
        Assert.Equal(agentId, reloaded.AssignedAgent?.Id);
        Assert.Null(reloaded.Triage);
    }

    [Fact]
    public async Task Create_WhenNobodyIsEligible_PersistsAnUnassignedTicketWithAnExplanation()
    {
        foreach (var agent in await _db.Set<Agent>().ToListAsync())
        {
            agent.Deactivate();
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _handler.HandleAsync(Request(1, 5, TicketPriority.Low), default);

        Assert.Null(result.AssignedAgent);
        Assert.Equal(TicketPriority.Critical, result.Priority);
        Assert.Equal(_clock.UtcNow.AddHours(2), result.DueAtUtc);
        Assert.Equal("No available agent specialised in Security", result.Triage?.AssignmentReason);
        _db.ChangeTracker.Clear();
        Assert.Null((await _db.Set<Ticket>().SingleAsync(ticket => ticket.Id == result.Id)).AssignedAgentId);
        Assert.Equal(41, await _db.Set<Ticket>().CountAsync());
    }

    [Theory]
    [InlineData(999, 1)]
    [InlineData(1, 999)]
    public async Task Create_WithMissingReferences_DoesNotInsertATicket(int customerId, int categoryId)
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(Request(customerId, categoryId, TicketPriority.High), default));

        Assert.Equal(40, await _db.Set<Ticket>().CountAsync());
    }

    [Fact]
    public async Task Create_UsesFreshWorkloadsForTheNextTicket()
    {
        var first = await _handler.HandleAsync(Request(2, 1, TicketPriority.High), default);
        var second = await _handler.HandleAsync(Request(2, 1, TicketPriority.High), default);

        Assert.Equal(4, first.AssignedAgent?.Id); // Sara: 2 -> 3 open
        Assert.Equal(2, second.AssignedAgent?.Id); // Ben and Sara tie at 3; Ben has lower ID
        Assert.Equal("TCK-0041", first.Reference);
        Assert.Equal("TCK-0042", second.Reference);
    }

    [Fact]
    public async Task Create_ResponseSerializesTheSpecifiedTriageShape()
    {
        var result = await _handler.HandleAsync(Request(1, 5, TicketPriority.Low), default);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result, options));

        var root = document.RootElement;
        Assert.Equal("Critical", root.GetProperty("priority").GetString());
        Assert.Equal("WithinSla", root.GetProperty("slaStatus").GetString());
        Assert.Equal("Priya Nair", root.GetProperty("assignedAgent").GetProperty("fullName").GetString());
        var triage = root.GetProperty("triage");
        Assert.Equal("Critical", triage.GetProperty("appliedPriority").GetString());
        Assert.Equal(2, triage.GetProperty("slaWindowHours").GetDouble());
        Assert.Contains("Security", triage.GetProperty("priorityReason").GetString());
        Assert.Contains("Premium", triage.GetProperty("slaReason").GetString());
        Assert.Contains("Security specialists", triage.GetProperty("assignmentReason").GetString());
    }

    private static CreateTicketRequest Request(int customerId, int categoryId, TicketPriority? priority) =>
        new("An integration test ticket", "A description for a customer support problem.", customerId, categoryId, priority);

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
