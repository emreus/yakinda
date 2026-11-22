using MediatR;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Application.Behaviors;

/// <summary>
/// Command'ları (ICommand) tek bir transaction içinde çalıştırır. Handler içindeki tüm
/// repository çağrıları aynı bağlantı + transaction'ı kullanır; hata olursa hepsi geri alınır.
/// Query'ler bu davranışı atlar.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// İstek command değilse ya da zaten açık bir transaction varsa doğrudan sonraki adıma geçer.
    /// Aksi halde transaction açar; handler başarıyla dönerse commit eder, hata fırlarsa rollback yapıp
    /// hatayı yeniden fırlatır.
    /// </summary>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ITransactionalRequest || unitOfWork.HasActiveTransaction)
            return await next();

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await next();
            await unitOfWork.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
