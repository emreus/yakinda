using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Yakinda.Application.Behaviors;

/// <summary>Her isteğin süresini loglar, yavaş olanları uyarı olarak işaretler.</summary>
internal sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const long SlowRequestThresholdMs = 500;

    /// <summary>
    /// Kronometreyi başlatır, sonraki adımı (next) çalıştırır ve süreyi loglar: 500 ms'den uzun süren
    /// istek uyarı (Warning) olarak, diğerleri Debug seviyesinde yazılır. Yanıta dokunmaz.
    /// </summary>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        var response = await next();

        stopwatch.Stop();
        if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            logger.LogWarning("Yavaş istek: {RequestName} {ElapsedMs} ms sürdü", requestName, stopwatch.ElapsedMilliseconds);
        else
            logger.LogDebug("{RequestName} tamamlandı ({ElapsedMs} ms)", requestName, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
