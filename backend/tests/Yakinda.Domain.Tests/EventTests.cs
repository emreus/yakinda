using Yakinda.Domain.Common;
using Yakinda.Domain.Events;

namespace Yakinda.Domain.Tests;

public sealed class EventTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Organizer = Guid.NewGuid();

    /// <summary>
    /// Testlerin ortak etkinliği: yarın başlar, İstanbul koordinatlarındadır, başlığının başında ve sonunda
    /// boşluk vardır. Kontenjan, katılım şekli, başlangıç ve bitiş parametreyle değiştirilebilir.
    /// </summary>
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

    /// <summary>
    /// Verilen işlemin DomainException fırlattığını ve hata kodunun beklenen kod olduğunu doğrular.
    /// </summary>
    private static void AssertDomainError(string code, Action action)
    {
        var ex = Assert.Throws<DomainException>(action);
        Assert.Equal(code, ex.Code);
    }

    // ---------------------------------------------------------------- Oluşturma

    /// <summary>
    /// Yeni etkinlikte başlığın baştaki ve sondaki boşlukları atılır; etkinlik aktif, katılımcısız ve
    /// puansız başlar.
    /// </summary>
    [Fact]
    public void Create_trims_title_and_starts_active_and_empty()
    {
        var ev = NewEvent();

        Assert.Equal("Okey 4. aranıyor", ev.Title);
        Assert.Equal(EventStatus.Active, ev.Status);
        Assert.Equal(0, ev.ParticipantCount);
        Assert.Null(ev.AverageRating);
    }

    /// <summary>Başlangıcı geçmişte olan (1 dakika önce) etkinlik oluşturulamaz: event.start_in_past.</summary>
    [Fact]
    public void Create_rejects_start_in_the_past() =>
        AssertDomainError("event.start_in_past", () => NewEvent(startsAt: Now.AddMinutes(-1)));

    /// <summary>Bitişi başlangıcından önce olan etkinlik oluşturulamaz: event.invalid_end.</summary>
    [Fact]
    public void Create_rejects_end_before_start() =>
        AssertDomainError("event.invalid_end", () => NewEvent(startsAt: Now.AddDays(1), endsAt: Now.AddHours(1)));

    /// <summary>7 günden uzun süren (burada 8 gün) etkinlik oluşturulamaz: event.too_long.</summary>
    [Fact]
    public void Create_rejects_events_longer_than_seven_days() =>
        AssertDomainError("event.too_long", () => NewEvent(startsAt: Now.AddDays(1), endsAt: Now.AddDays(9)));

    /// <summary>
    /// Kontenjan 1 ile 10.000 arasında olmalıdır; 0, eksi değer ve 10.001 reddedilir: event.invalid_capacity.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(10_001)]
    public void Create_rejects_invalid_capacity(int capacity) =>
        AssertDomainError("event.invalid_capacity", () => NewEvent(capacity: capacity));

    // ---------------------------------------------------------------- Katılım

    /// <summary>Herkese açık etkinliğe katılan kişi hemen onaylanır ve katılımcı sayısı 1 artar.</summary>
    [Fact]
    public void Admit_to_open_event_approves_immediately_and_counts()
    {
        var ev = NewEvent();

        var status = ev.Admit(Guid.NewGuid(), Now);

        Assert.Equal(ParticipantStatus.Approved, status);
        Assert.Equal(1, ev.ParticipantCount);
    }

    /// <summary>
    /// Onay gerektiren etkinlikte istek "bekliyor" durumunda kalır ve katılımcı sayısına eklenmez.
    /// </summary>
    [Fact]
    public void Admit_to_approval_event_is_pending_and_does_not_count()
    {
        var ev = NewEvent(policy: JoinPolicy.ApprovalRequired);

        var status = ev.Admit(Guid.NewGuid(), Now);

        Assert.Equal(ParticipantStatus.Pending, status);
        Assert.Equal(0, ev.ParticipantCount);
    }

    /// <summary>
    /// Kontenjanı dolu etkinliğe gelen kişi, katılım şekli ne olursa olsun bekleme listesine alınır ve
    /// katılımcı sayısı değişmez. Tek kişilik etkinlik önce doldurulur, sonra ikinci kişi denenir.
    /// </summary>
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

    /// <summary>
    /// Onay gerektiren etkinlikte yer olsa bile sırada bekleyen varsa yeni gelen de bekleme listesine girer
    /// (öne geçemez); bekleyen yoksa isteği Pending olur. Herkese açık etkinlikte bu bilgi sonucu
    /// değiştirmez: yer varsa kişi hemen katılır.
    /// </summary>
    [Fact]
    public void Admit_does_not_let_a_newcomer_cut_ahead_of_the_waitlist()
    {
        var approval = NewEvent(capacity: 2, policy: JoinPolicy.ApprovalRequired);
        approval.AddApprovedParticipant(); // 1/2: yer var

        Assert.Equal(ParticipantStatus.Waitlisted, approval.Admit(Guid.NewGuid(), Now, someoneIsWaiting: true));
        Assert.Equal(ParticipantStatus.Pending, approval.Admit(Guid.NewGuid(), Now, someoneIsWaiting: false));
        Assert.Equal(1, approval.ParticipantCount);

        var open = NewEvent(capacity: 2);
        Assert.Equal(ParticipantStatus.Approved, open.Admit(Guid.NewGuid(), Now, someoneIsWaiting: true));
    }

    /// <summary>Kontenjan doluyken organizatör bir kişiyi daha onaylayamaz: event.full.</summary>
    [Fact]
    public void Approving_into_a_full_event_fails()
    {
        var ev = NewEvent(capacity: 1, policy: JoinPolicy.ApprovalRequired);
        ev.AddApprovedParticipant();
        AssertDomainError("event.full", ev.AddApprovedParticipant);
    }

    // ---------------------------------------------------------------- Düzenleme

    /// <summary>
    /// Etkinliği "Yeni başlık" ile günceller; verilmeyen alanlar eski değerinde kalır. Testler yalnızca
    /// değiştirmek istediği alanı (saat, kontenjan, enlem, mekan adı, şimdiki zaman) parametreyle verir.
    /// </summary>
    private static EventChanges Update(Event ev, DateTime? startsAt = null, int? capacity = null,
        double latitude = 41.0082, string? placeName = null, DateTime? now = null) =>
        ev.Update(1, "Yeni başlık", null, startsAt ?? ev.StartsAt, ev.EndsAt, latitude, 28.9784,
            placeName, null, capacity, now ?? Now);

    /// <summary>
    /// Güncelleme sonucu neyin değiştiğini doğru bildirir: hiçbir şey değişmediyse katılımcılara bildirim
    /// gerekmez; saat değişince TimeChanged, konum değişince PlaceChanged olur. Kontenjanın artması
    /// CapacityIncreased olarak işaretlenir ama tek başına bildirim sebebi değildir.
    /// </summary>
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

    /// <summary>
    /// Kontenjan mevcut katılımcı sayısının (2) altına indirilemez: event.capacity_below_participants.
    /// </summary>
    [Fact]
    public void Capacity_cannot_drop_below_current_participants()
    {
        var ev = NewEvent(capacity: 3);
        ev.Admit(Guid.NewGuid(), Now);
        ev.Admit(Guid.NewGuid(), Now);

        AssertDomainError("event.capacity_below_participants", () => Update(ev, capacity: 1));
    }

    /// <summary>
    /// Başlamış etkinlik (event.already_started) ve iptal edilmiş etkinlik (event.cancelled) düzenlenemez.
    /// </summary>
    [Fact]
    public void Started_or_cancelled_events_cannot_be_edited()
    {
        var started = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("event.already_started", () => Update(started, now: Now.AddHours(2)));

        var cancelled = NewEvent();
        cancelled.Cancel(Now);
        AssertDomainError("event.cancelled", () => Update(cancelled));
    }

    /// <summary>Organizatör kendi etkinliğine katılımcı olarak giremez: event.organizer_cannot_join.</summary>
    [Fact]
    public void Organizer_cannot_join_own_event() =>
        AssertDomainError("event.organizer_cannot_join", () => NewEvent().Admit(Organizer, Now));

    /// <summary>Etkinlik başladıktan sonra kimse katılamaz: event.already_started.</summary>
    [Fact]
    public void Nobody_can_join_after_start()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("event.already_started", () => ev.Admit(Guid.NewGuid(), Now.AddHours(2)));
    }

    /// <summary>Katılımcısı olmayan etkinlikten katılımcı düşülürse sayı eksiye inmez, 0 kalır.</summary>
    [Fact]
    public void Removing_participants_never_goes_below_zero()
    {
        var ev = NewEvent();
        ev.RemoveApprovedParticipant();
        Assert.Equal(0, ev.ParticipantCount);
    }

    // ---------------------------------------------------------------- İptal

    /// <summary>
    /// İptal edilen etkinliğin durumu Cancelled olur ve yeni katılım kabul etmez: event.cancelled.
    /// </summary>
    [Fact]
    public void Cancelled_event_rejects_new_participants()
    {
        var ev = NewEvent();
        ev.Cancel(Now);

        Assert.Equal(EventStatus.Cancelled, ev.Status);
        AssertDomainError("event.cancelled", () => ev.Admit(Guid.NewGuid(), Now));
    }

    /// <summary>Başlamış etkinlik iptal edilemez: event.already_started.</summary>
    [Fact]
    public void Started_event_cannot_be_cancelled()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("event.already_started", () => ev.Cancel(Now.AddHours(2)));
    }

    /// <summary>Zaten iptal edilmiş etkinlik ikinci kez iptal edilemez: event.cancelled.</summary>
    [Fact]
    public void Event_cannot_be_cancelled_twice()
    {
        var ev = NewEvent();
        ev.Cancel(Now);
        AssertDomainError("event.cancelled", () => ev.Cancel(Now));
    }

    // ---------------------------------------------------------------- Oylama

    /// <summary>Organizatör kendi etkinliğine puan veremez: rating.own_event.</summary>
    [Fact]
    public void Organizer_cannot_rate_own_event()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        AssertDomainError("rating.own_event",
            () => ev.EnsureCanBeRatedBy(Organizer, null, Now.AddHours(2)));
    }

    /// <summary>
    /// Yalnızca onaylı katılımcı puan verebilir. Hiç katılmamış, isteği bekleyen, reddedilmiş ya da ayrılmış
    /// kişi rating.not_participant hatası alır.
    /// </summary>
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

    /// <summary>
    /// Oylama etkinlik başlayınca açılır: başlamadan 30 dakika önce rating.not_open_yet hatası verir, tam
    /// başlangıç anında izin verir.
    /// </summary>
    [Fact]
    public void Rating_opens_when_event_starts()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        var participant = Guid.NewGuid();

        AssertDomainError("rating.not_open_yet",
            () => ev.EnsureCanBeRatedBy(participant, ParticipantStatus.Approved, Now.AddMinutes(30)));

        ev.EnsureCanBeRatedBy(participant, ParticipantStatus.Approved, Now.AddHours(1)); // tam başlangıçta açık
    }

    /// <summary>
    /// Bitiş saati verilmemişse süre varsayılan kabul edilir (3 saat) ve oylama bitişten 14 gün sonra
    /// kapanır: tam kapanış anında hâlâ izin vardır, 1 dakika sonrasında rating.closed hatası gelir.
    /// </summary>
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

    /// <summary>İptal edilmiş etkinliğe onaylı katılımcı bile puan veremez: event.cancelled.</summary>
    [Fact]
    public void Cancelled_event_cannot_be_rated()
    {
        var ev = NewEvent(startsAt: Now.AddHours(1));
        ev.Cancel(Now);
        AssertDomainError("event.cancelled",
            () => ev.EnsureCanBeRatedBy(Guid.NewGuid(), ParticipantStatus.Approved, Now.AddHours(2)));
    }

    /// <summary>
    /// İlk puan (4) toplamı 4, oy sayısını 1 artırır; ortalama 4,0 olur ve dönen fark (4, 1) olur.
    /// </summary>
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

    /// <summary>
    /// Verilmiş puanı değiştirmek (5 yerine 1) oy sayısını değiştirmez, yalnızca toplamı farkı kadar
    /// oynatır: fark (-4, 0), toplam 4, oy sayısı 2, ortalama 2,0.
    /// </summary>
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
