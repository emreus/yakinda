namespace Yakinda.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
