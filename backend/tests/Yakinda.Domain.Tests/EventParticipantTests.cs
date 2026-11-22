using Yakinda.Domain.Common;
using Yakinda.Domain.Events;

namespace Yakinda.Domain.Tests;

public sealed class EventParticipantTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Rastgele bir etkinlik ve kullanıcı için, verilen durumda bir katılım kaydı üretir.</summary>
    private static EventParticipant With(ParticipantStatus status) =>
        EventParticipant.Create(Guid.NewGuid(), Guid.NewGuid(), status, Now);

    /// <summary>Bekleyen istek onaylanınca durum Approved olur.</summary>
    [Fact]
    public void Pending_request_can_be_approved()
    {
        var participant = With(ParticipantStatus.Pending);
        participant.Approve(Now);
        Assert.Equal(ParticipantStatus.Approved, participant.Status);
    }

    /// <summary>
    /// Yalnızca bekleyen istek onaylanabilir; onaylı, reddedilmiş ya da ayrılmış kayıtta
    /// participant.not_pending hatası gelir.
    /// </summary>
    [Theory]
    [InlineData(ParticipantStatus.Approved)]
    [InlineData(ParticipantStatus.Rejected)]
    [InlineData(ParticipantStatus.Left)]
    public void Only_pending_requests_can_be_approved(ParticipantStatus status)
    {
        var ex = Assert.Throws<DomainException>(() => With(status).Approve(Now));
        Assert.Equal("participant.not_pending", ex.Code);
    }

    /// <summary>
    /// Reddetme, kişinin daha önce onaylı olup olmadığını döner (yer boşaldı mı): bekleyen istekte false,
    /// onaylı katılımcıda true. Her iki durumda da kayıt Rejected olur.
    /// </summary>
    [Theory]
    [InlineData(ParticipantStatus.Pending, false)]
    [InlineData(ParticipantStatus.Approved, true)]
    public void Reject_reports_whether_a_seat_was_freed(ParticipantStatus status, bool expectedWasApproved)
    {
        var participant = With(status);

        var wasApproved = participant.Reject(Now);

        Assert.Equal(expectedWasApproved, wasApproved);
        Assert.Equal(ParticipantStatus.Rejected, participant.Status);
    }

    /// <summary>Onaylı katılımcı ayrılınca durum Left olur ve bir yer boşaldığı bildirilir (true).</summary>
    [Fact]
    public void Approved_participant_leaving_frees_a_seat()
    {
        var participant = With(ParticipantStatus.Approved);
        Assert.True(participant.Leave(Now));
        Assert.Equal(ParticipantStatus.Left, participant.Status);
    }

    /// <summary>Reddedilmiş ya da zaten ayrılmış kişi yeniden ayrılamaz: participant.not_active.</summary>
    [Theory]
    [InlineData(ParticipantStatus.Rejected)]
    [InlineData(ParticipantStatus.Left)]
    public void Inactive_participant_cannot_leave_again(ParticipantStatus status)
    {
        var ex = Assert.Throws<DomainException>(() => With(status).Leave(Now));
        Assert.Equal("participant.not_active", ex.Code);
    }

    /// <summary>
    /// Kaydı duran kişi yeniden katılamaz; her durumun kendi hata kodu vardır: bekleyen istek
    /// (already_pending), onaylı katılım (already_joined), reddedilmiş istek (rejected).
    /// </summary>
    [Theory]
    [InlineData(ParticipantStatus.Pending, "participant.already_pending")]
    [InlineData(ParticipantStatus.Approved, "participant.already_joined")]
    [InlineData(ParticipantStatus.Rejected, "participant.rejected")]
    public void Rejoin_is_blocked_unless_participant_left(ParticipantStatus status, string code)
    {
        var ex = Assert.Throws<DomainException>(() => With(status).Rejoin(ParticipantStatus.Approved, Now));
        Assert.Equal(code, ex.Code);
    }

    /// <summary>
    /// Bekleme listesindeki kişi sıraya alınabilir: etkinliğin katılım şekline göre doğrudan onaylı ya da
    /// onay bekleyen duruma geçer.
    /// </summary>
    [Theory]
    [InlineData(ParticipantStatus.Approved)]
    [InlineData(ParticipantStatus.Pending)]
    public void Waitlisted_participant_can_be_promoted(ParticipantStatus newStatus)
    {
        var participant = With(ParticipantStatus.Waitlisted);
        participant.PromoteFromWaitlist(newStatus, Now);
        Assert.Equal(newStatus, participant.Status);
    }

    /// <summary>Bekleme listesinde olmayan kayıt sıraya alınamaz: participant.not_waitlisted.</summary>
    [Fact]
    public void Only_waitlisted_participants_can_be_promoted()
    {
        var ex = Assert.Throws<DomainException>(() => With(ParticipantStatus.Pending).PromoteFromWaitlist(ParticipantStatus.Approved, Now));
        Assert.Equal("participant.not_waitlisted", ex.Code);
    }

    /// <summary>Bekleme listesinden ayrılan kişi yer boşaltmaz (false döner); kaydı Left olur.</summary>
    [Fact]
    public void Leaving_the_waitlist_frees_no_seat()
    {
        var participant = With(ParticipantStatus.Waitlisted);
        Assert.False(participant.Leave(Now));
        Assert.Equal(ParticipantStatus.Left, participant.Status);
    }

    /// <summary>
    /// Bekleme listesindeki kişi ikinci kez katılma isteği gönderemez: participant.already_waitlisted.
    /// </summary>
    [Fact]
    public void Waitlisted_participant_cannot_join_again()
    {
        var ex = Assert.Throws<DomainException>(() => With(ParticipantStatus.Waitlisted).Rejoin(ParticipantStatus.Approved, Now));
        Assert.Equal("participant.already_waitlisted", ex.Code);
    }

    /// <summary>Ayrılmış kişi yeniden katılabilir; kaydı verilen yeni duruma (burada Pending) geçer.</summary>
    [Fact]
    public void Participant_who_left_can_rejoin()
    {
        var participant = With(ParticipantStatus.Left);
        participant.Rejoin(ParticipantStatus.Pending, Now);
        Assert.Equal(ParticipantStatus.Pending, participant.Status);
    }
}
