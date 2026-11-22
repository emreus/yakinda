namespace Yakinda.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }
    /// <summary>Veritabanı transaction'ını başlatır.</summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    /// <summary>Transaction'daki bütün değişiklikleri kalıcı yapar.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>Transaction'daki bütün değişiklikleri geri alır.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
