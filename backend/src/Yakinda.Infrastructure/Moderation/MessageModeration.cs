using Yakinda.Application.Safety;

namespace Yakinda.Infrastructure.Moderation;

/// <summary>Sohbet mesajları eklenene kadar: mesaj şikayeti hedefi bulunamaz.</summary>
internal sealed class MessageModeration : IReportTargetLookup, IModerationContentRemover
{
    public Task<Guid?> GetMessageAuthorAsync(Guid messageId, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(null);

    public Task RemoveMessageAsync(Guid messageId, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
