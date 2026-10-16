using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class TicketEscalationTests
{
    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void Escalate_RaisesOneLevelAndCapturesBeforeAfter(TicketPriority before, TicketPriority after)
    {
        var ticket = new TicketBuilder().WithPriority(before).AssignedTo(1).Build();
        var now = FixedClock.DefaultNow.AddHours(5);
        var sla = SqliteDesk.DefaultPolicy().Calculate(after, CustomerTier.Standard, now);
        var assignment = new AssignmentDecision(new(2, "Ben", true, 8, 3, true), "Reassigned");
        var revision = ticket.Revision;

        var history = ticket.Escalate(sla, assignment, "  Needs urgent attention  ", "  test.lead  ", now);

        Assert.Equal(after, ticket.Priority);
        Assert.Equal(sla.DueAtUtc, ticket.DueAtUtc);
        Assert.Equal(now, ticket.SlaStartedAtUtc);
        Assert.Equal(sla.AtRiskAtUtc, ticket.AtRiskAtUtc);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Equal(revision + 1, ticket.Revision);
        Assert.Same(history, Assert.Single(ticket.Escalations));
        Assert.Equal(before, history.FromPriority);
        Assert.Equal(after, history.ToPriority);
        Assert.Equal(1, history.FromAgentId);
        Assert.Equal(2, history.ToAgentId);
        Assert.Null(history.FromDueAtUtc);
        Assert.Equal(sla.DueAtUtc, history.ToDueAtUtc);
        Assert.Equal(now, history.EscalatedAtUtc);
        Assert.Equal("Needs urgent attention", history.Reason);
        Assert.Equal("test.lead", history.EscalatedBy);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, TicketStatus.New, "Critical")]
    [InlineData(TicketPriority.High, TicketStatus.Resolved, "Resolved")]
    [InlineData(TicketPriority.High, TicketStatus.Closed, "Closed")]
    public void Escalate_RejectsNonEscalatableTickets(TicketPriority priority, TicketStatus status, string message)
    {
        var ticket = new TicketBuilder().WithPriority(priority).WithStatus(status).Build();
        var error = Assert.Throws<BusinessRuleViolationException>(() => ticket.NextEscalationPriority());
        Assert.Contains(message, error.Message);
        Assert.Empty(ticket.Escalations);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(501)]
    public void Escalate_InvalidReasonLeavesTheTicketUnchanged(int length)
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.High).Build();
        var now = FixedClock.DefaultNow;
        var sla = SqliteDesk.DefaultPolicy().Calculate(TicketPriority.Critical, CustomerTier.Standard, now);
        Assert.Throws<ArgumentException>(() => ticket.Escalate(sla,
            new AssignmentDecision(null, "None available"), new string('x', length), "lead", now));
        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Null(ticket.DueAtUtc);
        Assert.Empty(ticket.Escalations);
    }
}
