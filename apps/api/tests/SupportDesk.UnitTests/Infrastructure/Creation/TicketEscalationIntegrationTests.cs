using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetEscalations;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Creation;

public sealed class TicketEscalationIntegrationTests
{
    private static EscalateTicketRequest Reason() => new("Customer needs urgent attention");
    private static CreateTicketRequest Create(TicketPriority priority = TicketPriority.High) =>
        new("An escalation test ticket", "Customer requires assistance with their account.", 2, 1, priority);

    [Fact]
    public async Task Escalate_KeepsAnEligibleOwnerAndSavesHistoryWithTheTicket()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create(), default);
        desk.Clock.Advance(TimeSpan.FromHours(5));

        var result = await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);

        Assert.Equal(TicketPriority.Critical, result.Priority);
        Assert.Equal(desk.Clock.UtcNow.AddHours(4), result.DueAtUtc);
        Assert.Equal(created.AssignedAgent?.Id, result.AssignedAgent?.Id);
        Assert.Equal("Current agent remains eligible", result.Triage?.AssignmentReason);
        var history = Assert.IsType<TicketEscalationDto>(result.Escalation);
        Assert.Equal(TicketPriority.High, history.FromPriority);
        Assert.Equal(TicketPriority.Critical, history.ToPriority);
        Assert.Equal(created.DueAtUtc, history.FromDueAtUtc);
        Assert.Equal(result.DueAtUtc, history.ToDueAtUtc);
        Assert.Equal("agent:1", history.EscalatedBy);
        desk.Db.ChangeTracker.Clear();
        var stored = await desk.Db.Set<Ticket>().SingleAsync(t => t.Id == created.Id);
        Assert.Equal(result.Priority, stored.Priority);
        Assert.Equal(desk.Clock.UtcNow, stored.SlaStartedAtUtc);
        Assert.Equal(desk.Clock.UtcNow.AddHours(3), stored.AtRiskAtUtc);
        Assert.Single(await desk.Db.Set<TicketEscalation>().ToListAsync());
    }

    [Fact]
    public async Task Escalate_ReassignsAnInactiveOwner()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create(), default);
        var owner = await desk.Db.Set<Agent>().SingleAsync(a => a.Id == created.AssignedAgent!.Id);
        owner.Deactivate();
        await desk.Db.SaveChangesAsync();

        var result = await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);

        Assert.NotEqual(created.AssignedAgent?.Id, result.AssignedAgent?.Id);
        Assert.Equal(2, result.AssignedAgent?.Id);
        Assert.Equal(created.AssignedAgent?.Id, result.Escalation?.FromAgent?.Id);
        Assert.Equal(2, result.Escalation?.ToAgent?.Id);
    }

    [Fact]
    public async Task Escalate_CanBecomeUnassignedWhenNobodyQualifies()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create(), default);
        foreach (var agent in await desk.Db.Set<Agent>().ToListAsync()) agent.Deactivate();
        await desk.Db.SaveChangesAsync();

        var result = await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);

        Assert.Null(result.AssignedAgent);
        Assert.Null(result.Escalation?.ToAgent);
        Assert.Contains("No active agent", result.Triage!.AssignmentReason);
    }

    [Theory]
    [InlineData(6, "Critical")]
    [InlineData(25, "Resolved")]
    [InlineData(26, "Closed")]
    public async Task Escalate_ConflictsSaveNeitherTicketNorHistory(int id, string message)
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => desk.EscalateHandler().HandleAsync(id, Reason(), 1, default));
        Assert.Contains(message, error.Message);
        Assert.Empty(await desk.Db.Set<TicketEscalation>().ToListAsync());
    }

    [Fact]
    public async Task MissingTicketsReturnNotFound_ForEscalationAndHistory()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => desk.EscalateHandler().HandleAsync(999, Reason(), 1, default));
        var history = new GetEscalationsQueryHandler(new TicketRepository(desk.Db), new TicketEscalationQueries(desk.Db));
        await Assert.ThrowsAsync<NotFoundException>(() => history.HandleAsync(999, default));
        Assert.Empty(await history.HandleAsync(1, default));
    }

    [Fact]
    public async Task History_IsNewestFirst_WithStableOrderingWhenTimesTie()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create(TicketPriority.Low), default);
        var first = await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);
        var second = await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);

        var history = await new TicketEscalationQueries(desk.Db).GetHistoryAsync(created.Id, default);

        Assert.Equal(2, history.Count);
        Assert.Equal(second.Escalation?.Id, history[0].Id);
        Assert.Equal(first.Escalation?.Id, history[1].Id);
        Assert.Equal(TicketPriority.High, history[0].ToPriority);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_CannotBeEditedOrDeletedThroughEf(bool delete)
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create(), default);
        await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);
        var history = await desk.Db.Set<TicketEscalation>().SingleAsync();
        if (delete) desk.Db.Remove(history);
        else desk.Db.Entry(history).Property(e => e.Reason).CurrentValue = "Tampered reason";

        await Assert.ThrowsAsync<InvalidOperationException>(() => desk.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task StaleEscalation_ReturnsConflictAndRollsBackItsHistory()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create(), default);
        await using var other = new SupportDbContext(desk.Options);
        var stale = await other.Set<Ticket>().SingleAsync(t => t.Id == created.Id);
        await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);
        stale.Escalate(desk.Policy.Calculate(TicketPriority.Critical, CustomerTier.Standard, desk.Clock.UtcNow),
            new AssignmentDecision(null, "No available agent"), "Stale escalation request", "other.lead", desk.Clock.UtcNow);

        await Assert.ThrowsAsync<ConflictException>(() => other.SaveChangesAsync());

        Assert.Single(await desk.Db.Set<TicketEscalation>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task FailedSave_RollsBackBothTheTicketAndItsHistory()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create(), default);
        var ticket = await desk.Db.Set<Ticket>().SingleAsync(t => t.Id == created.Id);
        ticket.Escalate(desk.Policy.Calculate(TicketPriority.Critical, CustomerTier.Standard, desk.Clock.UtcNow),
            new AssignmentDecision(new(999, "Missing agent", true, 8, 0, true), "Invalid fixture"),
            "This save must fail atomically", "lead", desk.Clock.UtcNow);

        await Assert.ThrowsAsync<DbUpdateException>(() => desk.Db.SaveChangesAsync());

        desk.Db.ChangeTracker.Clear();
        Assert.Equal(TicketPriority.High, (await desk.Db.Set<Ticket>().SingleAsync(t => t.Id == created.Id)).Priority);
        Assert.Empty(await desk.Db.Set<TicketEscalation>().ToListAsync());
    }

    [Fact]
    public async Task Escalate_ReassignsAnOwnerAtCapacity()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create(), default);
        for (var index = 42; index <= 44; index++)
            desk.Db.Add(new TicketBuilder().WithReference($"TCK-{index:0000}").AssignedTo(created.AssignedAgent!.Id).Build());
        await desk.Db.SaveChangesAsync();

        var result = await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);

        Assert.Equal(2, result.AssignedAgent?.Id);
        Assert.NotEqual(created.AssignedAgent?.Id, result.AssignedAgent?.Id);
    }

    [Fact]
    public async Task Escalate_DoesNotKeepAnOwnerWhoLostTheRequiredSpecialization()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create() with { CategoryId = 2 }, default);
        var specialization = await desk.Db.Set<AgentSpecialization>()
            .SingleAsync(s => s.AgentId == created.AssignedAgent!.Id && s.CategoryId == 2);
        desk.Db.Remove(specialization);
        await desk.Db.SaveChangesAsync();

        var result = await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);

        Assert.Null(result.AssignedAgent); // The other Billing specialist, Marcus, is full.
        Assert.Contains("specialised in Billing", result.Triage!.AssignmentReason);
    }

    [Fact]
    public async Task Escalate_PremiumCustomerGetsTheShortenedNewWindow()
    {
        await using var desk = await SqliteDesk.CreateAsync();
        var created = await desk.CreateHandler().HandleAsync(Create() with { CustomerId = 1 }, default);
        desk.Clock.Advance(TimeSpan.FromHours(1));

        var result = await desk.EscalateHandler().HandleAsync(created.Id, Reason(), 1, default);

        Assert.Equal(desk.Clock.UtcNow.AddHours(2), result.DueAtUtc);
        Assert.Equal(2, result.Triage?.SlaWindowHours);
    }

}
