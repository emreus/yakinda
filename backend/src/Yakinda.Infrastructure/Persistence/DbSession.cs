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

    /// <summary>
    /// Oturumun bağlantısını döner; henüz açılmadıysa havuzdan bir bağlantı açar. Veritabanına hiç
    /// dokunmayan bir istek bağlantı da açmaz.
    /// </summary>
    public async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        _connection ??= await dataSource.OpenConnectionAsync(cancellationToken);
        return _connection;
    }

    /// <summary>Bağlantıyı açıp transaction başlatır. Zaten açık bir transaction varsa hata fırlatır.</summary>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is not null)
            throw new InvalidOperationException("Zaten aktif bir transaction var.");

        var connection = await GetConnectionAsync(cancellationToken);
        Transaction = await connection.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Transaction'ı commit eder ve nesnesini bırakır. Açık transaction yoksa hiçbir şey yapmaz.
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is null)
            return;

        await Transaction.CommitAsync(cancellationToken);
        await Transaction.DisposeAsync();
        Transaction = null;
    }

    /// <summary>
    /// Transaction'ı geri alır. Geri alma sırasında hata olsa bile transaction nesnesi bırakılır;
    /// açık transaction yoksa hiçbir şey yapmaz.
    /// </summary>
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

    /// <summary>
    /// İstek bitince çağrılır: kalmış transaction'ı ve bağlantıyı kapatır, bağlantı havuza döner.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Transaction is not null)
            await Transaction.DisposeAsync();

        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
