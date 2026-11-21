using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Exceptions;
using Yakinda.Infrastructure.Auth;

namespace Yakinda.Api.Infrastructure;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var subject = httpContextAccessor.HttpContext?.User.FindFirst(ClaimNames.Subject)?.Value;
            return Guid.TryParse(subject, out var id) ? id : null;
        }
    }

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedException("auth.required", "Bu işlem için giriş yapmalısın.");
}
