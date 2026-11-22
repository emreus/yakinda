using Dapper;

namespace Yakinda.Infrastructure.Persistence.Repositories;

/// <summary>Dapper çağrılarını aktif DbSession bağlantısı + transaction'ı ile yapar.</summary>
internal abstract class RepositoryBase(DbSession session)
{
    /// <summary>
    /// Sonuç döndürmeyen SQL'i (INSERT, UPDATE, DELETE) oturumun bağlantısı ve transaction'ıyla çalıştırır;
    /// etkilenen satır sayısını döner.
    /// </summary>
    protected async Task<int> ExecuteAsync(string sql, object? param, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, param, cancellationToken));
    }

    /// <summary>
    /// Tek satır bekleyen sorguyu çalıştırır ve satırı verilen tipe eşler; satır yoksa null döner.
    /// </summary>
    protected async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<T>(Command(sql, param, cancellationToken));
    }

    /// <summary>Tek bir değer (sayı, bool gibi) döndüren sorguyu çalıştırır.</summary>
    protected async Task<T?> ExecuteScalarAsync<T>(string sql, object? param, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<T>(Command(sql, param, cancellationToken));
    }

    /// <summary>Birden çok satır döndüren sorguyu çalıştırır ve satırları liste olarak döner.</summary>
    protected async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<T>(Command(sql, param, cancellationToken))).AsList();
    }

    /// <summary>
    /// SQL'i, parametreleri, oturumun aktif transaction'ını ve iptal token'ını tek bir Dapper komutunda
    /// toplar; böylece her çağrı aynı transaction'a dahil olur.
    /// </summary>
    private CommandDefinition Command(string sql, object? param, CancellationToken cancellationToken) =>
        new(sql, param, session.Transaction, cancellationToken: cancellationToken);
}
