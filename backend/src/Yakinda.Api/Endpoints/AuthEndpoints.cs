using MediatR;
using Yakinda.Api.Infrastructure;
using Yakinda.Application.Auth;

namespace Yakinda.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AuthPolicy);

        group.MapPost("/register", async (RegisterCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        group.MapPost("/login", async (LoginCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        group.MapPost("/refresh", async (RefreshTokenCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        // Şifre sıfırlama: önce e-postaya kod, sonra kodla yeni şifre (başarılıysa giriş yapılmış olarak döner)
        group.MapPost("/forgot-password", async (ForgotPasswordCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.NoContent();
        });

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
