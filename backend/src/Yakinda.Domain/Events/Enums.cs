namespace Yakinda.Domain.Events;

// Enum'lar veritabanındaki smallint kolonlarla birebir eşleşsin diye "short" tabanlıdır.

public enum EventStatus : short
{
    Active = 0,
    Cancelled = 1
}

public enum JoinPolicy : short
{
    /// <summary>Katıl butonuna basan anında katılır.</summary>
    Open = 0,

    /// <summary>Katılım isteği organizatör onayına düşer.</summary>
    ApprovalRequired = 1
}

public enum ParticipantStatus : short
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Left = 3
}
