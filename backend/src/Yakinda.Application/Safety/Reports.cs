using Dapper;
using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;
using Yakinda.Application.Exceptions;
using Yakinda.Domain.Moderation;

namespace Yakinda.Application.Safety;

// ===================================================================== Şikayet etme

/// <summary>
/// Etkinliği, kullanıcıyı ya da mesajı şikayet eder. Aynı kişi aynı içeriği tekrar şikayet ederse
/// (açık şikayeti varken) yeni kayıt oluşmaz; yanıt yine başarılıdır.
/// </summary>
public sealed record CreateReportCommand(ReportTargetType TargetType, Guid TargetId, ReportReason Reason, string? Details)
    : ICommand<Unit>;

internal sealed class CreateReportCommandValidator : AbstractValidator<CreateReportCommand>
{
    /// <summary>
    /// Şikayet türü ve neden geçerli birer değer olmalı; açıklama en fazla 500 karakter olabilir.
    /// </summary>
    public CreateReportCommandValidator()
    {
        RuleFor(x => x.TargetType).IsInEnum().WithName("Şikayet türü");
        RuleFor(x => x.Reason).IsInEnum().WithName("Neden");
        RuleFor(x => x.Details).MaximumLength(500).WithName("Açıklama");
    }
}

internal sealed class CreateReportCommandHandler(
    IReportRepository reports,
    IEventRepository events,
    IUserRepository users,
    IReportTargetLookup targets,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<CreateReportCommand, Unit>
{
    /// <summary>
    /// Şikayet oluşturur. Hedefin (etkinlik, kullanıcı ya da mesaj) sahibini bulur; hedef yoksa 404,
    /// kişi kendi içeriğini şikayet ediyorsa 409 döner. Sonra şikayeti Open durumda kaydeder; aynı kişinin
    /// aynı içerik için açık şikayeti varsa yeni kayıt oluşmaz ama yanıt yine başarılıdır.
    /// </summary>
    public async Task<Unit> Handle(CreateReportCommand request, CancellationToken cancellationToken)
    {
        var reporterId = currentUser.RequireUserId();

        var ownerId = request.TargetType switch
        {
            ReportTargetType.Event => (await events.GetForUpdateAsync(request.TargetId, cancellationToken))?.OrganizerId,
            ReportTargetType.User => (await users.GetByIdAsync(request.TargetId, cancellationToken))?.Id,
            ReportTargetType.Message => await targets.GetMessageAuthorAsync(request.TargetId, cancellationToken),
            _ => null
        } ?? throw new NotFoundException("report.target_not_found", "Şikayet edilen içerik bulunamadı.");

        if (ownerId == reporterId)
            throw new ConflictException("report.own_content", "Kendi içeriğini şikayet edemezsin.");

        await reports.AddIfNotOpenAsync(new ReportRecord
        {
            Id = Guid.CreateVersion7(),
            ReporterId = reporterId,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            Reason = request.Reason,
            Details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim(),
            Status = ReportStatus.Open,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Mesaj gibi ayrı tablolardaki şikayet hedeflerinin sahibini bulur.</summary>
public interface IReportTargetLookup
{
    /// <summary>Sohbet mesajının yazarını döner; mesaj yoksa null.</summary>
    Task<Guid?> GetMessageAuthorAsync(Guid messageId, CancellationToken cancellationToken);
}

// ===================================================================== Yönetici

internal static class AdminGuard
{
    /// <summary>Yönetici değilse 403. Yetki her istekte veritabanından okunur (token'a gömülmez).</summary>
    public static async Task<Guid> RequireAdminAsync(IUserRepository users, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is not { IsAdmin: true })
            throw new ForbiddenException("auth.admin_only", "Bu işlem için yönetici yetkisi gerekiyor.");
        return userId;
    }
}

public sealed class OpenReportDto
{
    public Guid Id { get; set; }
    public ReportTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }

    /// <summary>Etkinlik başlığı, kullanıcı adı ya da mesaj metni.</summary>
    public string? TargetSummary { get; set; }

    /// <summary>İçeriğin sahibi (yasaklama için).</summary>
    public Guid? TargetOwnerId { get; set; }

    public ReportReason Reason { get; set; }
    public string? Details { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Aynı içerik için toplam açık şikayet sayısı.</summary>
    public int OpenReportsForTarget { get; set; }
}

/// <summary>İncelenmeyi bekleyen şikayetler, en eski önce (24 saat içinde yanıt hedefi).</summary>
public sealed record GetOpenReportsQuery : IQuery<IReadOnlyList<OpenReportDto>>;

internal sealed class GetOpenReportsQueryHandler(IDbConnectionFactory db, IUserRepository users, ICurrentUser currentUser)
    : IRequestHandler<GetOpenReportsQuery, IReadOnlyList<OpenReportDto>>
{
    /// <summary>
    /// Yönetici kuyruğunu listeler. Önce isteği yapanın yönetici olduğunu denetler (değilse 403); sonra
    /// açık şikayetleri en eski önce, hedefin özeti, sahibi ve aynı içerik için açık şikayet sayısıyla okur.
    /// </summary>
    public async Task<IReadOnlyList<OpenReportDto>> Handle(GetOpenReportsQuery request, CancellationToken cancellationToken)
    {
        await AdminGuard.RequireAdminAsync(users, currentUser, cancellationToken);

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<OpenReportDto>(new CommandDefinition($"""
            SELECT r.id, r.target_type, r.target_id, r.reason, r.details, r.created_at,
                   reporter.display_name AS reporter_name,
                   CASE r.target_type
                        WHEN 1 THEN e.title
                        WHEN 2 THEN tu.display_name
                        ELSE {ReportTargetSql.MessageSummary}
                   END AS target_summary,
                   CASE r.target_type
                        WHEN 1 THEN e.organizer_id
                        WHEN 2 THEN tu.id
                        ELSE {ReportTargetSql.MessageAuthor}
                   END AS target_owner_id,
                   count(*) OVER (PARTITION BY r.target_type, r.target_id)::int AS open_reports_for_target
            FROM reports r
            JOIN users reporter ON reporter.id = r.reporter_id
            LEFT JOIN events e  ON r.target_type = 1 AND e.id = r.target_id
            LEFT JOIN users tu  ON r.target_type = 2 AND tu.id = r.target_id
            WHERE r.status = 0
            ORDER BY r.created_at
            LIMIT 200
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}

/// <summary>Şikayet kuyruğunda mesaj hedeflerinin özeti ve sahibi.</summary>
internal static class ReportTargetSql
{
    public const string MessageSummary = "(SELECT m.body FROM event_messages m WHERE m.id = r.target_id)";
    public const string MessageAuthor = "(SELECT m.author_id FROM event_messages m WHERE m.id = r.target_id)";
}

/// <summary>
/// Şikayeti sonuçlandırır; aynı içerik için açık olan diğer şikayetler de kapanır.
///  * Dismiss: kural ihlali yok.
///  * RemoveContent: etkinlik kaldırılır (katılımcılara bildirim, kapak görseli silinir), mesaj silinir ya da
///    kullanıcı şikayetinde profil fotoğrafı ve "hakkımda" yazısı kaldırılır.
///  * BanUser: içeriğin sahibi yasaklanır: giriş yapamaz, yaklaşan etkinlikleri kaldırılır, katılımları boşalır.
/// </summary>
public sealed record ResolveReportCommand(Guid ReportId, ModerationAction Action, string? Note) : ICommand<Unit>;

internal sealed class ResolveReportCommandHandler(
    IReportRepository reports,
    IEventRepository events,
    IUserRepository users,
    IReportTargetLookup targets,
    IModerationContentRemover contentRemover,
    IMediaRepository media,
    Media.MediaChanges mediaChanges,
    ICacheInvalidator cacheInvalidator,
    AccountCleanup cleanup,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<ResolveReportCommand, Unit>
{
    /// <summary>
    /// Yöneticinin şikayeti sonuçlandırması. Yönetici değilse 403, şikayet yoksa 404, zaten kapatılmışsa
    /// 409 döner. Dismiss hiçbir şey yapmaz; RemoveContent içeriği kaldırır; BanUser içeriğin sahibini
    /// yasaklar, yaklaşan etkinliklerini kaldırır, katılımlarını boşaltır ve içeriği kaldırır. En sonda
    /// aynı içerik için açık olan bütün şikayetler tek seferde kapatılır.
    /// </summary>
    public async Task<Unit> Handle(ResolveReportCommand request, CancellationToken cancellationToken)
    {
        var adminId = await AdminGuard.RequireAdminAsync(users, currentUser, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var report = await reports.GetAsync(request.ReportId, cancellationToken)
            ?? throw new NotFoundException("report.not_found", "Şikayet bulunamadı.");
        if (report.Status != ReportStatus.Open)
            throw new ConflictException("report.already_resolved", "Bu şikayet zaten sonuçlandırılmış.");

        switch (request.Action)
        {
            case ModerationAction.Dismiss:
                break;

            case ModerationAction.RemoveContent:
                await RemoveContentAsync(report, now, cancellationToken);
                break;

            case ModerationAction.BanUser:
                var ownerId = await GetOwnerAsync(report, cancellationToken);
                await users.BanAsync(ownerId, now, cancellationToken);
                await cleanup.CloseUpcomingOrganizedEventsAsync(ownerId, removedByModerator: true, now, cancellationToken);
                await cleanup.LeaveUpcomingEventsAsync(ownerId, now, cancellationToken);
                await RemoveContentAsync(report, now, cancellationToken);
                break;
        }

        await reports.ResolveOpenForTargetAsync(
            report.TargetType,
            report.TargetId,
            request.Action == ModerationAction.Dismiss ? ReportStatus.Dismissed : ReportStatus.ActionTaken,
            adminId,
            request.Note,
            now,
            cancellationToken);

        return Unit.Value;
    }

    /// <summary>
    /// Şikayet edilen içeriği kaldırır. Etkinlikse: yönetici kaldırması olarak kapatılır ve kapak görseli
    /// silinir. Mesajsa: silinmiş olarak işaretlenir. Kullanıcıysa: profil fotoğrafı ve tanıtım yazısı
    /// kaldırılır, profil cache'i temizlenir.
    /// </summary>
    private async Task RemoveContentAsync(ReportRecord report, DateTime now, CancellationToken cancellationToken)
    {
        switch (report.TargetType)
        {
            case ReportTargetType.Event:
                var ev = await events.GetForUpdateAsync(report.TargetId, cancellationToken)
                    ?? throw new NotFoundException("report.target_not_found", "Etkinlik bulunamadı.");
                await cleanup.CloseAsync(ev, removedByModerator: true, now, cancellationToken);
                mediaChanges.DeleteAfterCommit(await media.SetEventImageAsync(ev.Id, null, cancellationToken));
                break;
            case ReportTargetType.Message:
                await contentRemover.RemoveMessageAsync(report.TargetId, now, cancellationToken);
                break;
            case ReportTargetType.User:
                mediaChanges.DeleteAfterCommit(await media.SetUserAvatarAsync(report.TargetId, null, cancellationToken));
                await users.ClearBioAsync(report.TargetId, cancellationToken);
                cacheInvalidator.InvalidateAfterCommit(CacheTag.User(report.TargetId));
                break;
        }
    }

    /// <summary>
    /// Şikayet edilen içeriğin sahibini bulur: etkinliğin organizatörü, kullanıcının kendisi ya da mesajın
    /// yazarı. Bulunamazsa 404 fırlatır.
    /// </summary>
    private async Task<Guid> GetOwnerAsync(ReportRecord report, CancellationToken cancellationToken) =>
        report.TargetType switch
        {
            ReportTargetType.Event => (await events.GetForUpdateAsync(report.TargetId, cancellationToken))?.OrganizerId,
            ReportTargetType.User => report.TargetId,
            ReportTargetType.Message => await targets.GetMessageAuthorAsync(report.TargetId, cancellationToken),
            _ => null
        } ?? throw new NotFoundException("report.target_not_found", "İçeriğin sahibi bulunamadı.");
}

/// <summary>Mesaj gibi ayrı tablolardaki içeriği kaldırır.</summary>
public interface IModerationContentRemover
{
    /// <summary>Sohbet mesajını yönetici kararıyla silinmiş olarak işaretler.</summary>
    Task RemoveMessageAsync(Guid messageId, DateTime nowUtc, CancellationToken cancellationToken);
}
