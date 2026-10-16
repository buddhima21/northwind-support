using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly List<string> _sql = [];
    private readonly SupportDbContext _db;
    private readonly TicketQueries _queries;
    private readonly Customer _contoso;
    private readonly Customer _fabrikam;
    private readonly Category _general;
    private readonly Category _billing;
    private readonly Agent _alex;
    private readonly Agent _sara;

    public TicketQueriesTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .LogTo(_sql.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information)
            .Options;
        _db = new SupportDbContext(options);
        _db.Database.EnsureCreated();
        var now = FixedClock.DefaultNow;
        _contoso = new Customer("Contoso", "help@contoso.test", null, CustomerTier.Premium, now);
        _fabrikam = new Customer("Fabrikam", "help@fabrikam.test", null, CustomerTier.Standard, now);
        _general = new Category("General", false, false);
        _billing = new Category("Billing", true, false);
        _alex = new Agent("Alex", "alex@example.test", 10, now);
        _sara = new Agent("Sara", "sara@example.test", 6, now);
        _db.AddRange(_contoso, _fabrikam, _general, _billing, _alex, _sara);
        _db.SaveChanges();
        AddTicket(1, "Printer offline", TicketPriority.High, TicketStatus.New, _contoso, _general, _alex);
        AddTicket(2, "Invoice incorrect", TicketPriority.High, TicketStatus.InProgress, _contoso, _billing, null);
        AddTicket(3, "Network outage", TicketPriority.Critical, TicketStatus.New, _fabrikam, _general, _sara);
        AddTicket(4, "How to export", TicketPriority.Low, TicketStatus.New, _fabrikam, _billing, null);
        AddTicket(5, "Login issue", TicketPriority.Medium, TicketStatus.Resolved, _contoso, _general, _alex);
        AddTicket(6, "Printer queue", TicketPriority.High, TicketStatus.New, _contoso, _general, _sara);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _sql.Clear();
        _queries = new TicketQueries(_db, new FixedClock());
    }

    private void AddTicket(int number, string title, TicketPriority priority, TicketStatus status,
        Customer customer, Category category, Agent? agent)
    {
        var now = FixedClock.DefaultNow.AddMinutes(number);
        var ticket = Ticket.Raise($"TCK-{number:0000}", title, "A customer needs assistance.",
            customer.Id, category.Id, priority, now);
        ticket.ChangeStatus(status, now);
        ticket.AssignTo(agent?.Id, now);
        _db.Add(ticket);
    }

    [Theory]
    [InlineData(TicketPriority.High, 3)]
    [InlineData(TicketPriority.Low, 1)]
    [InlineData(TicketPriority.Medium, 1)]
    [InlineData(TicketPriority.Critical, 1)]
    public async Task Priority_IsAnExactMatch(TicketPriority priority, int expectedCount)
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { Priority = priority }, default);
        Assert.Equal(expectedCount, result.TotalCount);
        Assert.Equal(expectedCount, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(priority, item.Priority));
    }

    [Fact]
    public async Task Status_FiltersTickets()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { Status = TicketStatus.InProgress }, default);
        Assert.Equal("TCK-0002", Assert.Single(result.Items).Reference);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task Category_FiltersTickets()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { CategoryId = _billing.Id }, default);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(_billing.Id, item.Category.Id));
    }

    [Fact]
    public async Task Customer_FiltersTickets()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { CustomerId = _fabrikam.Id }, default);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(_fabrikam.Id, item.Customer.Id));
    }

    [Fact]
    public async Task AssignedAgent_FiltersTickets()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { AssignedAgentId = _alex.Id }, default);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(_alex.Id, item.AssignedAgent!.Id));
    }

    [Fact]
    public async Task UnassignedOnly_ReturnsOnlyTicketsWithoutAnAgent()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { UnassignedOnly = true }, default);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Null(item.AssignedAgent));
    }

    [Fact]
    public async Task Filters_CombineWithAnd()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery
        {
            Priority = TicketPriority.High,
            Status = TicketStatus.New,
            CategoryId = _general.Id,
            CustomerId = _contoso.Id,
            AssignedAgentId = _alex.Id,
            Search = "Printer"
        }, default);
        Assert.Equal("TCK-0001", Assert.Single(result.Items).Reference);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task AgentAndUnassignedOnly_AreContradictory()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery
        {
            AssignedAgentId = _alex.Id, UnassignedOnly = true
        }, default);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Theory]
    [InlineData("  Contoso  ", 4)]
    [InlineData("Printer", 2)]
    [InlineData("TCK-0003", 1)]
    [InlineData("nothing matches", 0)]
    [InlineData("   ", 6)]
    [InlineData("", 6)]
    public async Task Search_MatchesTitleReferenceOrCustomer_AndIgnoresBlank(string search, int expectedCount)
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { Search = search }, default);
        Assert.Equal(expectedCount, result.TotalCount);
        Assert.Equal(expectedCount, result.Items.Count);
    }

    [Fact]
    public async Task Filtering_HappensBeforeCountingSortingAndPaging_InSql()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery
        {
            Priority = TicketPriority.High, Page = 2, PageSize = 1,
            SortBy = "createdAtUtc", SortDirection = "asc"
        }, default);
        Assert.Equal("TCK-0002", Assert.Single(result.Items).Reference);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        var countSql = Assert.Single(_sql, sql => sql.Contains("SELECT COUNT(*)"));
        Assert.Contains("WHERE", countSql);
        Assert.Contains("\"Priority\" =", countSql);
        var pageSql = Assert.Single(_sql, sql => sql.Contains("LIMIT"));
        Assert.Contains("\"Priority\" =", pageSql);
        Assert.True(pageSql.IndexOf("WHERE", StringComparison.Ordinal) < pageSql.IndexOf("LIMIT", StringComparison.Ordinal));
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task PagePastTheEnd_KeepsTheFilteredCount()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery
        {
            Priority = TicketPriority.High, Page = 4, PageSize = 1
        }, default);
        Assert.Empty(result.Items);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
