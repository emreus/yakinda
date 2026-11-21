using Npgsql;
using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Infrastructure.Persistence;

/// <summary>
/// HTTP isteği başına (scoped) tek bağlantı + opsiyonel transaction.
/// Command tarafındaki tüm repository'ler bu oturumu paylaşır; böylece
/// TransactionBehavior'ın açtığı transaction hepsini kapsar.
/// </summary>
internal sealed class DbSession(NpgsqlDataSource dataSource) : IUnitOfWork, IAsyncDisposable
{
    private NpgsqlConnection? _connection;

    public NpgsqlTransaction? Transaction { get; private set; }

    public bool HasActiveTransaction => Transaction is not null;

    public async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        _connection ??= await dataSource.OpenConnectionAsync(cancellationToken);
        return _connection;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is not null)
            throw new InvalidOperationException("Zaten aktif bir transaction var.");

        var connection = await GetConnectionAsync(cancellationToken);
        Transaction = await connection.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is null)
            return;

        await Transaction.CommitAsync(cancellationToken);
        await Transaction.DisposeAsync();
        Transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is null)
            return;

        try
        {
            await Transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await Transaction.DisposeAsync();
            Transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Transaction is not null)
            await Transaction.DisposeAsync();

        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
