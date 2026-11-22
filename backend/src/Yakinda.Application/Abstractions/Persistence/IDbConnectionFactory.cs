using System.Data.Common;

namespace Yakinda.Application.Abstractions.Persistence;

/// <summary>Okuma (query) tarafı için açık bir bağlantı verir. Çağıran dispose etmelidir.</summary>
public interface IDbConnectionFactory
{
    /// <summary>Bağlantı havuzundan açık bir bağlantı alır.</summary>
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
