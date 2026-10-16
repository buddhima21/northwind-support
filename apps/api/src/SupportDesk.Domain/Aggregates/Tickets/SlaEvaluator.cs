using System.Linq.Expressions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>One rule for both runtime evaluation and SQL-translatable query expressions.</summary>
public static class SlaEvaluator
{
    private static readonly Expression<Func<DateTime?, DateTime?, DateTime?, DateTime, SlaStatus>> Rule =
        (due, resolved, atRisk, now) => due == null ? SlaStatus.NotApplicable
            : resolved != null ? (resolved <= due ? SlaStatus.Met : SlaStatus.Breached)
            : now > due ? SlaStatus.Breached
            : atRisk != null && now >= atRisk ? SlaStatus.AtRisk
            : SlaStatus.WithinSla;

    private static readonly Func<DateTime?, DateTime?, DateTime?, DateTime, SlaStatus> EvaluateRule = Rule.Compile();

    public static SlaStatus Evaluate(DateTime? dueAtUtc, DateTime? resolvedAtUtc, DateTime nowUtc,
        DateTime? atRiskAtUtc = null) => EvaluateRule(dueAtUtc, resolvedAtUtc, atRiskAtUtc, nowUtc);

    /// <summary>Substitutes field selectors directly; no Invoke or compiled delegate is left in the SQL query.</summary>
    public static Expression<Func<T, SlaStatus>> For<T>(Expression<Func<T, DateTime?>> due,
        Expression<Func<T, DateTime?>> resolved, Expression<Func<T, DateTime?>> atRisk, DateTime nowUtc)
    {
        var parameter = Expression.Parameter(typeof(T), "ticket");
        Expression Bind(Expression<Func<T, DateTime?>> selector) =>
            new Substitute(new Dictionary<ParameterExpression, Expression>
                { [selector.Parameters[0]] = parameter }).Visit(selector.Body)!;

        var replacements = new Dictionary<ParameterExpression, Expression>
        {
            [Rule.Parameters[0]] = Bind(due),
            [Rule.Parameters[1]] = Bind(resolved),
            [Rule.Parameters[2]] = Bind(atRisk),
            [Rule.Parameters[3]] = Expression.Constant(nowUtc)
        };
        return Expression.Lambda<Func<T, SlaStatus>>(new Substitute(replacements).Visit(Rule.Body)!, parameter);
    }

    private sealed class Substitute(IReadOnlyDictionary<ParameterExpression, Expression> replacements) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            replacements.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
    }
}
