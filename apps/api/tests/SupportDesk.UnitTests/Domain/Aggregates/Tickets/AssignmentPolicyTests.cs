using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class AssignmentPolicyTests
{
    private static Category General() => new("General", false, false);
    private static Category Specialist() => new("Security", true, true);

    private static AssignmentCandidate Candidate(int id, int open, int max = 8,
        bool active = true, bool specialist = false) =>
        new(id, $"Agent {id}", active, max, open, specialist);

    [Fact]
    public void Select_FewestOpenTicketsWins()
    {
        var result = AssignmentPolicy.Select(General(),
            [Candidate(1, 5), Candidate(2, 3), Candidate(3, 4)]);

        Assert.Equal(2, result.Agent?.Id);
        Assert.Contains("Fewest open tickets", result.Reason);
    }

    [Fact]
    public void Select_RequiredSpecialist_ExcludesLessBusyNonSpecialists()
    {
        var result = AssignmentPolicy.Select(Specialist(),
            [Candidate(1, 0), Candidate(2, 3, specialist: true), Candidate(3, 4, specialist: true)]);

        Assert.Equal(2, result.Agent?.Id);
        Assert.Contains("Security specialists", result.Reason);
    }

    [Fact]
    public void Select_WhenSpecialistIsNotRequired_AnyEligibleAgentCanWin()
    {
        var result = AssignmentPolicy.Select(new Category("Technical", false, false),
            [Candidate(1, 3, specialist: true), Candidate(2, 1)]);

        Assert.Equal(2, result.Agent?.Id);
    }

    [Theory]
    [InlineData(5, 6, true)]
    [InlineData(6, 6, false)]
    [InlineData(7, 6, false)]
    public void IsEligible_RequiresStrictlyLessThanTheLimit(int open, int max, bool expected)
    {
        Assert.Equal(expected, AssignmentPolicy.IsEligible(General(), Candidate(1, open, max)));
    }

    [Fact]
    public void Select_ExcludesFullAndOverCapacityAgents()
    {
        var result = AssignmentPolicy.Select(General(),
            [Candidate(1, 1, max: 1), Candidate(2, 2, max: 1), Candidate(3, 3)]);

        Assert.Equal(3, result.Agent?.Id);
    }

    [Fact]
    public void Select_ExcludesInactiveAgentsEvenWhenTheyHaveNoWork()
    {
        var inactive = Candidate(1, 0, active: false, specialist: true);
        var result = AssignmentPolicy.Select(Specialist(), [inactive, Candidate(2, 4, specialist: true)]);

        Assert.False(AssignmentPolicy.IsEligible(Specialist(), inactive));
        Assert.Equal(2, result.Agent?.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_TiesUseLowestIdRegardlessOfInputOrder(bool reverse)
    {
        AssignmentCandidate[] candidates = [Candidate(9, 2), Candidate(3, 2), Candidate(1, 3)];
        var result = AssignmentPolicy.Select(General(), reverse ? candidates.Reverse() : candidates);

        Assert.Equal(3, result.Agent?.Id);
    }

    [Fact]
    public void Select_NoAgents_ReturnsUnassignedWithAReason()
    {
        var result = AssignmentPolicy.Select(General(), []);

        Assert.Null(result.Agent);
        Assert.Contains("No active agent", result.Reason);
    }

    [Fact]
    public void Select_NoAvailableSpecialists_IsANormalUnassignedResult()
    {
        var result = AssignmentPolicy.Select(Specialist(),
            [Candidate(1, 0), Candidate(2, 6, max: 6, specialist: true), Candidate(3, 0, active: false, specialist: true)]);

        Assert.Null(result.Agent);
        Assert.Equal("No available agent specialised in Security", result.Reason);
    }

    [Fact]
    public void Select_NoAvailableGeneralAgents_ReturnsUnassignedWithAReason()
    {
        var result = AssignmentPolicy.Select(General(),
            [Candidate(1, 6, max: 6), Candidate(2, 0, active: false)]);

        Assert.Null(result.Agent);
        Assert.Equal("No active agent below their open-ticket limit", result.Reason);
    }

    [Fact]
    public void Select_UsesTheCategoryFlagRatherThanItsName()
    {
        var category = new Category("Renamed specialist category", true, false);
        var result = AssignmentPolicy.Select(category,
            [Candidate(1, 0), Candidate(2, 1, specialist: true)]);

        Assert.Equal(2, result.Agent?.Id);
        Assert.Contains(category.Name, result.Reason);
    }

    [Theory]
    [InlineData(1, "General", false, 4)]
    [InlineData(2, "Billing", true, 4)]
    [InlineData(3, "Technical", false, 4)]
    [InlineData(4, "Outage", true, 2)]
    [InlineData(5, "Security", true, 3)]
    public void Select_MatchesThePdfSeededExamples(int categoryId, string name, bool requiresSpecialist, int expectedAgent)
    {
        var category = new Category(name, requiresSpecialist, false);
        var candidates = SeededCandidates(categoryId);

        var result = AssignmentPolicy.Select(category, candidates);

        Assert.Equal(expectedAgent, result.Agent?.Id);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    private static AssignmentCandidate[] SeededCandidates(int categoryId) =>
    [
        new(1, "Alex Turner", true, 10, 5, new[] { 1, 3 }.Contains(categoryId)),
        new(2, "Ben Osei", true, 8, 3, new[] { 3, 4 }.Contains(categoryId)),
        new(3, "Priya Nair", true, 8, 4, new[] { 4, 5 }.Contains(categoryId)),
        new(4, "Sara Lindqvist", true, 6, 2, new[] { 1, 2 }.Contains(categoryId)),
        new(5, "Marcus Doyle", true, 6, 6, new[] { 2, 5 }.Contains(categoryId)),
        new(6, "Yuki Tanaka", false, 8, 0, categoryId == 3)
    ];
}
