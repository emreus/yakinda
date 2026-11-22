using Yakinda.Domain.Common;
using Yakinda.Domain.Events;

namespace Yakinda.Domain.Tests;

public sealed class EventParticipantTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static EventParticipant With(ParticipantStatus status) =>
        EventParticipant.Create(Guid.NewGuid(), Guid.NewGuid(), status, Now);

    [Fact]
    public void Pending_request_can_be_approved()
    {
        var participant = With(ParticipantStatus.Pending);
        participant.Approve(Now);
        Assert.Equal(ParticipantStatus.Approved, participant.Status);
    }

    [Theory]
    [InlineData(ParticipantStatus.Approved)]
    [InlineData(ParticipantStatus.Rejected)]
    [InlineData(ParticipantStatus.Left)]
    public void Only_pending_requests_can_be_approved(ParticipantStatus status)
    {
        var ex = Assert.Throws<DomainException>(() => With(status).Approve(Now));
        Assert.Equal("participant.not_pending", ex.Code);
    }

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

    [Fact]
    public void Approved_participant_leaving_frees_a_seat()
    {
        var participant = With(ParticipantStatus.Approved);
        Assert.True(participant.Leave(Now));
        Assert.Equal(ParticipantStatus.Left, participant.Status);
    }

    [Theory]
    [InlineData(ParticipantStatus.Rejected)]
    [InlineData(ParticipantStatus.Left)]
    public void Inactive_participant_cannot_leave_again(ParticipantStatus status)
    {
        var ex = Assert.Throws<DomainException>(() => With(status).Leave(Now));
        Assert.Equal("participant.not_active", ex.Code);
    }

    [Theory]
    [InlineData(ParticipantStatus.Pending, "participant.already_pending")]
    [InlineData(ParticipantStatus.Approved, "participant.already_joined")]
    [InlineData(ParticipantStatus.Rejected, "participant.rejected")]
    public void Rejoin_is_blocked_unless_participant_left(ParticipantStatus status, string code)
    {
        var ex = Assert.Throws<DomainException>(() => With(status).Rejoin(ParticipantStatus.Approved, Now));
        Assert.Equal(code, ex.Code);
    }

    [Theory]
    [InlineData(ParticipantStatus.Approved)]
    [InlineData(ParticipantStatus.Pending)]
    public void Waitlisted_participant_can_be_promoted(ParticipantStatus newStatus)
    {
        var participant = With(ParticipantStatus.Waitlisted);
        participant.PromoteFromWaitlist(newStatus, Now);
        Assert.Equal(newStatus, participant.Status);
    }

    [Fact]
    public void Only_waitlisted_participants_can_be_promoted()
    {
        var ex = Assert.Throws<DomainException>(() => With(ParticipantStatus.Pending).PromoteFromWaitlist(ParticipantStatus.Approved, Now));
        Assert.Equal("participant.not_waitlisted", ex.Code);
    }

    [Fact]
    public void Leaving_the_waitlist_frees_no_seat()
    {
        var participant = With(ParticipantStatus.Waitlisted);
        Assert.False(participant.Leave(Now));
        Assert.Equal(ParticipantStatus.Left, participant.Status);
    }

    [Fact]
    public void Waitlisted_participant_cannot_join_again()
    {
        var ex = Assert.Throws<DomainException>(() => With(ParticipantStatus.Waitlisted).Rejoin(ParticipantStatus.Approved, Now));
        Assert.Equal("participant.already_waitlisted", ex.Code);
    }

    [Fact]
    public void Participant_who_left_can_rejoin()
    {
        var participant = With(ParticipantStatus.Left);
        participant.Rejoin(ParticipantStatus.Pending, Now);
        Assert.Equal(ParticipantStatus.Pending, participant.Status);
    }
}
