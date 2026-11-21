using System.Data.Common;
using Npgsql;
using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Infrastructure.Persistence;

/// <summary>Okuma tarafı için havuzdan (pool) bağlantı verir.</summary>
internal sealed class NpgsqlConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        await dataSource.OpenConnectionAsync(cancellationToken);
}
