using Yakinda.Domain.Common;
using Yakinda.Domain.Events;

namespace Yakinda.Domain.Tests;

public sealed class EventTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Organizer = Guid.NewGuid();

    private static Event NewEvent(
        int? capacity = null,
        JoinPolicy policy = JoinPolicy.Open,
        DateTime? startsAt = null,
        DateTime? endsAt = null) =>
        Event.Create(
            Organizer,
            categoryId: 1,
            title: "  Okey 4. aranıyor  ",
            description: null,
            startsAtUtc: startsAt ?? Now.AddDays(1),
            endsAtUtc: endsAt,
            latitude: 41.0082,
            longitude: 28.9784,
            placeName: null,
            address: null,
            capacity,
            policy,
            Now);

    private static void AssertDomainError(string code, Action action)
    {
        var ex = Assert.Throws<DomainException>(action);
        Assert.Equal(code, ex.Code);
    }

    // ---------------------------------------------------------------- Oluşturma

    [Fact]
    public void Create_trims_title_and_starts_active_and_empty()
    {
        var ev = NewEvent();

        Assert.Equal("Okey 4. aranıyor", ev.Title);
        Assert.Equal(EventStatus.Active, ev.Status);
        Assert.Equal(0, ev.ParticipantCount);
        Assert.Null(ev.AverageRating);
    }

    [Fact]
    public void Create_rejects_start_in_the_past() =>
        AssertDomainError("event.start_in_past", () => NewEvent(startsAt: Now.AddMinutes(-1)));

    [Fact]
    public void Create_rejects_end_before_start() =>
        AssertDomainError("event.invalid_end", () => NewEvent(startsAt: Now.AddDays(1), endsAt: Now.AddHours(1)));

    [Fact]
    public void Create_rejects_events_longer_than_seven_days() =>
        AssertDomainError("event.too_long", () => NewEvent(startsAt: Now.AddDays(1), endsAt: Now.AddDays(9)));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(10_001)]
    public void Create_rejects_invalid_capacity(int capacity) =>
        AssertDomainError("event.invalid_capacity", () => NewEvent(capacity: capacity));

    // ---------------------------------------------------------------- Katılım

    [Fact]
    public void Admit_to_open_event_approves_immediately_and_counts()
    {
        var ev = NewEvent();

        var status = ev.Admit(Guid.NewGuid(), Now);

        Assert.Equal(ParticipantStatus.Approved, status);
        Assert.Equal(1, ev.ParticipantCount);
    }

    [Fact]
    public void Admit_to_approval_event_is_pending_and_does_not_count()
    {
        var ev = NewEvent(policy: JoinPolicy.ApprovalRequired);

        var status = ev.Admit(Guid.NewGuid(), Now);

        Assert.Equal(ParticipantStatus.Pending, status);
        Assert.Equal(0, ev.ParticipantCount);
    }

    [Theory]
    [InlineData(JoinPolicy.Open)]
    [InlineData(JoinPolicy.ApprovalRequired)]
    public void Admit_to_full_event_goes_to_waitlist(JoinPolicy policy)
    {
        var ev = NewEvent(capacity: 1, policy: policy);
        if (policy == JoinPolicy.Open)
            ev.Admit(Guid.NewGuid(), Now);
        else
            ev.AddApprovedParticipant(); // organizatör birini onayladı

        Assert.True(ev.IsFull);
        Assert.Equal(ParticipantStatus.Waitlisted, ev.Admit(Guid.NewGuid(), Now));
        Assert.Equal(1, ev.ParticipantCount);
    }

    [Fact]
    public void Approving_into_a_full_event_fails()
    {
        var ev = NewEvent(capacity: 1, policy: JoinPolicy.ApprovalRequired);
        ev.AddApprovedParticipant();
        AssertDomainError("event.full", ev.AddApprovedParticipant);
    }

    // ---------------------------------------------------------------- Düzenleme

    private static EventChanges Update(Event ev, DateTime? startsAt = null, int? capacity = null,
        double latitude = 41.0082, string? placeName = null, DateTime? now = null) =>
        ev.Update(1, "Yeni başlık", null, startsAt ?? ev.StartsAt, ev.EndsAt, latitude, 28.9784,
            placeName, null, capacity, now ?? Now);

    [Fact]
    public void Update_reports_what_participants_need_to_know()
    {
        var ev = NewEvent(capacity: 2);

        var none = Update(ev, capacity: 2);
        Assert.False(none.ShouldNotifyParticipants);
        Assert.Equal("Yeni başlık", ev.Title);

        var time = Update(ev, startsAt: Now.AddDays(2), capacity: 2);
        Assert.True(time.TimeChanged);
        Assert.False(time.PlaceChanged);

        var place = Update(ev, latitude: 41.1, capacity: 2);
        Assert.True(place.PlaceChanged);

        var bigger = Update(ev, latitude: 41.1, capacity: 5);
        Assert.True(bigger.CapacityIncreased);
        Assert.False(bigger.ShouldNotifyParticipants);
    }

    [Fact]
    public void Capacity_cannot_drop_below_current_participants()
    {
        var ev = NewEvent(capacity: 3);
        ev.Admit(Guid.NewGuid(), Now);
        ev.Admit(Guid.NewGuid(), Now);

        AssertDomainError("event.capacity_below_participants", () => Update(ev, capacity: 1));
    }

    [Fact]
    public void Started_or_cancelled_events_cannot_be_edited()
    {
        var started = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("event.already_started", () => Update(started, now: Now.AddHours(2)));

        var cancelled = NewEvent();
        cancelled.Cancel(Now);
        AssertDomainError("event.cancelled", () => Update(cancelled));
    }

    [Fact]
    public void Organizer_cannot_join_own_event() =>
        AssertDomainError("event.organizer_cannot_join", () => NewEvent().Admit(Organizer, Now));

    [Fact]
    public void Nobody_can_join_after_start()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("event.already_started", () => ev.Admit(Guid.NewGuid(), Now.AddHours(2)));
    }

    [Fact]
    public void Removing_participants_never_goes_below_zero()
    {
        var ev = NewEvent();
        ev.RemoveApprovedParticipant();
        Assert.Equal(0, ev.ParticipantCount);
    }

    // ---------------------------------------------------------------- İptal

    [Fact]
    public void Cancelled_event_rejects_new_participants()
    {
        var ev = NewEvent();
        ev.Cancel(Now);

        Assert.Equal(EventStatus.Cancelled, ev.Status);
        AssertDomainError("event.cancelled", () => ev.Admit(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Started_event_cannot_be_cancelled()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("event.already_started", () => ev.Cancel(Now.AddHours(2)));
    }

    [Fact]
    public void Event_cannot_be_cancelled_twice()
    {
        var ev = NewEvent();
        ev.Cancel(Now);
        AssertDomainError("event.cancelled", () => ev.Cancel(Now));
    }

    // ---------------------------------------------------------------- Oylama

    [Fact]
    public void Organizer_cannot_rate_own_event()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("rating.own_event",
            () => ev.EnsureCanBeRatedBy(Organizer, null, Now.AddHours(2)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ParticipantStatus.Pending)]
    [InlineData(ParticipantStatus.Rejected)]
    [InlineData(ParticipantStatus.Left)]
    public void Only_approved_participants_can_rate(ParticipantStatus? status)
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("rating.not_participant",
            () => ev.EnsureCanBeRatedBy(Guid.NewGuid(), status, Now.AddHours(2)));
    }

    [Fact]
    public void Rating_opens_when_event_starts()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        var participant = Guid.NewGuid();

        AssertDomainError("rating.not_open_yet",
            () => ev.EnsureCanBeRatedBy(participant, ParticipantStatus.Approved, Now.AddMinutes(30)));

        ev.EnsureCanBeRatedBy(participant, ParticipantStatus.Approved, Now.AddHours(1)); // tam başlangıçta açık
    }

    [Fact]
    public void Rating_closes_fourteen_days_after_default_end()
    {
        var start = Now.AddHours(1);
        var ev = NewEvent(startsAt: start); // bitiş yok → başlangıç + 3 saat
        var participant = Guid.NewGuid();
        var closesAt = start + EventRules.DefaultDuration + EventRules.RatingWindowAfterEnd;

        ev.EnsureCanBeRatedBy(participant, ParticipantStatus.Approved, closesAt);
        AssertDomainError("rating.closed",
            () => ev.EnsureCanBeRatedBy(participant, ParticipantStatus.Approved, closesAt.AddMinutes(1)));
    }

    [Fact]
    public void Cancelled_event_cannot_be_rated()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        ev.Cancel(Now);
        AssertDomainError("event.cancelled",
            () => ev.EnsureCanBeRatedBy(Guid.NewGuid(), ParticipantStatus.Approved, Now.AddHours(2)));
    }

    [Fact]
    public void First_rating_adds_score_and_count()
    {
        var ev = NewEvent();

        var delta = ev.ApplyRating(4, previousScore: null);

        Assert.Equal(new RatingDelta(4, 1), delta);
        Assert.Equal(4, ev.RatingSum);
        Assert.Equal(1, ev.RatingCount);
        Assert.Equal(4.0, ev.AverageRating);
    }

    [Fact]
    public void Changing_a_rating_only_moves_the_sum()
    {
        var ev = NewEvent();
        ev.ApplyRating(5, previousScore: null);
        ev.ApplyRating(3, previousScore: null);

        var delta = ev.ApplyRating(1, previousScore: 5); // ilk oy 5 → 1

        Assert.Equal(new RatingDelta(-4, 0), delta);
        Assert.Equal(4, ev.RatingSum);
        Assert.Equal(2, ev.RatingCount);
        Assert.Equal(2.0, ev.AverageRating);
    }
}
