using Yakinda.Application.Safety;
using Yakinda.Infrastructure.Persistence;
using Yakinda.Infrastructure.Persistence.Repositories;
using Yakinda.Domain.Notifications;

namespace Yakinda.Infrastructure.Moderation;

/// <summary>Sohbet mesajı şikayetleri: mesajın yazarını bulur, yönetici kararıyla mesajı siler.</summary>
internal sealed class MessageModeration(DbSession session) : RepositoryBase(session), IReportTargetLookup, IModerationContentRemover
{
    /// <summary>Sohbet mesajının yazarını okur; mesaj yoksa null döner.</summary>
    public Task<Guid?> GetMessageAuthorAsync(Guid messageId, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<Guid?>(
            "SELECT author_id FROM event_messages WHERE id = @Id",
            new { Id = messageId },
            cancellationToken);

    /// <summary>
    /// Mesajı yönetici kararıyla siler; sohbetteki silmeyle aynı SQL kullanılır (mesaj silinmiş işaretlenir,
    /// bildirimdeki önizleme kaldırılır).
    /// </summary>
    public Task RemoveMessageAsync(Guid messageId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            ChatRepository.SoftDeleteSql,
            new { Id = messageId, Now = nowUtc, ChatType = (short)NotificationType.ChatMessage },
            cancellationToken);
}
