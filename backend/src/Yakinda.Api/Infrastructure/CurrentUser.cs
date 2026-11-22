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

    /// <summary>
    /// Giriş yapmış kullanıcının kimliğini döner; token yoksa ya da kimlik okunamıyorsa UnauthorizedException
    /// fırlatır (API'de 401).
    /// </summary>
    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedException("auth.required", "Bu işlem için giriş yapmalısın.");
}
