using MediatR;
using Yakinda.Api.Infrastructure;
using Yakinda.Application.Auth;

namespace Yakinda.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Kimlik uçlarını /api/auth altına bağlar. Hepsi giriş gerektirmez ve IP başına sıkı istek sınırına
    /// ("auth" politikası) tabidir.
    /// </summary>
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AuthPolicy);

        // POST /api/auth/register: yeni hesap açar; token'ları ve kullanıcı özetini döner.
        group.MapPost("/register", async (RegisterCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        // POST /api/auth/login: e-posta ve şifreyle giriş; token'ları döner.
        group.MapPost("/login", async (LoginCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        // POST /api/auth/refresh: refresh token'ı yenisiyle değiştirir ve yeni erişim token'ı verir.
        group.MapPost("/refresh", async (RefreshTokenCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        // Çıkış: refresh token sunucuda iptal edilir
        group.MapPost("/logout", async (LogoutCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.NoContent();
        });

        // Şifre sıfırlama: önce e-postaya kod, sonra kodla yeni şifre (başarılıysa giriş yapılmış olarak döner)
        group.MapPost("/forgot-password", async (ForgotPasswordCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.NoContent();
        });

        // POST /api/auth/reset-password: kod doğruysa şifreyi değiştirir ve token'ları döner. Kod hatalıysa
        // ya da süresi dolduysa handler'ın döndüğü hata 400 ProblemDetails'e çevrilir.
        group.MapPost("/reset-password", async (ResetPasswordCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            if (result.Auth is not null)
                return Results.Ok(result.Auth);

            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Şifre sıfırlanamadı",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });
        });

        return api;
    }
}
