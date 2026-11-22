using Yakinda.Application.Exceptions;

namespace Yakinda.Application.Auth;

internal static class AuthErrors
{
    /// <summary>Yasaklı hesap hatasını üretir (auth.banned). API bunu 403 olarak döner.</summary>
    public static ForbiddenException Banned() =>
        new("auth.banned", "Hesabın topluluk kurallarına aykırılık nedeniyle kapatıldı. İtiraz için destek ekibine yaz.");
}
