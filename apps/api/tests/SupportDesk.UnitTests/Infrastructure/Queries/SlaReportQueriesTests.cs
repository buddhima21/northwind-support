using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Application.Features.Reports.Queries;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class SlaReportQueriesTests
{
    [Fact]
    public async Task AgentGroups_IncludeUnassignedAndAllSlaStates_WithCorrectRatesAndNullableAverages()
    {
        await using var desk = await Fixture();
        var result = await new SlaReportQueries(desk.Db, desk.Clock).GetSummaryAsync(new(), default);
        Assert.Equal(3, result.Count);
        var unassigned = Assert.Single(result, r => r.AgentId == null);
        Assert.Null(unassigned.AgentName);
        Assert.Equal(1, unassigned.WithinSla);
        Assert.Null(unassigned.AverageResolutionHours);
        var alex = Assert.Single(result, r => r.AgentName == "Alex");
        Assert.Null(alex.Priority);
        Assert.Equal(6, alex.TotalTickets);
        Assert.Equal((2, 1, 1, 1, 1), (alex.Breached, alex.AtRisk, alex.WithinSla, alex.Met, alex.NotApplicable));
        Assert.Equal(2.0 / 6, alex.BreachRate, 6);
        Assert.Equal((1.5 + 73 + 0.25) / 3, alex.AverageResolutionHours!.Value, 5);
        // More than 24 days, across month boundaries, including fractional hours.
        var ben = Assert.Single(result, r => r.AgentName == "Ben");
        Assert.Equal(720.125, ben.AverageResolutionHours!.Value, 5);
    }

    [Fact]
    public async Task PriorityGrouping_CountsEachTicketOnce_AndReturnsNullAverageForUnresolvedGroup()
    {
        await using var desk = await Fixture();
        var result = await new SlaReportQueries(desk.Db, desk.Clock)
            .GetSummaryAsync(new() { GroupBy = "PRIORITY" }, default);
        Assert.Equal(4, result.Count);
        Assert.Equal(8, result.Sum(r => r.TotalTickets));
        Assert.Equal(result.Select(r => r.Priority).Order(), result.Select(r => r.Priority));
        Assert.All(result, r => { Assert.Null(r.AgentId); Assert.Null(r.AgentName); });
        var high = Assert.Single(result, r => r.Priority == TicketPriority.High);
        Assert.Equal(3, high.TotalTickets);
        Assert.Equal((1, 1, 1), (high.Breached, high.AtRisk, high.WithinSla));
        Assert.Null(high.AverageResolutionHours);
        Assert.Equal(1.0 / 3, high.BreachRate, 6);
    }

    [Fact]
    public async Task CreationDateRange_IsInclusiveFromExclusiveTo_AndNormalizesOffsets()
    {
        await using var desk = await Fixture();
        var query = new SlaSummaryQuery
        {
            From = new DateTimeOffset(desk.Clock.UtcNow.AddHours(-6)).ToOffset(TimeSpan.FromHours(5.5)),
            To = new DateTimeOffset(desk.Clock.UtcNow.AddHours(-1)).ToOffset(TimeSpan.FromHours(5.5))
        };
        var result = await new SlaReportQueries(desk.Db, desk.Clock).GetSummaryAsync(query, default);
        var group = Assert.Single(result);
        Assert.Equal(2, group.TotalTickets);
        Assert.Equal(1, group.AtRisk);
        Assert.Equal(1, group.Met);
    }

    [Fact]
    public async Task EmptyRange_ReturnsNoGroups()
    {
        await using var desk = await Fixture();
        var result = await new SlaReportQueries(desk.Db, desk.Clock)
            .GetSummaryAsync(new() { From = desk.Clock.UtcNow.AddDays(1) }, default);
        Assert.Empty(result);
    }

    [Fact]
    public async Task StatusUsesInjectedClock_AndSharedDeadlineBoundary()
    {
        await using var desk = await Fixture();
        desk.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromTicks(1));
        var result = await new SlaReportQueries(desk.Db, desk.Clock).GetSummaryAsync(new(), default);
        var alex = Assert.Single(result, r => r.AgentName == "Alex");
        Assert.Equal(3, alex.Breached);
        Assert.Equal(0, alex.AtRisk);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("priority")]
    public async Task ExecutesOneDatabaseAggregation_WithDateFilterCountsAndAverage(string groupBy)
    {
        await using var desk = await Fixture();
        var logs = new List<string>();
        var options = new DbContextOptionsBuilder<SupportDbContext>(desk.Options)
            .LogTo(logs.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information).Options;
        await using var db = new SupportDbContext(options);
        await new SlaReportQueries(db, desk.Clock).GetSummaryAsync(new()
        { GroupBy = groupBy, From = desk.Clock.UtcNow.AddDays(-100), To = desk.Clock.UtcNow }, default);
        var sql = Assert.Single(logs, line => line.Contains("Executed DbCommand"));
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("COUNT", sql);
        Assert.Contains("AVG", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains("WHERE", sql);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Validator_AcceptsOptionalBoundsAndRejectsInvalidGroupingOrRange()
    {
        var validator = new SlaSummaryQueryValidator();
        Assert.True(validator.Validate(new SlaSummaryQuery()).IsValid);
        Assert.False(validator.Validate(new SlaSummaryQuery() { GroupBy = "customer" }).IsValid);
        Assert.False(validator.Validate(new SlaSummaryQuery() { GroupBy = "" }).IsValid);
        Assert.False(validator.Validate(new SlaSummaryQuery() { From = DateTimeOffset.UtcNow, To = DateTimeOffset.MinValue }).IsValid);
        Assert.False(validator.Validate(new SlaSummaryQuery() { From = DateTimeOffset.MinValue, To = DateTimeOffset.MinValue }).IsValid);
    }

    private static async Task<SqliteDesk> Fixture()
    {
        var desk = await SqliteDesk.CreateAsync(seed: false);
        var now = desk.Clock.UtcNow;
        var customer = new Customer("Customer", "help@example.test", null, CustomerTier.Standard, now);
        var category = new Category("General", false, false);
        var alex = new Agent("Alex", "alex@example.test", 10, now);
        var ben = new Agent("Ben", "ben@example.test", 10, now);
        desk.Db.AddRange(customer, category, alex, ben);
        await desk.Db.SaveChangesAsync();
        Add("ONE", TicketPriority.High, 1, alex.Id);
        Add("TWO", TicketPriority.High, 6, alex.Id);
        Add("THREE", TicketPriority.High, 9, alex.Id);
        Add("FOUR", TicketPriority.Low, 2, alex.Id, resolutionHours: 1.5);
        Add("FIVE", TicketPriority.Low, 80, alex.Id, resolutionHours: 73);
        Add("SIX", TicketPriority.Low, 10, alex.Id, noSla: true, resolutionHours: 0.25);
        Add("SEVEN", TicketPriority.Critical, 1, null);
        Add("EIGHT", TicketPriority.Medium, 40 * 24, ben.Id, resolutionHours: 720.125);
        await desk.Db.SaveChangesAsync();
        desk.Db.ChangeTracker.Clear();
        return desk;

        void Add(string reference, TicketPriority priority, double hoursAgo, int? agentId,
            bool noSla = false, double? resolutionHours = null)
        {
            var start = now.AddHours(-hoursAgo);
            var ticket = noSla
                ? Ticket.Raise(reference, "A test ticket", "A test description", customer.Id, category.Id, priority, start)
                : Ticket.RaiseTriaged(reference, "A test ticket", "A test description", customer.Id, category.Id,
                    new PriorityDecision(priority, "Test"), desk.Policy.Calculate(priority, customer.Tier, start),
                    new AssignmentDecision(null, "Test"), start);
            ticket.AssignTo(agentId, start);
            if (resolutionHours is { } hours) ticket.ChangeStatus(TicketStatus.Resolved, start.AddHours(hours));
            desk.Db.Add(ticket);
        }
    }
}

