using System.Globalization;
using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Domain.Aggregates.Tickets;

public sealed record SlaDecision(TimeSpan Window, DateTime DueAtUtc, string Reason, DateTime AtRiskAtUtc)
{
    public double WindowHours => Window.TotalHours;
}

/// <summary>
/// Calculates the current SLA window from an explicit UTC start. Creation and escalation
/// can pass IClock.UtcNow without this policy depending on a clock or a framework.
/// </summary>
public sealed class SlaPolicy
{
    private readonly Dictionary<TicketPriority, double> _baseWindowHours;
    private readonly double _premiumMultiplier;
    private readonly double _minimumWindowHours;

    /// <summary>Validated percentage used to snapshot the at-risk cutoff of each new window.</summary>
    public double AtRiskThresholdPercent { get; }

    public SlaPolicy(SlaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.BaseWindowHours is null)
        {
            throw new ArgumentException("Sla:BaseWindowHours is required.", nameof(settings));
        }

        foreach (var priority in Enum.GetValues<TicketPriority>())
        {
            if (!settings.BaseWindowHours.TryGetValue(priority, out var hours) || !ValidHours(hours))
            {
                throw new ArgumentException($"Sla:BaseWindowHours:{priority} must be a positive, finite duration.", nameof(settings));
            }
        }

        if (!double.IsFinite(settings.PremiumCustomerMultiplier)
            || settings.PremiumCustomerMultiplier <= 0 || settings.PremiumCustomerMultiplier > 1)
        {
            throw new ArgumentException("Sla:PremiumCustomerMultiplier must be greater than 0 and at most 1.", nameof(settings));
        }
        if (!ValidHours(settings.MinimumWindowHours))
        {
            throw new ArgumentException("Sla:MinimumWindowHours must be a positive, finite duration.", nameof(settings));
        }
        if (!double.IsFinite(settings.AtRiskThresholdPercent)
            || settings.AtRiskThresholdPercent <= 0 || settings.AtRiskThresholdPercent > 100)
        {
            throw new ArgumentException("Sla:AtRiskThresholdPercent must be greater than 0 and at most 100.", nameof(settings));
        }

        // Do not let later mutation of the bound settings change an active policy.
        _baseWindowHours = new Dictionary<TicketPriority, double>(settings.BaseWindowHours);
        _premiumMultiplier = settings.PremiumCustomerMultiplier;
        _minimumWindowHours = settings.MinimumWindowHours;
        AtRiskThresholdPercent = settings.AtRiskThresholdPercent;
    }

    public SlaDecision Calculate(TicketPriority priority, CustomerTier tier, DateTime startedAtUtc)
    {
        if (!_baseWindowHours.TryGetValue(priority, out var baseHours))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }
        if (!Enum.IsDefined(tier))
        {
            throw new ArgumentOutOfRangeException(nameof(tier));
        }
        if (startedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The SLA start must be UTC.", nameof(startedAtUtc));
        }

        var hours = baseHours;
        var reason = $"{priority} base window {Format(baseHours)}h";
        if (tier == CustomerTier.Premium)
        {
            var discountedHours = baseHours * _premiumMultiplier;
            hours = Math.Max(discountedHours, _minimumWindowHours);
            reason += $", multiplied by {Format(_premiumMultiplier)} for a Premium customer";
            if (discountedHours < _minimumWindowHours)
            {
                reason += $"; minimum window {Format(_minimumWindowHours)}h applied";
            }
        }
        else
        {
            reason += " for a Standard customer";
        }

        var window = TimeSpan.FromHours(hours);
        var atRiskAt = startedAtUtc.AddTicks((long)Math.Round(window.Ticks * (1 - AtRiskThresholdPercent / 100)));
        return new SlaDecision(window, startedAtUtc.Add(window), reason, atRiskAt);
    }

    private static bool ValidHours(double hours) =>
        double.IsFinite(hours) && hours > 0 && hours < TimeSpan.MaxValue.TotalHours;

    private static string Format(double value) => value.ToString("G", CultureInfo.InvariantCulture);
}
