using FluentValidation;
using SupportDesk.Application.Contracts.Auth;

namespace SupportDesk.Application.Features.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(request => request.Password).NotEmpty().MaximumLength(1024);
    }
}
