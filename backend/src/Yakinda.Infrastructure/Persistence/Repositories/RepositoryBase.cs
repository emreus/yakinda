using Dapper;

namespace Yakinda.Infrastructure.Persistence.Repositories;

/// <summary>Dapper çağrılarını aktif DbSession bağlantısı + transaction'ı ile yapar.</summary>
internal abstract class RepositoryBase(DbSession session)
{
    protected async Task<int> ExecuteAsync(string sql, object? param, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, param, cancellationToken));
    }

    protected async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<T>(Command(sql, param, cancellationToken));
    }

    protected async Task<T?> ExecuteScalarAsync<T>(string sql, object? param, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<T>(Command(sql, param, cancellationToken));
    }

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<T>(Command(sql, param, cancellationToken))).AsList();
    }

    private CommandDefinition Command(string sql, object? param, CancellationToken cancellationToken) =>
        new(sql, param, session.Transaction, cancellationToken: cancellationToken);
}
