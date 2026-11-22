using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Moderation;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class BlockRepository(DbSession session) : RepositoryBase(session), IBlockRepository
{
    /// <summary>
    /// Engelleme kaydı ekler; ON CONFLICT DO NOTHING sayesinde aynı kişiyi ikinci kez engellemek hata vermez.
    /// </summary>
    public Task BlockAsync(Guid blockerId, Guid blockedId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO user_blocks (blocker_id, blocked_id, created_at)
            VALUES (@BlockerId, @BlockedId, @Now)
            ON CONFLICT DO NOTHING
            """, new { BlockerId = blockerId, BlockedId = blockedId, Now = nowUtc }, cancellationToken);

    /// <summary>Engelleme kaydını siler; kayıt yoksa hiçbir şey olmaz.</summary>
    public Task UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "DELETE FROM user_blocks WHERE blocker_id = @BlockerId AND blocked_id = @BlockedId",
            new { BlockerId = blockerId, BlockedId = blockedId },
            cancellationToken);

    /// <summary>
    /// İki kullanıcı arasında herhangi bir yönde engel kaydı var mı? Tek EXISTS sorgusuyla bakar.
    /// </summary>
    public async Task<bool> IsBlockedEitherWayAsync(Guid userA, Guid userB, CancellationToken cancellationToken) =>
        await ExecuteScalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM user_blocks
                WHERE (blocker_id = @A AND blocked_id = @B) OR (blocker_id = @B AND blocked_id = @A))
            """, new { A = userA, B = userB }, cancellationToken);
}

internal sealed class ReportRepository(DbSession session) : RepositoryBase(session), IReportRepository
{
    /// <summary>
    /// Şikayeti ekler. Aynı kişinin aynı içerik için açık şikayeti varsa kısmi benzersiz indeks çakışır ve
    /// ON CONFLICT DO NOTHING ile satır eklenmez. Satır eklendiyse true döner.
    /// </summary>
    public async Task<bool> AddIfNotOpenAsync(ReportRecord report, CancellationToken cancellationToken) =>
        await ExecuteAsync("""
            INSERT INTO reports (id, reporter_id, target_type, target_id, reason, details, status, created_at)
            VALUES (@Id, @ReporterId, @TargetType, @TargetId, @Reason, @Details, 0, @CreatedAt)
            ON CONFLICT (reporter_id, target_type, target_id) WHERE status = 0 DO NOTHING
            """,
            new
            {
                report.Id,
                report.ReporterId,
                TargetType = (short)report.TargetType,
                report.TargetId,
                Reason = (short)report.Reason,
                report.Details,
                report.CreatedAt
            },
            cancellationToken) > 0;

    /// <summary>Şikayeti kimliğiyle okur; yoksa null döner.</summary>
    public Task<ReportRecord?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<ReportRecord>("""
            SELECT id, reporter_id, target_type, target_id, reason, details, status, created_at
            FROM reports WHERE id = @Id
            """, new { Id = id }, cancellationToken);

    /// <summary>
    /// Aynı hedef için açık olan bütün şikayetleri tek UPDATE ile kapatır: durumu, karar zamanını, kararı
    /// veren yöneticiyi ve notu yazar. Kapatılan şikayet sayısını döner.
    /// </summary>
    public Task<int> ResolveOpenForTargetAsync(
        ReportTargetType targetType,
        Guid targetId,
        ReportStatus status,
        Guid adminId,
        string? note,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE reports
               SET status = @Status, resolved_at = @Now, resolved_by = @AdminId, resolution_note = @Note
             WHERE target_type = @TargetType AND target_id = @TargetId AND status = 0
            """,
            new
            {
                Status = (short)status,
                Now = nowUtc,
                AdminId = adminId,
                Note = note,
                TargetType = (short)targetType,
                TargetId = targetId
            },
            cancellationToken);
}
