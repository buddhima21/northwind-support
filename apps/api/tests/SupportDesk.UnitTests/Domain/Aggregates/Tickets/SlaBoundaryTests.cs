using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class SlaBoundaryTests
{
    private static readonly DateTime Start = FixedClock.DefaultNow;
    private static readonly DateTime Due = Start.AddHours(4);
    private static readonly DateTime Risk = Start.AddHours(3);

    [Theory]
    [InlineData(-1, SlaStatus.WithinSla)]
    [InlineData(0, SlaStatus.AtRisk)]
    [InlineData(1, SlaStatus.AtRisk)]
    public void Evaluate_AtTheRiskBoundary(long ticks, SlaStatus expected) =>
        Assert.Equal(expected, SlaEvaluator.Evaluate(Due, null, Risk.AddTicks(ticks), Risk));

    [Theory]
    [InlineData(-1, SlaStatus.AtRisk)]
    [InlineData(0, SlaStatus.AtRisk)]
    [InlineData(1, SlaStatus.Breached)]
    public void Evaluate_AtTheDueDate(long ticks, SlaStatus expected) =>
        Assert.Equal(expected, SlaEvaluator.Evaluate(Due, null, Due.AddTicks(ticks), Risk));

    [Theory]
    [InlineData(-1, SlaStatus.Met)]
    [InlineData(0, SlaStatus.Met)]
    [InlineData(1, SlaStatus.Breached)]
    public void Evaluate_ResolutionHasPrecedenceOverNow(long ticks, SlaStatus expected) =>
        Assert.Equal(expected, SlaEvaluator.Evaluate(Due, Due.AddTicks(ticks), Due.AddDays(2), Risk));

    [Fact]
    public void Evaluate_NoDeadline_IsNotApplicableEvenWithOtherDates() =>
        Assert.Equal(SlaStatus.NotApplicable, SlaEvaluator.Evaluate(null, Due, Due.AddDays(1), Risk));

    [Fact]
    public void Policy_ComputesTheConfiguredRiskCutoff()
    {
        var policy = new SlaPolicy(new SlaSettings
        {
            BaseWindowHours = Enum.GetValues<TicketPriority>().ToDictionary(priority => priority, _ => 10d),
            PremiumCustomerMultiplier = 0.5, MinimumWindowHours = 1, AtRiskThresholdPercent = 40
        });
        var standard = policy.Calculate(TicketPriority.High, CustomerTier.Standard, Start);
        var premium = policy.Calculate(TicketPriority.High, CustomerTier.Premium, Start);
        Assert.Equal(Start.AddHours(6), standard.AtRiskAtUtc);
        Assert.Equal(Start.AddHours(3), premium.AtRiskAtUtc);
    }

    [Fact]
    public void QueryExpression_AndRuntimeEvaluationUseTheSameRule()
    {
        var rule = SlaEvaluator.For<Dates>(x => x.Due, x => x.Resolved, x => x.Risk, Due).Compile();
        var dates = new Dates(Due, null, Risk);
        Assert.Equal(SlaEvaluator.Evaluate(Due, null, Due, Risk), rule(dates));
    }

    private sealed record Dates(DateTime? Due, DateTime? Resolved, DateTime? Risk);
}
