using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.Application.Features.Tickets.Queries.SearchTickets;

public sealed class TicketQueryValidator : AbstractValidator<TicketQuery>
{
    public TicketQueryValidator() => RuleFor(q => q.SlaStatus).IsInEnum().When(q => q.SlaStatus is not null);
}
