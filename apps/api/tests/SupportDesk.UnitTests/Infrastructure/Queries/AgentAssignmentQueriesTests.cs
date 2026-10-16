using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class AgentAssignmentQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly List<string> _sql = [];
    private readonly SupportDbContext _db;
    private readonly AgentAssignmentQueries _queries;
    private readonly Agent _alex;
    private readonly Agent _sara;
    private readonly Agent _yuki;
    private readonly Category _general;
    private readonly Category _billing;

    public AgentAssignmentQueriesTests()
    {
        _connection.Open();
        _db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .LogTo(_sql.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information)
            .Options);
        _db.Database.EnsureCreated();
        var clock = new FixedClock();
        var customer = new Customer("Contoso", "support@example.test", null, CustomerTier.Standard, clock.UtcNow);
        _general = new Category("General", false, false);
        _billing = new Category("Billing", true, false);
        _alex = new Agent("Alex", "alex@example.test", 3, clock.UtcNow);
        _sara = new Agent("Sara", "sara@example.test", 6, clock.UtcNow);
        _yuki = new Agent("Yuki", "yuki@example.test", 8, clock.UtcNow);
        _yuki.Deactivate();
        _db.AddRange(customer, _general, _billing, _alex, _sara, _yuki);
        _db.SaveChanges();
        _alex.AddSpecialization(_general.Id);
        _sara.AddSpecialization(_billing.Id);
        _yuki.AddSpecialization(_general.Id);
        _db.SaveChanges();

        // TicketBuilder's default customer/category are the first rows in this fresh database.
        Assert.Equal(1, customer.Id);
        Assert.Equal(1, _general.Id);
        var number = 1;
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            _db.Add(new TicketBuilder().WithReference($"TCK-{number++:0000}")
                .WithStatus(status).AssignedTo(_alex.Id).Build());
        }
        _db.Add(new TicketBuilder().WithReference($"TCK-{number++:0000}").AssignedTo(_sara.Id).Build());
        _db.Add(new TicketBuilder().WithReference($"TCK-{number++:0000}").WithStatus(TicketStatus.Resolved).AssignedTo(_sara.Id).Build());
        _db.Add(new TicketBuilder().WithReference($"TCK-{number:0000}").Build());
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _sql.Clear();
        _queries = new AgentAssignmentQueries(_db);
    }

    [Fact]
    public async Task GetCandidates_CountsOnlyAssignedNonTerminalTickets()
    {
        var candidates = await _queries.GetCandidatesAsync(_general.Id, default);

        Assert.Equal(3, candidates.Count);
        var alex = Assert.Single(candidates, candidate => candidate.Id == _alex.Id);
        Assert.Equal(3, alex.OpenTicketCount);
        Assert.Equal(3, alex.MaxOpenTickets);
        Assert.Equal("Alex", alex.FullName);
        Assert.Equal(1, Assert.Single(candidates, candidate => candidate.Id == _sara.Id).OpenTicketCount);
        var yuki = Assert.Single(candidates, candidate => candidate.Id == _yuki.Id);
        Assert.Equal(0, yuki.OpenTicketCount);
        Assert.False(yuki.IsActive);
    }

    [Fact]
    public async Task GetCandidates_ChecksSpecializationForTheRequestedCategory()
    {
        var general = await _queries.GetCandidatesAsync(_general.Id, default);
        var billing = await _queries.GetCandidatesAsync(_billing.Id, default);

        Assert.True(Assert.Single(general, candidate => candidate.Id == _alex.Id).IsSpecialistForCategory);
        Assert.False(Assert.Single(general, candidate => candidate.Id == _sara.Id).IsSpecialistForCategory);
        Assert.False(Assert.Single(billing, candidate => candidate.Id == _alex.Id).IsSpecialistForCategory);
        Assert.True(Assert.Single(billing, candidate => candidate.Id == _sara.Id).IsSpecialistForCategory);
    }

    [Fact]
    public async Task GetCandidates_UsesOneUntrackedSqlQueryForWorkloadAndSpecialization()
    {
        await _queries.GetCandidatesAsync(_billing.Id, default);

        var command = Assert.Single(_sql);
        Assert.Contains("COUNT(*)", command);
        Assert.Contains("EXISTS", command);
        Assert.Contains("\"Status\"", command);
        Assert.Contains("\"AssignedAgentId\"", command);
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetCandidates_ProvidesRealWorkloadsForTheSelectionPolicy()
    {
        var candidates = await _queries.GetCandidatesAsync(_general.Id, default);

        var result = AssignmentPolicy.Select(_general, candidates);

        Assert.Equal(_sara.Id, result.Agent?.Id);
        Assert.False(AssignmentPolicy.IsEligible(_general, Assert.Single(candidates, candidate => candidate.Id == _alex.Id)));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
