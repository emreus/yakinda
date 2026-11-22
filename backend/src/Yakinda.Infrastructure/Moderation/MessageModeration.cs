using Yakinda.Application.Safety;
using Yakinda.Infrastructure.Persistence;
using Yakinda.Infrastructure.Persistence.Repositories;

namespace Yakinda.Infrastructure.Moderation;

/// <summary>Sohbet mesajı şikayetleri: mesajın yazarını bulur, yönetici kararıyla mesajı siler.</summary>
internal sealed class MessageModeration(DbSession session) : RepositoryBase(session), IReportTargetLookup, IModerationContentRemover
{
    public Task<Guid?> GetMessageAuthorAsync(Guid messageId, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<Guid?>(
            "SELECT author_id FROM event_messages WHERE id = @Id",
            new { Id = messageId },
            cancellationToken);

    public Task RemoveMessageAsync(Guid messageId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE event_messages SET deleted_at = COALESCE(deleted_at, @Now) WHERE id = @Id",
            new { Id = messageId, Now = nowUtc },
            cancellationToken);
}
