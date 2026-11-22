using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Exceptions;

namespace Yakinda.Application.Safety;

/// <summary>
/// Engelleme iki yönlü etki eder: engelleyen ve engellenen birbirinin etkinliklerini listede görmez,
/// detayını ve profilini açamaz, birbirinin etkinliğine katılamaz, mesajlaşamaz.
/// Mevcut katılımlar otomatik silinmez; organizatör isterse katılımcıyı ayrıca çıkarabilir.
/// </summary>
public sealed record BlockUserCommand(Guid UserId) : ICommand<Unit>;

internal sealed class BlockUserCommandHandler(
    IBlockRepository blocks,
    IUserRepository users,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<BlockUserCommand, Unit>
{
    /// <summary>
    /// Kullanıcıyı engeller. Kişi kendini engelleyemez (409); hedef kullanıcı yoksa 404 döner. Engel
    /// kaydını ekler ve iki tarafın profil cache etiketlerini temizletir.
    /// </summary>
    public async Task<Unit> Handle(BlockUserCommand request, CancellationToken cancellationToken)
    {
        var me = currentUser.RequireUserId();
        if (request.UserId == me)
            throw new ConflictException("block.self", "Kendini engelleyemezsin.");

        _ = await users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("user.not_found", "Kullanıcı bulunamadı.");

        await blocks.BlockAsync(me, request.UserId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        cacheInvalidator.InvalidateAfterCommit(CacheTag.User(me), CacheTag.User(request.UserId));
        return Unit.Value;
    }
}

public sealed record UnblockUserCommand(Guid UserId) : ICommand<Unit>;

internal sealed class UnblockUserCommandHandler(IBlockRepository blocks, ICurrentUser currentUser)
    : IRequestHandler<UnblockUserCommand, Unit>
{
    /// <summary>Engeli kaldırır: isteği yapanın bu kullanıcı için koyduğu engel kaydını siler.</summary>
    public async Task<Unit> Handle(UnblockUserCommand request, CancellationToken cancellationToken)
    {
        await blocks.UnblockAsync(currentUser.RequireUserId(), request.UserId, cancellationToken);
        return Unit.Value;
    }
}

public sealed class BlockedUserDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; }
}

/// <summary>Benim engellediklerim (engeli kaldırmak için).</summary>
public sealed record GetMyBlocksQuery : IQuery<IReadOnlyList<BlockedUserDto>>;

internal sealed class GetMyBlocksQueryHandler(IDbConnectionFactory db, ICurrentUser currentUser)
    : IRequestHandler<GetMyBlocksQuery, IReadOnlyList<BlockedUserDto>>
{
    /// <summary>
    /// İsteği yapanın engellediği kullanıcıları (ad ve engelleme tarihiyle) en yeni önce listeler.
    /// </summary>
    public async Task<IReadOnlyList<BlockedUserDto>> Handle(GetMyBlocksQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<BlockedUserDto>(new CommandDefinition("""
            SELECT b.blocked_id AS user_id, u.display_name, b.created_at AS blocked_at
            FROM user_blocks b
            JOIN users u ON u.id = b.blocked_id
            WHERE b.blocker_id = @UserId
            ORDER BY b.created_at DESC
            """, new { UserId = currentUser.RequireUserId() }, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}

/// <summary>Profil gibi cache'lenen okumalardan önce: aralarında engel var mı? (cache'lenmez)</summary>
public sealed record IsBlockedQuery(Guid OtherUserId) : IQuery<bool>;

internal sealed class IsBlockedQueryHandler(IDbConnectionFactory db, ICurrentUser currentUser)
    : IRequestHandler<IsBlockedQuery, bool>
{
    /// <summary>
    /// İsteği yapanla verilen kullanıcı arasında herhangi bir yönde engel var mı? Tek satırlık EXISTS
    /// sorgusuyla yanıtlar.
    /// </summary>
    public async Task<bool> Handle(IsBlockedQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT " + BlockSql.EitherWay("@Me", "@Other"),
            new { Me = currentUser.RequireUserId(), Other = request.OtherUserId },
            cancellationToken: cancellationToken));
    }
}

/// <summary>Okuma sorgularında kullanılan engel koşulu.</summary>
internal static class BlockSql
{
    /// <summary>
    /// İki kullanıcı arasında herhangi bir yönde engel varsa true olan SQL koşulunu üretir; a ve b,
    /// kullanıcı kimliğini veren kolon ya da parametre adlarıdır.
    /// </summary>
    public static string EitherWay(string a, string b) => $"""
        EXISTS (SELECT 1 FROM user_blocks ub
                WHERE (ub.blocker_id = {a} AND ub.blocked_id = {b})
                   OR (ub.blocker_id = {b} AND ub.blocked_id = {a}))
        """;
}
