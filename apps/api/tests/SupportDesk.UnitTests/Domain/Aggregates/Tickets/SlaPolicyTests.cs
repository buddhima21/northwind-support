using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class SlaPolicyTests
{
    private static SlaSettings Settings() => new()
    {
        BaseWindowHours = new()
        {
            [TicketPriority.Low] = 72,
            [TicketPriority.Medium] = 24,
            [TicketPriority.High] = 8,
            [TicketPriority.Critical] = 4
        },
        PremiumCustomerMultiplier = 0.5,
        MinimumWindowHours = 1,
        AtRiskThresholdPercent = 25
    };

    [Theory]
    [InlineData(TicketPriority.Critical, CustomerTier.Standard, 4)]
    [InlineData(TicketPriority.High, CustomerTier.Standard, 8)]
    [InlineData(TicketPriority.Medium, CustomerTier.Standard, 24)]
    [InlineData(TicketPriority.Low, CustomerTier.Standard, 72)]
    [InlineData(TicketPriority.Critical, CustomerTier.Premium, 2)]
    [InlineData(TicketPriority.High, CustomerTier.Premium, 4)]
    [InlineData(TicketPriority.Medium, CustomerTier.Premium, 12)]
    [InlineData(TicketPriority.Low, CustomerTier.Premium, 36)]
    public void Calculate_UsesTheWindowForPriorityAndTier(TicketPriority priority, CustomerTier tier, double hours)
    {
        var clock = new FixedClock();

        var result = new SlaPolicy(Settings()).Calculate(priority, tier, clock.UtcNow);

        Assert.Equal(TimeSpan.FromHours(hours), result.Window);
        Assert.Equal(hours, result.WindowHours);
        Assert.Equal(clock.UtcNow.AddHours(hours), result.DueAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.DueAtUtc.Kind);
        Assert.Contains(priority.ToString(), result.Reason);
        Assert.Contains(tier.ToString(), result.Reason);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1.5)]
    public void Calculate_PremiumWindow_RespectsTheOneHourFloor(double baseHours, double expectedHours)
    {
        var settings = Settings();
        settings.BaseWindowHours[TicketPriority.Critical] = baseHours;

        var result = new SlaPolicy(settings).Calculate(TicketPriority.Critical, CustomerTier.Premium, FixedClock.DefaultNow);

        Assert.Equal(expectedHours, result.WindowHours);
        if (baseHours < 2)
        {
            Assert.Contains("minimum window 1h applied", result.Reason);
        }
    }

    [Fact]
    public void Calculate_UsesChangedConfigurationInsteadOfHardcodedRules()
    {
        var settings = Settings();
        settings.BaseWindowHours[TicketPriority.High] = 10;
        settings.PremiumCustomerMultiplier = 0.25;
        settings.MinimumWindowHours = 3;
        settings.AtRiskThresholdPercent = 40;
        var policy = new SlaPolicy(settings);

        Assert.Equal(10, policy.Calculate(TicketPriority.High, CustomerTier.Standard, FixedClock.DefaultNow).WindowHours);
        var premium = policy.Calculate(TicketPriority.High, CustomerTier.Premium, FixedClock.DefaultNow);
        Assert.Equal(3, premium.WindowHours);
        Assert.Contains("multiplied by 0.25", premium.Reason);
        Assert.Contains("minimum window 3h applied", premium.Reason);
        Assert.Equal(40, policy.AtRiskThresholdPercent);
    }

    [Fact]
    public void Calculate_RecomputesTheDeadlineFromTheSuppliedStart()
    {
        var clock = new FixedClock();
        var policy = new SlaPolicy(Settings());
        var original = policy.Calculate(TicketPriority.High, CustomerTier.Standard, clock.UtcNow);
        clock.Advance(TimeSpan.FromHours(5));

        var later = policy.Calculate(TicketPriority.High, CustomerTier.Standard, clock.UtcNow);

        Assert.Equal(original.DueAtUtc.AddHours(5), later.DueAtUtc);
    }

    [Fact]
    public void Calculate_SnapshotsSettingsSoLaterMutationCannotChangeItsRules()
    {
        var settings = Settings();
        var policy = new SlaPolicy(settings);
        settings.BaseWindowHours[TicketPriority.Critical] = 100;
        settings.PremiumCustomerMultiplier = 1;
        settings.MinimumWindowHours = 100;
        settings.AtRiskThresholdPercent = 99;

        Assert.Equal(2, policy.Calculate(TicketPriority.Critical, CustomerTier.Premium, FixedClock.DefaultNow).WindowHours);
        Assert.Equal(25, policy.AtRiskThresholdPercent);
    }

    [Fact]
    public void Constructor_MissingPriorityWindow_IsRejected()
    {
        var settings = Settings();
        settings.BaseWindowHours.Remove(TicketPriority.Low);

        var error = Assert.Throws<ArgumentException>(() => new SlaPolicy(settings));

        Assert.Contains("Sla:BaseWindowHours:Low", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_InvalidDurations_AreRejected(double hours)
    {
        var settings = Settings();
        settings.BaseWindowHours[TicketPriority.Low] = hours;
        Assert.Throws<ArgumentException>(() => new SlaPolicy(settings));
        settings = Settings();
        settings.MinimumWindowHours = hours;
        Assert.Throws<ArgumentException>(() => new SlaPolicy(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_InvalidPremiumMultiplier_IsRejected(double multiplier)
    {
        var settings = Settings();
        settings.PremiumCustomerMultiplier = multiplier;

        Assert.Throws<ArgumentException>(() => new SlaPolicy(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_InvalidAtRiskThreshold_IsRejected(double percent)
    {
        var settings = Settings();
        settings.AtRiskThresholdPercent = percent;

        Assert.Throws<ArgumentException>(() => new SlaPolicy(settings));
    }

    [Fact]
    public void Calculate_InvalidPriorityOrTier_IsRejected()
    {
        var policy = new SlaPolicy(Settings());

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Calculate((TicketPriority)99, CustomerTier.Standard, FixedClock.DefaultNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Calculate(TicketPriority.High, (CustomerTier)99, FixedClock.DefaultNow));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Calculate_NonUtcStart_IsRejected(DateTimeKind kind)
    {
        var start = DateTime.SpecifyKind(FixedClock.DefaultNow, kind);

        Assert.Throws<ArgumentException>(() => new SlaPolicy(Settings()).Calculate(TicketPriority.High, CustomerTier.Standard, start));
    }
}
