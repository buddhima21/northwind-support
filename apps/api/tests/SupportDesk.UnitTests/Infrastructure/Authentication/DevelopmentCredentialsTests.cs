using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Authentication;

public sealed class DevelopmentCredentialsTests
{
    private readonly PasswordHasher<Agent> _hasher = new();

    [Fact]
    public async Task FreshSeed_PersistsDistinctSaltedHashesForAllSixAgents()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var agents = await desk.Db.Set<Agent>().AsNoTracking().ToListAsync();

        Assert.Equal(6, agents.Count);
        Assert.Equal(6, agents.Select(agent => agent.PasswordHash).Distinct().Count());
        foreach (var agent in agents)
        {
            Assert.NotNull(agent.PasswordHash);
            Assert.NotEqual(SupportDbSeeder.DevelopmentPassword, agent.PasswordHash);
            Assert.NotEqual(PasswordVerificationResult.Failed,
                _hasher.VerifyHashedPassword(agent, agent.PasswordHash, SupportDbSeeder.DevelopmentPassword));
            Assert.Equal(PasswordVerificationResult.Failed,
                _hasher.VerifyHashedPassword(agent, agent.PasswordHash, "WrongPassword!"));
        }
        Assert.False(Assert.Single(agents, agent => agent.Email.StartsWith("yuki.", StringComparison.Ordinal)).IsActive);
    }

    [Fact]
    public async Task ExistingDatabase_ProvisionsMissingDemoCredentialsWithoutResettingExistingPasswords()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var agents = await desk.Db.Set<Agent>().OrderBy(agent => agent.Id).ToListAsync();
        var preservedHash = _hasher.HashPassword(agents[0], "ChangedPassword!1");
        agents[0].SetPasswordHash(preservedHash);
        desk.Db.Entry(agents[1]).Property(agent => agent.PasswordHash).CurrentValue = null;
        var otherAgent = new Agent("Other Agent", "other@example.test", 5, desk.Clock.UtcNow);
        desk.Db.Add(otherAgent);
        await desk.Db.SaveChangesAsync();
        var ticketCount = await desk.Db.Set<Ticket>().CountAsync();

        await Seeder(desk).SeedAsync();
        desk.Db.ChangeTracker.Clear();

        var saved = await desk.Db.Set<Agent>().OrderBy(agent => agent.Id).ToListAsync();
        Assert.Equal(preservedHash, saved[0].PasswordHash);
        Assert.NotNull(saved[1].PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed,
            _hasher.VerifyHashedPassword(saved[1], saved[1].PasswordHash!, SupportDbSeeder.DevelopmentPassword));
        Assert.Null(saved[^1].PasswordHash);
        Assert.Equal(ticketCount, await desk.Db.Set<Ticket>().CountAsync());

        var hashes = saved.Select(agent => agent.PasswordHash).ToArray();
        await Seeder(desk).SeedAsync();
        desk.Db.ChangeTracker.Clear();
        Assert.Equal(hashes, await desk.Db.Set<Agent>().OrderBy(agent => agent.Id)
            .Select(agent => agent.PasswordHash).ToArrayAsync());
    }

    [Fact]
    public void Agent_RejectsBlankPasswordHashes()
    {
        var agent = new Agent("Alex", "alex@example.test", 10, FixedClock.DefaultNow);
        Assert.Throws<ArgumentException>(() => agent.SetPasswordHash(" "));
    }

    private SupportDbSeeder Seeder(SqliteDesk desk) => new(desk.Db, desk.Clock, desk.Policy,
        NullLogger<SupportDbSeeder>.Instance, _hasher);
}
