using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryDoesNotExist_DoesNotCreateOrSave()
    {
        var context = new TicketCommandTestContext();
        context.Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var error = await Assert.ThrowsAsync<NotFoundException>(() =>
            context.RaiseTicket.HandleAsync(Request(), default));

        Assert.Contains("Category", error.Message);
        Assert.Null(context.AddedTicket);
        context.VerifySaved(Times.Never());
        context.AssignmentQueries.Verify(q => q.GetCandidatesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheSavedDecisionsAndTheirExplanations()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCustomer(CustomerTier.Premium);
        context.ExistingCategory("Security", true, true);
        context.Candidates = [new(3, "Priya Nair", true, 8, 4, true), new(5, "Marcus Doyle", true, 6, 6, true)];
        context.Clock.Advance(TimeSpan.FromHours(3));
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;

        var result = await context.RaiseTicket.HandleAsync(Request(TicketPriority.Low), ct);

        var ticket = Assert.IsType<Ticket>(context.AddedTicket);
        Assert.Equal(TicketPriority.Critical, ticket.Priority);
        Assert.Equal(context.Clock.UtcNow.AddHours(2), ticket.DueAtUtc);
        Assert.Equal(3, ticket.AssignedAgentId);
        Assert.Equal(ticket.Priority, result.Priority);
        Assert.Equal(ticket.DueAtUtc, result.DueAtUtc);
        Assert.Equal(ticket.AssignedAgentId, result.AssignedAgent?.Id);
        Assert.Equal(SlaStatus.WithinSla, result.SlaStatus);
        var triage = Assert.IsType<TriageResultDto>(result.Triage);
        Assert.Equal(TicketPriority.Critical, triage.AppliedPriority);
        Assert.Equal(2, triage.SlaWindowHours);
        Assert.Contains("Security", triage.PriorityReason);
        Assert.Contains("Premium", triage.SlaReason);
        Assert.Contains("Security specialists", triage.AssignmentReason);
        context.VerifySaved(Times.Once());
        context.Customers.Verify(c => c.GetByIdAsync(1, ct), Times.Once());
        context.Categories.Verify(c => c.GetByIdAsync(3, ct), Times.Once());
        context.AssignmentQueries.Verify(q => q.GetCandidatesAsync(3, ct), Times.Once());
        context.UnitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once());
    }

    [Fact]
    public async Task HandleAsync_NoEligibleAgent_StillCreatesAnUnassignedTicket()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCategory("Billing", true, false);
        context.Candidates = [new(1, "Non-specialist", true, 10, 0, false)];

        var result = await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), default);

        Assert.NotNull(context.AddedTicket);
        Assert.Null(context.AddedTicket.AssignedAgentId);
        Assert.Null(result.AssignedAgent);
        Assert.Equal(context.Clock.UtcNow.AddHours(8), result.DueAtUtc);
        Assert.Equal("No available agent specialised in Billing", result.Triage?.AssignmentReason);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WhenAssignmentReadIsCancelled_DoesNotCreateOrSave()
    {
        var context = new TicketCommandTestContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var ct = cancellation.Token;
        context.AssignmentQueries.Setup(q => q.GetCandidatesAsync(3, ct))
            .Returns(Task.FromCanceled<IReadOnlyList<AssignmentCandidate>>(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.RaiseTicket.HandleAsync(Request(), ct));

        Assert.Null(context.AddedTicket);
        context.VerifySaved(Times.Never());
    }

}
