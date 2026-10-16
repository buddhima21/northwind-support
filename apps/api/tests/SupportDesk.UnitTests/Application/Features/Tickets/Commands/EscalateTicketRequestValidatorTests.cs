using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public sealed class EscalateTicketRequestValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("     ")]
    [InlineData("four")]
    public void Reason_MissingOrTooShort_IsInvalid(string? reason) =>
        Assert.False(new EscalateTicketRequestValidator().Validate(new EscalateTicketRequest(reason!, "lead")).IsValid);

    [Theory]
    [InlineData(5, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Reason_LengthBoundariesMatchTheDomain(int length, bool valid) =>
        Assert.Equal(valid, new EscalateTicketRequestValidator().Validate(new EscalateTicketRequest(new string('x', length), "lead")).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Actor_IsRequired(string? actor) =>
        Assert.False(new EscalateTicketRequestValidator().Validate(new EscalateTicketRequest("A valid reason", actor!)).IsValid);

    [Fact]
    public void Actor_HasAMaximumOf100Characters() =>
        Assert.False(new EscalateTicketRequestValidator().Validate(new EscalateTicketRequest("A valid reason", new string('x', 101))).IsValid);
}
