using MediatR;

namespace Yakinda.Application.Abstractions.Messaging;

/// <summary>
/// İşaretleyici: bu arayüzü taşıyan istekler <c>TransactionBehavior</c> tarafından
/// tek bir veritabanı transaction'ı içinde çalıştırılır.
/// </summary>
public interface ITransactionalRequest
{
}

/// <summary>Yazma işlemi (state değiştirir). Repository + domain nesneleri üzerinden çalışır.</summary>
public interface ICommand<TResponse> : IRequest<TResponse>, ITransactionalRequest
{
}

/// <summary>Okuma işlemi (state değiştirmez). Dapper ile doğrudan DTO'ya SQL çalıştırır.</summary>
public interface IQuery<TResponse> : IRequest<TResponse>
{
}
