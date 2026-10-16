using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class PriorityPolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.Medium)]
    [InlineData(TicketPriority.High)]
    [InlineData(TicketPriority.Critical)]
    public void Determine_WithoutForcedPriority_RespectsTheRequest(TicketPriority requested)
    {
        var category = new Category("Billing", true, false);

        var result = PriorityPolicy.Determine(requested, category);

        Assert.Equal(requested, result.AppliedPriority);
        Assert.Contains("Requested priority", result.Reason);
    }

    [Fact]
    public void Determine_WithoutARequest_DefaultsToMedium()
    {
        var result = PriorityPolicy.Determine(null, new Category("General", false, false));

        Assert.Equal(TicketPriority.Medium, result.AppliedPriority);
        Assert.Contains("defaulted to Medium", result.Reason);
    }

    [Theory]
    [InlineData("Security", TicketPriority.Low)]
    [InlineData("Outage", TicketPriority.Medium)]
    [InlineData("Renamed urgent category", TicketPriority.High)]
    [InlineData("Security", TicketPriority.Critical)]
    [InlineData("Outage", null)]
    public void Determine_ForcedCategory_AlwaysUsesCritical(string name, TicketPriority? requested)
    {
        var result = PriorityPolicy.Determine(requested, new Category(name, true, true));

        Assert.Equal(TicketPriority.Critical, result.AppliedPriority);
        Assert.Contains(name, result.Reason);
        Assert.Contains("always treated as Critical", result.Reason);
    }

    [Fact]
    public void Determine_UsesTheFlagRatherThanTheCategoryName()
    {
        var result = PriorityPolicy.Determine(TicketPriority.Low, new Category("Security", false, false));

        Assert.Equal(TicketPriority.Low, result.AppliedPriority);
    }

    [Fact]
    public void Determine_InvalidPriority_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PriorityPolicy.Determine((TicketPriority)99, new Category("General", false, false)));
}
