using System.Net;
using System.Net.Http.Json;
using Yakinda.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Yakinda.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class EventFlowTests(ApiFixture api)
{
    // ------------------------------------------------------------------ yakındakiler

    [Fact]
    public async Task Nearby_returns_events_inside_the_radius_only()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        // Kuzey Kutbu'na yakın, diğer testlerin etkinliklerinden uzak bir nokta
        var near = await organizer.CreateEventAsync("Yakındaki", latitude: 80.0, longitude: 10.0);
        var far = await organizer.CreateEventAsync("Uzaktaki", latitude: 80.5, longitude: 10.0); // ~55 km

        var json = await (await organizer.Client.GetAsync("/api/events/nearby?lat=80.0&lng=10.01&radiusKm=10"))
            .ReadJsonAsync(HttpStatusCode.OK);
        var ids = json["items"]!.AsArray().Select(i => Guid.Parse(i!["id"]!.GetValue<string>())).ToList();

        Assert.Contains(near, ids);
        Assert.DoesNotContain(far, ids);
    }

    [Fact]
    public async Task Event_creation_validates_input()
    {
        var organizer = await api.RegisterAsync();

        var response = await organizer.Client.PostAsJsonAsync("/api/events", new
        {
            categoryId = 1,
            title = "",
            startsAt = DateTimeOffset.UtcNow.AddHours(-1),
            latitude = 41.0,
            longitude = 29.0,
            joinPolicy = "Open"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------ katılım + bildirim

    [Fact]
    public async Task Joining_an_open_event_approves_and_notifies_the_organizer()
    {
        var organizer = await api.RegisterAsync("Ayşe");
        var guest = await api.RegisterAsync("Burak");
        var eventId = await organizer.CreateEventAsync();

        var join = await (await guest.JoinAsync(eventId)).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Approved", join["status"]!.GetValue<string>());

        var detail = await (await guest.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal(1, detail["participantCount"]!.GetValue<int>());

        var notifications = await organizer.NotificationsAsync();
        Assert.Contains("ParticipantJoined", notifications.Types());
        Assert.Contains("Burak", notifications[0]!["body"]!.GetValue<string>());
    }

    [Fact]
    public async Task Approval_flow_notifies_both_sides_and_marks_read()
    {
        var organizer = await api.RegisterAsync("Ayşe");
        var guest = await api.RegisterAsync("Cem");
        var eventId = await organizer.CreateEventAsync(joinPolicy: "ApprovalRequired");

        var join = await (await guest.JoinAsync(eventId)).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Pending", join["status"]!.GetValue<string>());
        Assert.Equal(new[] { "JoinRequested" }, (await organizer.NotificationsAsync()).Types());

        await (await organizer.Client.PostAsync($"/api/events/{eventId}/participants/{guest.Id}/approve", null))
            .ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal(new[] { "RequestApproved" }, (await guest.NotificationsAsync()).Types());
        Assert.Equal(1, await guest.UnreadCountAsync());

        await (await guest.Client.PostAsync("/api/notifications/read-all", null)).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal(0, await guest.UnreadCountAsync());
    }

    [Fact]
    public async Task Users_cannot_mark_someone_elses_notification_read()
    {
        var organizer = await api.RegisterAsync();
        var guest = await api.RegisterAsync();
        await guest.JoinAsync(await organizer.CreateEventAsync());
        var notificationId = (await organizer.NotificationsAsync())[0]!["id"]!.GetValue<string>();

        var response = await guest.Client.PostAsync($"/api/notifications/{notificationId}/read", null);

        Assert.Equal("notification.not_found", await response.ProblemCodeAsync(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Full_event_rejects_new_participants()
    {
        var organizer = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync(capacity: 1);
        await (await (await api.RegisterAsync()).JoinAsync(eventId)).ExpectAsync(HttpStatusCode.OK);

        var late = await (await api.RegisterAsync()).JoinAsync(eventId);

        Assert.Equal("event.full", await late.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Organizer_cannot_join_own_event()
    {
        var organizer = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();

        var response = await organizer.JoinAsync(eventId);

        Assert.Equal("event.organizer_cannot_join", await response.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Cancelling_notifies_participants_and_closes_the_event()
    {
        var organizer = await api.RegisterAsync();
        var approved = await api.RegisterAsync();
        var pending = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync(joinPolicy: "ApprovalRequired");
        await approved.JoinAsync(eventId);
        await organizer.Client.PostAsync($"/api/events/{eventId}/participants/{approved.Id}/approve", null);
        await pending.JoinAsync(eventId);

        await (await organizer.Client.PostAsync($"/api/events/{eventId}/cancel", null)).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Contains("EventCancelled", (await approved.NotificationsAsync()).Types());
        Assert.Contains("EventCancelled", (await pending.NotificationsAsync()).Types());

        var late = await (await api.RegisterAsync()).JoinAsync(eventId);
        Assert.Equal("event.cancelled", await late.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    // ------------------------------------------------------------------ oylama

    [Fact]
    public async Task Ratings_update_event_and_organizer_averages()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        var first = await api.RegisterAsync();
        var second = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();
        await first.JoinAsync(eventId);
        await second.JoinAsync(eventId);
        await StartEventAsync(eventId);

        await Rate(first, eventId, 5);
        var result = await Rate(second, eventId, 2);
        Assert.Equal(3.5, result["eventAverageRating"]!.GetValue<double>());
        Assert.Equal(2, result["eventRatingCount"]!.GetValue<int>());

        // İlk oyu değiştirmek sayıyı artırmaz, sadece ortalamayı değiştirir: (3 + 2) / 2
        result = await Rate(first, eventId, 3);
        Assert.Equal(2.5, result["eventAverageRating"]!.GetValue<double>());
        Assert.Equal(2, result["eventRatingCount"]!.GetValue<int>());

        var profile = await (await first.Client.GetAsync($"/api/users/{organizer.Id}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal(2.5, profile["averageRating"]!.GetValue<double>());
        Assert.Equal(2, profile["ratingCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Only_participants_can_rate_and_not_before_start()
    {
        var organizer = await api.RegisterAsync();
        var participant = await api.RegisterAsync();
        var outsider = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();
        await participant.JoinAsync(eventId);

        var early = await participant.Client.PutAsJsonAsync($"/api/events/{eventId}/rating", new { score = 4 });
        Assert.Equal("rating.not_open_yet", await early.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));

        await StartEventAsync(eventId);

        var stranger = await outsider.Client.PutAsJsonAsync($"/api/events/{eventId}/rating", new { score = 4 });
        Assert.Equal("rating.not_participant", await stranger.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));

        var own = await organizer.Client.PutAsJsonAsync($"/api/events/{eventId}/rating", new { score = 5 });
        Assert.Equal("rating.own_event", await own.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    // ------------------------------------------------------------------ zamanlanmış bildirimler

    [Fact]
    public async Task Scheduler_sends_reminder_once_and_rating_prompt_after_the_event()
    {
        var organizer = await api.RegisterAsync();
        var participant = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();
        await participant.JoinAsync(eventId);
        var scheduler = CreateScheduler();

        // 30 dakika sonra başlıyor → hatırlatma (organizatöre ve katılımcıya), ikinci turda tekrar yok
        await api.ExecuteSqlAsync("UPDATE events SET starts_at = now() + interval '30 minutes' WHERE id = @id", new { id = eventId });
        await scheduler.RunOnceAsync(CancellationToken.None);
        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "EventReminder" }, (await participant.NotificationsAsync()).Types());
        Assert.Single((await organizer.NotificationsAsync()).Types().Where(t => t == "EventReminder"));

        // 5 saat önce başladı (varsayılan süre 3 saat) → bitti, puanlama bildirimi sadece katılımcıya
        await api.ExecuteSqlAsync("UPDATE events SET starts_at = now() - interval '5 hours' WHERE id = @id", new { id = eventId });
        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Contains("RatingPrompt", (await participant.NotificationsAsync()).Types());
        Assert.DoesNotContain("RatingPrompt", (await organizer.NotificationsAsync()).Types());
    }

    // ------------------------------------------------------------------ yardımcılar

    private ScheduledNotificationService CreateScheduler() =>
        new(api.Factory.Services.GetRequiredService<NpgsqlDataSource>(),
            TimeProvider.System,
            NullLogger<ScheduledNotificationService>.Instance);

    /// <summary>Oylama etkinlik başlayınca açılır; zamanı beklemek yerine başlangıcı geriye çeker.</summary>
    private Task StartEventAsync(Guid eventId) =>
        api.ExecuteSqlAsync("UPDATE events SET starts_at = now() - interval '1 hour' WHERE id = @id", new { id = eventId });

    private static async Task<System.Text.Json.Nodes.JsonNode> Rate(TestUser user, Guid eventId, int score) =>
        await (await user.Client.PutAsJsonAsync($"/api/events/{eventId}/rating", new { score, comment = "test" }))
            .ReadJsonAsync(HttpStatusCode.OK);
}
