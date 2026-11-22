using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Yakinda.Application.Behaviors;

/// <summary>
/// İsteğe ait tüm FluentValidation validator'larını çalıştırır; hata varsa handler'a hiç
/// ulaşmadan <see cref="ValidationException"/> fırlatır (API'de 400 olarak döner).
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// İstek tipine kayıtlı bütün validator'ları sırayla çalıştırır ve hataları toplar. En az bir hata
    /// varsa ValidationException fırlatır (handler hiç çalışmaz); hata yoksa sonraki adıma geçer.
    /// </summary>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var context = new ValidationContext<TRequest>(request);

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
