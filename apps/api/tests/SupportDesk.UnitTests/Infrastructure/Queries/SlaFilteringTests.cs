using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class SlaFilteringTests
{
    [Theory]
    [InlineData(SlaStatus.NotApplicable, 1)]
    [InlineData(SlaStatus.WithinSla, 1)]
    [InlineData(SlaStatus.AtRisk, 2)]
    [InlineData(SlaStatus.Breached, 2)]
    [InlineData(SlaStatus.Met, 1)]
    public async Task Filter_UsesTheSameRuleAsTheReturnedStatus(SlaStatus status, int count)
    {
        await using var desk = await Fixture();
        var result = await new TicketQueries(desk.Db, desk.Clock).GetPagedAsync(new TicketQuery { SlaStatus = status }, default);
        Assert.Equal(count, result.TotalCount);
        Assert.Equal(count, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(status, item.SlaStatus));
    }

    [Fact]
    public async Task Filter_CountAndPagingRunInSql_AndOtherFiltersStillCombine()
    {
        await using var desk = await Fixture();
        var logs = new List<string>();
        var options = new DbContextOptionsBuilder<SupportDbContext>(desk.Options)
            .LogTo(logs.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information).Options;
        await using var db = new SupportDbContext(options);
        var queries = new TicketQueries(db, desk.Clock);
        var result = await queries.GetPagedAsync(new TicketQuery
        {
            SlaStatus = SlaStatus.Breached, Page = 2, PageSize = 1, SortBy = "dueAtUtc", SortDirection = "asc"
        }, default);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal("TCK-0004", Assert.Single(result.Items).Reference);
        var count = Assert.Single(logs, sql => sql.Contains("COUNT(*)"));
        var page = Assert.Single(logs, sql => sql.Contains("LIMIT"));
        Assert.Contains("CASE", count);
        Assert.Contains("WHERE", count);
        Assert.Contains("CASE", page);
        Assert.True(page.IndexOf("WHERE", StringComparison.Ordinal) < page.IndexOf("LIMIT", StringComparison.Ordinal));
        var empty = await queries.GetPagedAsync(new TicketQuery { SlaStatus = SlaStatus.AtRisk, Priority = TicketPriority.Low }, default);
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task SortBySlaStatus_IsDatabaseSideAndStable()
    {
        await using var desk = await Fixture();
        var result = await new TicketQueries(desk.Db, desk.Clock).GetPagedAsync(new TicketQuery
        { SortBy = "slaStatus", SortDirection = "asc" }, default);
        Assert.Equal(7, result.TotalCount);
        Assert.Equal(result.Items.Select(item => item.SlaStatus).Order(), result.Items.Select(item => item.SlaStatus));
    }

    private static async Task<SqliteDesk> Fixture()
    {
        var desk = await SqliteDesk.CreateAsync(seed: false);
        var now = desk.Clock.UtcNow;
        var customer = new Customer("Customer", "help@example.test", null, CustomerTier.Standard, now);
        var category = new Category("General", false, false);
        desk.Db.AddRange(customer, category);
        await desk.Db.SaveChangesAsync();
        for (var index = 1; index <= 7; index++)
        {
            var hoursAgo = index switch { 2 => 1, 3 => 6, 4 => 9, 5 or 6 => 10, _ => 8 };
            var start = now.AddHours(-hoursAgo);
            var ticket = index == 1
                ? new TicketBuilder().WithReference($"TCK-{index:0000}").Build()
                : Ticket.RaiseTriaged($"TCK-{index:0000}", "A test ticket", "A test description", customer.Id, category.Id,
                    new PriorityDecision(TicketPriority.High, "Test priority"),
                    desk.Policy.Calculate(TicketPriority.High, CustomerTier.Standard, start),
                    new AssignmentDecision(null, "Test unassigned"), start);
            if (index is 5 or 6) ticket.ChangeStatus(TicketStatus.Resolved, now.AddHours(index == 5 ? -3 : -1));
            desk.Db.Add(ticket);
        }
        await desk.Db.SaveChangesAsync();
        desk.Db.ChangeTracker.Clear();
        return desk;
    }
}
