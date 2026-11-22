namespace Yakinda.Domain.Moderation;

// Veritabanında smallint; sayılar değiştirilmemeli.

public enum ReportTargetType : short
{
    Event = 1,
    User = 2,
    Message = 3
}

public enum ReportReason : short
{
    Spam = 1,
    Inappropriate = 2,
    Harassment = 3,
    Dangerous = 4,
    Fake = 5,
    Other = 9
}

public enum ReportStatus : short
{
    Open = 0,
    ActionTaken = 1,
    Dismissed = 2
}

/// <summary>Yöneticinin şikayet üzerine yaptığı işlem.</summary>
public enum ModerationAction
{
    /// <summary>Kural ihlali yok.</summary>
    Dismiss,

    /// <summary>İçeriği kaldır (etkinlik ya da mesaj).</summary>
    RemoveContent,

    /// <summary>İçeriğin sahibini yasakla: giriş yapamaz, yaklaşan etkinlikleri kaldırılır.</summary>
    BanUser
}
