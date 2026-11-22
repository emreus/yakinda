namespace Yakinda.Application.Common;

/// <summary>
/// Sayfalı sonuç. Toplam kayıt sayısı yerine "HasMore" döner; böylece her istekte
/// pahalı bir COUNT(*) çalıştırmak gerekmez (mobilde sonsuz kaydırma için yeterli).
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);

public static class Paging
{
    /// <summary>
    /// İstenebilecek en büyük sayfa numarası. Sınırsız olduğunda (sayfa − 1) × boyut çarpımı taşıp
    /// sunucu hatasına yol açıyor, ayrıca her farklı sayfa için ayrı önbellek kaydı oluşturulabiliyordu.
    /// </summary>
    public const int MaxPage = 1000;
}
