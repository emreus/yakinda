using System.Data.Common;
using Npgsql;
using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Infrastructure.Persistence;

/// <summary>Okuma tarafı için havuzdan (pool) bağlantı verir.</summary>
internal sealed class NpgsqlConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    /// <summary>
    /// Havuzdan yeni, açık bir bağlantı verir. Her query kendi bağlantısını alır ve işi bitince kapatır.
    /// </summary>
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        await dataSource.OpenConnectionAsync(cancellationToken);
}
