using System.Data.Common;

namespace Yakinda.Application.Abstractions.Persistence;

/// <summary>Okuma (query) tarafı için açık bir bağlantı verir. Çağıran dispose etmelidir.</summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
