using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Users.GetMyEvents;
using Yakinda.Application.Users.GetProfile;
using Yakinda.Application.Users.DeleteAccount;
using Yakinda.Application.Safety;
using Yakinda.Application.Exceptions;
using Yakinda.Application.Media;
using Yakinda.Api.Infrastructure;

namespace Yakinda.Api.Endpoints;

public sealed record UpdateProfileRequest(string DisplayName, string? Bio);

public static class UserEndpoints
{
    /// <summary>Kullanıcı uçlarını /api/users altına bağlar; hepsi giriş gerektirir.</summary>
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users").WithTags("Users").RequireAuthorization();

        // GET /api/users/me: isteği yapanın kendi profili.
        group.MapGet("/me", async (ICurrentUser currentUser, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetUserProfileQuery(currentUser.RequireUserId()), ct)));

        // Profili düzenle (ad, hakkımda)
        group.MapPut("/me", async (UpdateProfileRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UpdateProfileCommand(body.DisplayName, body.Bio), ct);
            return Results.NoContent();
        });

        // Profil fotoğrafı: multipart/form-data, "file" alanı (JPEG/PNG/WebP, en fazla 5 MB)
        group.MapPut("/me/avatar", async (HttpRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SetMyAvatarCommand(await request.ReadImageUploadAsync(ct)), ct)));

        // DELETE /api/users/me/avatar: profil fotoğrafını kaldırır.
        group.MapDelete("/me/avatar", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteMyAvatarCommand(), ct);
            return Results.NoContent();
        });

        // GET /api/users/me/events?role=organizing|attending&includePast=true
        group.MapGet("/me/events", async (string? role, bool? includePast, ISender sender, CancellationToken ct) =>
        {
            var parsedRole = string.Equals(role, "attending", StringComparison.OrdinalIgnoreCase)
                ? MyEventsRole.Attending
                : MyEventsRole.Organizing;

            return Results.Ok(await sender.Send(new GetMyEventsQuery(parsedRole, includePast ?? true), ct));
        });

        // GET /api/users/{id}: başka bir kullanıcının profili. Profil cache'lendiği için engel önce ayrı bir
        // sorguyla denetlenir; engel varsa 404 döner.
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            // Profil cache'lenir; engel kontrolü cache'in dışında, her istekte yapılır.
            if (await sender.Send(new IsBlockedQuery(id), ct))
                throw new NotFoundException("user.not_found", "Kullanıcı bulunamadı.");

            return Results.Ok(await sender.Send(new GetUserProfileQuery(id), ct));
        });

        // Engelleme
        group.MapPost("/{id:guid}/block", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new BlockUserCommand(id), ct);
            return Results.NoContent();
        });

        // DELETE /api/users/{id}/block: engeli kaldırır.
        group.MapDelete("/{id:guid}/block", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UnblockUserCommand(id), ct);
            return Results.NoContent();
        });

        // GET /api/users/me/blocks: engellediğim kullanıcılar.
        group.MapGet("/me/blocks", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetMyBlocksQuery(), ct)));

        // Hesap silme: POST /api/users/me/delete  { "password": "..." }
        group.MapPost("/me/delete", async (DeleteAccountRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteAccountCommand(body.Password), ct);
            return Results.NoContent();
        });

        return api;
    }
}

public sealed record DeleteAccountRequest(string Password);
