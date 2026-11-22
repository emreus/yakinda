using Yakinda.Application.Exceptions;

namespace Yakinda.Application.Auth;

internal static class AuthErrors
{
    public static ForbiddenException Banned() =>
        new("auth.banned", "Hesabın topluluk kurallarına aykırılık nedeniyle kapatıldı. İtiraz için destek ekibine yaz.");
}
