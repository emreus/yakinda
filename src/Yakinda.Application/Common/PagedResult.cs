namespace Yakinda.Application.Common;

/// <summary>
/// Sayfalı sonuç. Toplam kayıt sayısı yerine "HasMore" döner; böylece her istekte
/// pahalı bir COUNT(*) çalıştırmak gerekmez (mobilde sonsuz kaydırma için yeterli).
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);
