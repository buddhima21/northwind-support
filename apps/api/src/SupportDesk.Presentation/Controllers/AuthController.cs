using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAgentAuthenticationService authentication) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authentication.LoginAsync(request, ct);
        return result is null
            ? Unauthorized(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = "Invalid email or password." })
            : Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthenticatedAgentDto>> Me(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirst("sub")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return Unauthorized();

        var agent = await authentication.GetActiveAgentAsync(id, ct);
        return agent is null ? Unauthorized() : Ok(agent);
    }
}
