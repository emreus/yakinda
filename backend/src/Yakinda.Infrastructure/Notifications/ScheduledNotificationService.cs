using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;

namespace Yakinda.Infrastructure.Notifications;

/// <summary>
/// Zamana bağlı bildirimleri üretir; dakikada bir çalışır:
///  * Hatırlatma: başlamasına 1 saatten az kalan etkinliklerin onaylı katılımcılarına ve organizatörüne.
///  * Puanlama: biten etkinliklerin, henüz oy vermemiş onaylı katılımcılarına.
/// Her etkinlik için bir kez gönderilir (events.reminder_sent_at / rating_prompt_sent_at).
/// Birden fazla API instance'ı çalışıyorsa FOR UPDATE SKIP LOCKED sayesinde aynı etkinliği
/// iki instance birden işlemez. Üretilen bildirimleri PushDispatcher telefonlara gönderir.
/// </summary>
internal sealed class ScheduledNotificationService(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider,
    ILogger<ScheduledNotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 200;

    /// <summary>
    /// Servisin ana döngüsü: açılışta bir kez, sonra dakikada bir RunOnceAsync çalıştırır. Bir turdaki hata
    /// loglanır ve döngü sürer; uygulama kapanırken döngüden çıkılır.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Zamanlanmış bildirimler üretilemedi; bir dakika sonra tekrar denenecek.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Bir tur çalıştırır. Entegrasyon testleri doğrudan çağırır.</summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var reminders = await CreateRemindersAsync(connection, transaction, now, cancellationToken);
        var ratingPrompts = await CreateRatingPromptsAsync(connection, transaction, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (reminders > 0 || ratingPrompts > 0)
        {
            logger.LogInformation(
                "Zamanlanmış bildirimler üretildi: {Reminders} hatırlatma, {RatingPrompts} puanlama",
                reminders,
                ratingPrompts);
        }
    }

    /// <summary>
    /// Başlamasına 1 saatten az kalan, aktif ve hatırlatması henüz gönderilmemiş etkinlikleri (en fazla 200)
    /// FOR UPDATE SKIP LOCKED ile seçer, reminder_sent_at alanını doldurur ve onaylı katılımcılarla
    /// organizatöre hatırlatma bildirimi ekler. Üretilen bildirim sayısını döner.
    /// </summary>
    private static async Task<int> CreateRemindersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var due = (await connection.QueryAsync<DueEvent>(new CommandDefinition($"""
            UPDATE events e
               SET reminder_sent_at = @Now
              FROM (
                    SELECT id
                    FROM events
                    WHERE status = 0
                      AND reminder_sent_at IS NULL
                      AND starts_at >  @Now
                      AND starts_at <= @Until
                    ORDER BY starts_at
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                   ) due
             WHERE e.id = due.id
            RETURNING e.id, e.title, e.organizer_id
            """,
            new { Now = now, Until = now + NotificationTexts.ReminderLeadTime },
            transaction,
            cancellationToken: cancellationToken))).AsList();

        var created = 0;
        foreach (var ev in due)
        {
            var content = NotificationTexts.EventReminder(ev.Title);

            // Onaylı katılımcılar + organizatör (UNION tekrarı engeller)
            created += await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO notifications (id, user_id, type, event_id, title, body, created_at)
                SELECT gen_random_uuid(), r.user_id, @Type, @EventId, @Title, @Body, @Now
                FROM (
                      SELECT p.user_id
                      FROM event_participants p
                      WHERE p.event_id = @EventId AND p.status = @Approved
                      UNION
                      SELECT @OrganizerId
                     ) r
                """,
                new
                {
                    Type = (short)content.Type,
                    EventId = ev.Id,
                    ev.OrganizerId,
                    Approved = (short)ParticipantStatus.Approved,
                    content.Title,
                    content.Body,
                    Now = now
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        return created;
    }

    /// <summary>
    /// Bitmiş, oylama penceresi hâlâ açık ve puanlama bildirimi henüz gönderilmemiş etkinlikleri (en fazla
    /// 200) seçer, rating_prompt_sent_at alanını doldurur ve henüz oy vermemiş onaylı katılımcılara "puanla"
    /// bildirimi ekler. Üretilen bildirim sayısını döner.
    /// </summary>
    private static async Task<int> CreateRatingPromptsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Bitiş saati girilmemişse etkinlik başlangıç + varsayılan süre (3 saat) sonra bitmiş sayılır.
        // Oylama penceresi kapanmış eski etkinliklere bildirim gönderilmez.
        // starts_at koşulları indeksin kullanılabilmesi için (bitmiş etkinlik zaten başlamıştır).
        var due = (await connection.QueryAsync<DueEvent>(new CommandDefinition($"""
            UPDATE events e
               SET rating_prompt_sent_at = @Now
              FROM (
                    SELECT id
                    FROM events
                    WHERE status = 0
                      AND rating_prompt_sent_at IS NULL
                      AND starts_at <= @Now
                      AND starts_at >  @Now - @RatingWindow - @MaxDuration
                      AND COALESCE(ends_at, starts_at + @DefaultDuration) <= @Now
                      AND COALESCE(ends_at, starts_at + @DefaultDuration) >  @Now - @RatingWindow
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                   ) due
             WHERE e.id = due.id
            RETURNING e.id, e.title, e.organizer_id
            """,
            new
            {
                Now = now,
                DefaultDuration = EventRules.DefaultDuration,
                RatingWindow = EventRules.RatingWindowAfterEnd,
                MaxDuration = EventRules.MaxDuration
            },
            transaction,
            cancellationToken: cancellationToken))).AsList();

        var created = 0;
        foreach (var ev in due)
        {
            var content = NotificationTexts.RatingPrompt(ev.Title);

            created += await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO notifications (id, user_id, type, event_id, title, body, created_at)
                SELECT gen_random_uuid(), p.user_id, @Type, p.event_id, @Title, @Body, @Now
                FROM event_participants p
                WHERE p.event_id = @EventId
                  AND p.status = @Approved
                  AND NOT EXISTS (
                        SELECT 1 FROM event_ratings r
                        WHERE r.event_id = p.event_id AND r.rater_id = p.user_id)
                """,
                new
                {
                    Type = (short)content.Type,
                    EventId = ev.Id,
                    Approved = (short)ParticipantStatus.Approved,
                    content.Title,
                    content.Body,
                    Now = now
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        return created;
    }
}

internal sealed class DueEvent
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid OrganizerId { get; set; }
}
