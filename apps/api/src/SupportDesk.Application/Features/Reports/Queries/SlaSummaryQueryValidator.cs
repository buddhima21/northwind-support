using FluentValidation;
using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.Application.Features.Reports.Queries;

public sealed class SlaSummaryQueryValidator : AbstractValidator<SlaSummaryQuery>
{
    public SlaSummaryQueryValidator()
    {
        RuleFor(q => q.GroupBy).Must(value =>
                string.Equals(value, "agent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "priority", StringComparison.OrdinalIgnoreCase))
            .WithMessage("GroupBy must be agent or priority.");
        RuleFor(q => q.To).Must((query, to) => query.From is null || to is null || query.From < to)
            .WithMessage("To must be later than From.");
    }
}
