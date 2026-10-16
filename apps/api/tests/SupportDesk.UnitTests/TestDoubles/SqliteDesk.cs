using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Repositories;

namespace SupportDesk.UnitTests.TestDoubles;

internal sealed class SqliteDesk : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public FixedClock Clock { get; } = new();
    public SupportDbContext Db { get; }
    public SlaPolicy Policy { get; } = DefaultPolicy();
    public DbContextOptions<SupportDbContext> Options { get; }

    private SqliteDesk()
    {
        Options = new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(_connection).Options;
        Db = new SupportDbContext(Options);
    }

    public static SlaPolicy DefaultPolicy() => new(new SlaSettings
    {
        BaseWindowHours = new()
        {
            [TicketPriority.Low] = 72, [TicketPriority.Medium] = 24,
            [TicketPriority.High] = 8, [TicketPriority.Critical] = 4
        },
        PremiumCustomerMultiplier = 0.5, MinimumWindowHours = 1, AtRiskThresholdPercent = 25
    });

    public static async Task<SqliteDesk> CreateAsync(bool seed = true)
    {
        var desk = new SqliteDesk();
        await desk._connection.OpenAsync();
        await desk.Db.Database.EnsureCreatedAsync();
        if (seed)
            await new SupportDbSeeder(desk.Db, desk.Clock, desk.Policy, NullLogger<SupportDbSeeder>.Instance).SeedAsync();
        desk.Db.ChangeTracker.Clear();
        return desk;
    }

    public RaiseTicketCommandHandler CreateHandler() => new(new TicketRepository(Db),
        new CustomerRepository(Db), new CategoryRepository(Db), new AgentAssignmentQueries(Db), Policy,
        Db, Clock, new GetTicketQueryHandler(new TicketQueries(Db, Clock)), NullLogger<RaiseTicketCommandHandler>.Instance);

    public EscalateTicketCommandHandler EscalateHandler() => new(new TicketRepository(Db),
        new CustomerRepository(Db), new CategoryRepository(Db), new AgentAssignmentQueries(Db),
        new TicketEscalationQueries(Db), Policy, Db, Clock, new GetTicketQueryHandler(new TicketQueries(Db, Clock)));

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
