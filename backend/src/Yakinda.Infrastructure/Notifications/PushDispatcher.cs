using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Yakinda.Domain.Notifications;

namespace Yakinda.Infrastructure.Notifications;

/// <summary>
/// Push outbox'ını boşaltır: notifications tablosunda push'u işlenmemiş kayıtları alır,
/// kullanıcının kayıtlı cihazlarına Expo Push ile gönderir ve işlendi olarak işaretler.
///  * Gönderilecek cihazı olmayan (ör. Expo Go kullanan) kullanıcıların bildirimleri hemen işlendi sayılır.
///  * Expo'ya ulaşılamazsa kayıtlar bekletilir, artan aralıklarla (en fazla 5 dk) tekrar denenir;
///    MaxAttempts denemeden sonra vazgeçilir. Bildirim uygulama içi listede her durumda durur.
///  * "DeviceNotRegistered" dönen token'lar (uygulama silinmiş) silinir.
///  * Birden fazla API instance'ı FOR UPDATE SKIP LOCKED ile aynı kayıtları paylaşmadan işler.
/// </summary>
internal sealed class PushDispatcher(
    NpgsqlDataSource dataSource,
    ExpoPushClient expo,
    IOptions<PushOptions> options,
    TimeProvider timeProvider,
    ILogger<PushDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private const int MaxAttempts = 8;

    /// <summary>API uzun süre kapalı kaldıysa eski bildirimler telefona push'lanmaz (listede yine görünür).</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Push bildirimleri kapalı (Push:Enabled = false).");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        var delay = pollInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await DispatchOnceAsync(stoppingToken);

                delay = result.HadFailures
                    ? Backoff(delay, pollInterval)
                    : result.Processed == BatchSize
                        ? TimeSpan.Zero // sırada daha fazlası olabilir, beklemeden devam
                        : pollInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Push bildirimleri işlenemedi.");
                delay = Backoff(delay, pollInterval);
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, timeProvider, stoppingToken);
        }
    }

    private static TimeSpan Backoff(TimeSpan current, TimeSpan pollInterval)
    {
        var next = current < pollInterval ? pollInterval * 2 : current * 2;
        return next > MaxBackoff ? MaxBackoff : next;
    }

    private async Task<DispatchResult> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var pending = (await connection.QueryAsync<PendingPush>(new CommandDefinition($"""
            SELECT id, user_id, type, event_id, title, body, created_at
            FROM notifications
            WHERE push_processed_at IS NULL
            ORDER BY created_at
            LIMIT {BatchSize}
            FOR UPDATE SKIP LOCKED
            """, transaction: transaction, cancellationToken: cancellationToken))).AsList();

        if (pending.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DispatchResult(0, HadFailures: false);
        }

        var fresh = pending.Where(p => now - p.CreatedAt <= MaxAge).ToList();
        var userIds = fresh.Select(p => p.UserId).Distinct().ToArray();

        var tokens = userIds.Length == 0
            ? []
            : (await connection.QueryAsync<DeviceToken>(new CommandDefinition(
                "SELECT token, user_id FROM device_push_tokens WHERE user_id = ANY(@UserIds)",
                new { UserIds = userIds },
                transaction,
                cancellationToken: cancellationToken))).AsList();

        var tokensByUser = tokens.ToLookup(t => t.UserId, t => t.Token);

        var outgoing = new List<(ExpoPushMessage Message, Guid NotificationId)>();
        foreach (var notification in fresh)
        {
            foreach (var token in tokensByUser[notification.UserId])
            {
                var data = new Dictionary<string, string?>
                {
                    ["notificationId"] = notification.Id.ToString(),
                    ["eventId"] = notification.EventId?.ToString(),
                    ["type"] = notification.Type.ToString()
                };
                outgoing.Add((new ExpoPushMessage(token, notification.Title, notification.Body, data), notification.Id));
            }
        }

        var failed = new HashSet<Guid>();
        var deadTokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in outgoing.Chunk(ExpoPushClient.MaxBatchSize))
        {
            try
            {
                var tickets = await expo.SendAsync(chunk.Select(c => c.Message).ToList(), cancellationToken);

                for (var i = 0; i < chunk.Length && i < tickets.Count; i++)
                {
                    var ticket = tickets[i];
                    if (ticket.IsDeviceNotRegistered)
                        deadTokens.Add(chunk[i].Message.To);
                    else if (ticket.Status == "error")
                        logger.LogWarning("Push reddedildi: {Error} {Message}", ticket.Details?.Error, ticket.Message);
                }
            }
            catch (Exception ex) when (
                (ex is HttpRequestException or JsonException or TaskCanceledException)
                && !cancellationToken.IsCancellationRequested)
            {
                // Aynı bildirimin başka bir cihaza giden kopyası başarılı olmuş olabilir; tekrar denemede
                // o cihaz bildirimi iki kez alabilir. Bu ölçekte kabul edilebilir bir ödünleşim.
                logger.LogWarning(ex, "Expo Push servisine ulaşılamadı; {Count} mesaj tekrar denenecek.", chunk.Length);
                foreach (var item in chunk)
                    failed.Add(item.NotificationId);
            }
        }

        var done = pending.Select(p => p.Id).Where(id => !failed.Contains(id)).ToArray();

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE notifications SET push_processed_at = @Now WHERE id = ANY(@Ids)",
            new { Now = now, Ids = done },
            transaction,
            cancellationToken: cancellationToken));

        if (failed.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE notifications
                   SET push_attempts     = push_attempts + 1,
                       push_processed_at = CASE WHEN push_attempts + 1 >= @MaxAttempts THEN @Now END
                 WHERE id = ANY(@Ids)
                """,
                new { Now = now, Ids = failed.ToArray(), MaxAttempts = (short)MaxAttempts },
                transaction,
                cancellationToken: cancellationToken));
        }

        if (deadTokens.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM device_push_tokens WHERE token = ANY(@Tokens)",
                new { Tokens = deadTokens.ToArray() },
                transaction,
                cancellationToken: cancellationToken));

            logger.LogInformation("{Count} geçersiz push token'ı silindi.", deadTokens.Count);
        }

        await transaction.CommitAsync(cancellationToken);

        if (outgoing.Count > 0)
        {
            logger.LogInformation(
                "Push: {Notifications} bildirim, {Messages} mesaj gönderildi ({Failed} başarısız)",
                pending.Count - failed.Count,
                outgoing.Count,
                failed.Count);
        }

        return new DispatchResult(pending.Count, HadFailures: failed.Count > 0);
    }

    private readonly record struct DispatchResult(int Processed, bool HadFailures);
}

internal sealed class PendingPush
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public Guid? EventId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

internal sealed class DeviceToken
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}
