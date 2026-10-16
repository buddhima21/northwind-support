namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Values supplied by the host's Sla configuration section. No framework dependency or
/// duplicated business defaults: SlaPolicy validates and snapshots these values.
/// </summary>
public sealed class SlaSettings
{
    public Dictionary<TicketPriority, double> BaseWindowHours { get; set; } = [];

    public double PremiumCustomerMultiplier { get; set; }

    public double MinimumWindowHours { get; set; }

    public double AtRiskThresholdPercent { get; set; }
}
