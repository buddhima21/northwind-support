using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class TriagedTicketTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;
    private static readonly PriorityDecision Priority = new(TicketPriority.High, "Requested High");
    private static readonly SlaDecision Sla = new(TimeSpan.FromHours(8), Now.AddHours(8), "8-hour window", Now.AddHours(6));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RaiseTriaged_InitializesAllDecisionsTogether(bool assign)
    {
        var assignment = new AssignmentDecision(assign ? new(4, "Sara", true, 6, 2, true) : null, "Decision");

        var ticket = Ticket.RaiseTriaged("TCK-0041", "  A ticket title  ", "A detailed description", 1, 2,
            Priority, Sla, assignment, Now);

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(Now.AddHours(8), ticket.DueAtUtc);
        Assert.Equal(assign ? (int?)4 : null, ticket.AssignedAgentId);
        Assert.Equal(Now, ticket.CreatedAtUtc);
        Assert.Equal(Now, ticket.UpdatedAtUtc);
        Assert.Equal("A ticket title", ticket.Title);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    [InlineData(8, 4)]
    public void RaiseTriaged_RejectsInvalidOrInconsistentSla(double windowHours, double dueHours)
    {
        var sla = new SlaDecision(TimeSpan.FromHours(windowHours), Now.AddHours(dueHours), "Invalid SLA", Now.AddHours(dueHours));

        Assert.Throws<ArgumentException>(() => Ticket.RaiseTriaged("TCK-0041", "A ticket title", "A description",
            1, 2, Priority, sla, new AssignmentDecision(null, "Unassigned"), Now));
    }

    [Fact]
    public void RaiseTriaged_RejectsAnUnpersistedAssignee()
    {
        var assignment = new AssignmentDecision(new(0, "Unsaved", true, 8, 0, true), "Invalid agent");

        Assert.Throws<ArgumentException>(() => Ticket.RaiseTriaged("TCK-0041", "A ticket title", "A description",
            1, 2, Priority, Sla, assignment, Now));
    }
}
