using Dapper;
using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events;
using Yakinda.Application.Events.Common;
using Yakinda.Application.Exceptions;
using Yakinda.Application.Moderation;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Common;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Chat;

/// <summary>
/// Etkinlik sohbeti. Organizatör ve onaylı katılımcılar okur ve yazar; bekleyenler ve bekleme listesi göremez.
/// İptal edilen etkinliğin sohbeti okunabilir ama yazılamaz; kaldırılan etkinliğin sohbeti kapanır.
/// Aralarında engel olan kişiler birbirinin mesajlarını görmez.
/// </summary>
public sealed class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Listede dolar; gönderme yanıtında (kendi mesajın) boş gelir.</summary>
    public string? AuthorAvatarUrl { get; set; }

    /// <summary>Silinen mesajda null.</summary>
    public string? Body { get; set; }

    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsMine { get; set; }
}

public sealed record ChatPage(IReadOnlyList<ChatMessageDto> Items, bool HasMore);

internal sealed class ChatAccessRow
{
    public Guid OrganizerId { get; set; }
    public EventStatus Status { get; set; }
    public ParticipantStatus? MyStatus { get; set; }
}

internal static class ChatAccess
{
    public const int MaxBodyLength = 1000;

    private const string Sql = """
        SELECT e.organizer_id, e.status,
               (SELECT p.status FROM event_participants p WHERE p.event_id = e.id AND p.user_id = @UserId) AS my_status
        FROM events e
        WHERE e.id = @EventId
        """;

    /// <summary>
    /// Kullanıcının etkinlik sohbetine erişebildiğini doğrular ve etkinlik bilgisini döner. Etkinlik yoksa
    /// ya da kaldırıldıysa 404; kullanıcı organizatör ya da onaylı katılımcı değilse 403 (chat.not_member)
    /// fırlatır.
    /// </summary>
    public static async Task<ChatAccessRow> RequireMemberAsync(
        System.Data.Common.DbConnection connection,
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ChatAccessRow>(
            new CommandDefinition(Sql, new { EventId = eventId, UserId = userId }, cancellationToken: cancellationToken));

        if (row is null || row.Status == EventStatus.Removed)
            throw EventErrors.NotFound();

        var isMember = row.OrganizerId == userId || row.MyStatus == ParticipantStatus.Approved;
        if (!isMember)
            throw new ForbiddenException("chat.not_member", "Sohbeti sadece organizatör ve onaylı katılımcılar görebilir.");

        return row;
    }
}

// ===================================================================== Mesajları oku

/// <summary>
/// Sayfalama mesaj id'si üzerinden (UUIDv7 zaman sıralı):
///  * before: daha eski mesajlar (yukarı kaydırınca)
///  * after: daha yeni mesajlar (açık sohbet birkaç saniyede bir sorar)
/// Her durumda en yeni önce döner.
/// </summary>
public sealed record GetChatMessagesQuery(Guid EventId, Guid? Before = null, Guid? After = null, int Limit = 30) : IQuery<ChatPage>;

internal sealed class GetChatMessagesQueryValidator : AbstractValidator<GetChatMessagesQuery>
{
    /// <summary>Limit 1 ile 100 arasında olmalı; before ve after birlikte verilemez.</summary>
    public GetChatMessagesQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => x.Before is null || x.After is null).WithMessage("before ve after birlikte kullanılamaz.");
    }
}

internal sealed class GetChatMessagesQueryHandler(IDbConnectionFactory db, ICurrentUser currentUser)
    : IRequestHandler<GetChatMessagesQuery, ChatPage>
{
    /// <summary>
    /// Sohbet mesajlarını en yeni önce döner. Önce isteği yapanın sohbet üyesi olduğunu denetler. before
    /// verilirse o mesajdan eskileri, after verilirse yenilerini getirir. Yazarıyla arada engel olan
    /// mesajlar gelmez; silinmiş mesajın metni boş döner. Bir fazla satır isteyerek HasMore belirlenir.
    /// </summary>
    public async Task<ChatPage> Handle(GetChatMessagesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        await ChatAccess.RequireMemberAsync(connection, request.EventId, userId, cancellationToken);

        // "after" yeni mesajları eskiden yeniye çeker (kaçırmamak için), sonra ters çevrilir.
        var ascending = request.After is not null;
        var sql = $"""
            SELECT m.id,
                   m.author_id,
                   u.display_name AS author_name,
                   {Media.MediaUrl.Sql("u.avatar_key")} AS author_avatar_url,
                   CASE WHEN m.deleted_at IS NULL THEN m.body END AS body,
                   m.created_at,
                   (m.deleted_at IS NOT NULL) AS is_deleted,
                   (m.author_id = @UserId) AS is_mine
            FROM event_messages m
            JOIN users u ON u.id = m.author_id
            WHERE m.event_id = @EventId
              AND (@Before::uuid IS NULL OR m.id < @Before::uuid)
              AND (@After::uuid IS NULL OR m.id > @After::uuid)
              AND NOT {Safety.BlockSql.EitherWay("@UserId", "m.author_id")}
            ORDER BY m.id {(ascending ? "ASC" : "DESC")}
            LIMIT @Limit
            """;

        var rows = (await connection.QueryAsync<ChatMessageDto>(new CommandDefinition(
            sql,
            new { request.EventId, UserId = userId, request.Before, request.After, Limit = request.Limit + 1 },
            cancellationToken: cancellationToken))).AsList();

        var hasMore = rows.Count > request.Limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);
        if (ascending)
            rows.Reverse();

        return new ChatPage(rows, hasMore);
    }
}

