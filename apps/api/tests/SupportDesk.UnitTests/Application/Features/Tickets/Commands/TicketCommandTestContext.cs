using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.AssignTicket;
using SupportDesk.Application.Features.Tickets.Commands.ChangeTicketStatus;
using SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

/// <summary>
/// Shared arrangement for the ticket command tests: mocked repositories and queries, a fixed
/// clock, and command handlers wired to all of them.
/// </summary>
internal sealed class TicketCommandTestContext
{
    public TicketCommandTestContext()
    {
        ExistingCustomer(CustomerTier.Standard);
        ExistingCategory("Technical", false, false);
        AssignmentQueries.Setup(q => q.GetCandidatesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Candidates);

        Tickets.Setup(t => t.NextReferenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("TCK-0041");

        Tickets.Setup(t => t.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, CancellationToken>((ticket, _) => AddedTicket = ticket)
            .Returns(Task.CompletedTask);

        // Every command returns the ticket as re-read once its change has been saved.
        TicketQueries.Setup(q => q.GetDetailAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TicketDetailDto
            {
                Id = 41,
                Reference = "TCK-0041",
                Title = AddedTicket?.Title ?? "Existing ticket",
                Priority = AddedTicket?.Priority ?? TicketPriority.Medium,
                Status = AddedTicket?.Status ?? TicketStatus.New,
                DueAtUtc = AddedTicket?.DueAtUtc,
                CreatedAtUtc = AddedTicket?.CreatedAtUtc ?? Clock.UtcNow,
                UpdatedAtUtc = AddedTicket?.UpdatedAtUtc ?? Clock.UtcNow,
                AssignedAgent = AddedTicket?.AssignedAgentId is { } agentId
                    ? new AgentSummaryDto(agentId, Candidates.Single(candidate => candidate.Id == agentId).FullName)
                    : null,
                SlaStatus = SlaEvaluator.Evaluate(AddedTicket?.DueAtUtc, null, Clock.UtcNow),
                Customer = new CustomerContactDto(1, "Test customer", "support@example.test", null, CustomerTier.Standard),
                Category = new CategoryDto(3, "Technical", false)
            });

        var getTicket = new GetTicketQueryHandler(TicketQueries.Object);

        RaiseTicket = new RaiseTicketCommandHandler(
            Tickets.Object, Customers.Object, Categories.Object, AssignmentQueries.Object, SlaPolicy, UnitOfWork.Object, Clock, getTicket,
            NullLogger<RaiseTicketCommandHandler>.Instance);

        ChangeTicketStatus = new ChangeTicketStatusCommandHandler(Tickets.Object, UnitOfWork.Object, Clock, getTicket);

        AssignTicket = new AssignTicketCommandHandler(
            Tickets.Object, Agents.Object, UnitOfWork.Object, Clock, getTicket,
            NullLogger<AssignTicketCommandHandler>.Instance);
    }

    public Mock<ITicketRepository> Tickets { get; } = new();

    public Mock<ICustomerRepository> Customers { get; } = new();

    public Mock<ICategoryRepository> Categories { get; } = new();

    public Mock<IAgentRepository> Agents { get; } = new();

    public Mock<IUnitOfWork> UnitOfWork { get; } = new();

    public Mock<ITicketQueries> TicketQueries { get; } = new();

    public Mock<IAgentAssignmentQueries> AssignmentQueries { get; } = new();

    public IReadOnlyList<AssignmentCandidate> Candidates { get; set; } =
        [new(4, "Sara Lindqvist", true, 6, 2, true)];

    public SlaPolicy SlaPolicy { get; } = new(new SlaSettings
    {
        BaseWindowHours = new()
        {
            [TicketPriority.Low] = 72, [TicketPriority.Medium] = 24,
            [TicketPriority.High] = 8, [TicketPriority.Critical] = 4
        },
        PremiumCustomerMultiplier = 0.5, MinimumWindowHours = 1, AtRiskThresholdPercent = 25
    });

    public void ExistingCustomer(CustomerTier tier) =>
        Customers.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer("Test customer", "support@example.test", null, tier, Clock.UtcNow));

    public void ExistingCategory(string name, bool requiresSpecialist, bool forcesCritical) =>
        Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category(name, requiresSpecialist, forcesCritical));

    public FixedClock Clock { get; } = new();

    public RaiseTicketCommandHandler RaiseTicket { get; }

    public ChangeTicketStatusCommandHandler ChangeTicketStatus { get; }

    public AssignTicketCommandHandler AssignTicket { get; }

    /// <summary>The ticket handed to the repository by the last create call.</summary>
    public Ticket? AddedTicket { get; private set; }

    /// <summary>Makes <see cref="ITicketRepository.GetByIdAsync"/> return this ticket.</summary>
    public Ticket ExistingTicket(Ticket ticket)
    {
        Tickets.Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        return ticket;
    }

    public void ExistingAgent(int id, string fullName = "Alex Turner", bool isActive = true)
    {
        var agent = new Agent(fullName, $"agent{id}@example.com", maxOpenTickets: 10, Clock.UtcNow);

        if (!isActive)
        {
            agent.Deactivate();
        }

        Agents.Setup(a => a.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
    }

    public void VerifySaved(Times times) =>
        UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), times);
}