// ===================================================================== Mesaj gönder

public sealed record SendChatMessageCommand(Guid EventId, string Body) : ICommand<ChatMessageDto>;

internal sealed class SendChatMessageCommandValidator : AbstractValidator<SendChatMessageCommand>
{
    /// <summary>Mesaj dolu, en fazla 1000 karakter olmalı ve içerik filtresinden geçmeli.</summary>
    public SendChatMessageCommandValidator() =>
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ChatAccess.MaxBodyLength).NotObjectionable().WithName("Mesaj");
}

internal sealed class SendChatMessageCommandHandler(
    IChatRepository chat,
    IEventRepository events,
    IParticipantRepository participants,
    IUserRepository users,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<SendChatMessageCommand, ChatMessageDto>
{
    /// <summary>
    /// Sohbete mesaj yazar. Etkinliği kilitsiz okur; üye değilse 403, etkinlik aktif değilse 422 döner.
    /// Mesajı zaman sıralı bir kimlikle (UUIDv7) kaydeder, diğer üyelere bildirim ekler (okunmamış sohbet
    /// bildirimi olana yenisi eklenmez) ve kaydedilen mesajı döner.
    /// </summary>
    public async Task<ChatMessageDto> Handle(SendChatMessageCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var ev = await events.GetAsync(request.EventId, cancellationToken);
        if (ev is null || ev.Status == EventStatus.Removed)
            throw EventErrors.NotFound();

        var isMember = ev.IsOrganizer(userId)
                       || (await participants.GetAsync(ev.Id, userId, cancellationToken))?.Status == ParticipantStatus.Approved;
        if (!isMember)
            throw new ForbiddenException("chat.not_member", "Sohbeti sadece organizatör ve onaylı katılımcılar görebilir.");

        if (ev.Status != EventStatus.Active)
            throw new DomainException("chat.closed", "Bu etkinlik iptal edildiği için sohbete mesaj yazılamaz.");

        var body = request.Body.Trim();
        var id = Guid.CreateVersion7();
        await chat.AddAsync(id, request.EventId, userId, body, now, cancellationToken);

        var author = await users.GetByIdAsync(userId, cancellationToken);
        await chat.NotifyMembersAsync(
            request.EventId,
            userId,
            NotificationTexts.ChatMessage(author?.DisplayName ?? "Biri", ev.Title, body),
            now,
            cancellationToken);

        return new ChatMessageDto
        {
            Id = id,
            AuthorId = userId,
            AuthorName = author?.DisplayName ?? string.Empty,
            Body = body,
            CreatedAt = now,
            IsDeleted = false,
            IsMine = true
        };
    }
}

// ===================================================================== Mesaj sil

/// <summary>Yazarı ya da etkinliğin organizatörü siler (yumuşak silme: "Mesaj silindi").</summary>
public sealed record DeleteChatMessageCommand(Guid EventId, Guid MessageId) : ICommand<Unit>;

internal sealed class DeleteChatMessageCommandHandler(
    IChatRepository chat,
    IEventRepository events,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<DeleteChatMessageCommand, Unit>
{
    /// <summary>
    /// Mesajı siler (satır kalır, silinmiş olarak işaretlenir). Mesaj yoksa ya da başka bir etkinliğe
    /// aitse 404 döner; yalnızca yazarı ya da etkinliğin organizatörü silebilir, başkası 403 alır.
    /// </summary>
    public async Task<Unit> Handle(DeleteChatMessageCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var ownership = await chat.GetOwnershipAsync(request.MessageId, cancellationToken);
        if (ownership is null || ownership.Value.EventId != request.EventId)
            throw new NotFoundException("chat.message_not_found", "Mesaj bulunamadı.");

        var ev = await events.GetAsync(request.EventId, cancellationToken) ?? throw EventErrors.NotFound();
        if (ownership.Value.AuthorId != userId && ev.OrganizerId != userId)
            throw new ForbiddenException("chat.cannot_delete", "Sadece kendi mesajını silebilirsin.");

        await chat.SoftDeleteAsync(request.MessageId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return Unit.Value;
    }
}

// ===================================================================== Okundu

/// <summary>Sohbet açıldığında: bu etkinliğin sohbet bildirimleri okundu, bir sonraki mesaj yeniden bildirim üretir.</summary>
public sealed record MarkChatReadCommand(Guid EventId) : ICommand<Unit>;

internal sealed class MarkChatReadCommandHandler(IChatRepository chat, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<MarkChatReadCommand, Unit>
{
    /// <summary>
    /// Sohbet açıldığında o etkinliğe ait sohbet bildirimlerini okundu yapar; böylece bir sonraki mesaj
    /// yeniden bildirim üretir.
    /// </summary>
    public async Task<Unit> Handle(MarkChatReadCommand request, CancellationToken cancellationToken)
    {
        await chat.MarkNotificationsReadAsync(
            request.EventId,
            currentUser.RequireUserId(),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        return Unit.Value;
    }
}
