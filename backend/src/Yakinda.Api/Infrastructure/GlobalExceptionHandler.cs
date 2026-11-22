using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Yakinda.Application.Exceptions;
using Yakinda.Domain.Common;

namespace Yakinda.Api.Infrastructure;

/// <summary>
/// Tüm hataları RFC 7807 ProblemDetails formatında döner. Mobil uygulama:
///   - "detail" alanını kullanıcıya gösterir
///   - "code" alanıyla hata tipini ayırt eder (örn. "event.full")
///   - doğrulama hatalarında "errors" sözlüğünü form alanlarına eşler
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Yakalanmayan her hatayı tek biçimde (ProblemDetails) yanıta çevirir. Hata tipine göre durum kodu ve
    /// makine kodu seçilir: doğrulama 400, iş kuralı 422, bulunamadı 404, yetkisiz 403, çakışma 409, giriş
    /// gerekli 401, veritabanı benzersizlik ihlali 409. İstemci bağlantıyı kapattıysa yanıt yazılmaz (499).
    /// Tanınmayan hata loglanır ve kullanıcıya ayrıntı verilmeden 500 döner.
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // İstemci bağlantıyı kapattı, yanıt yazmaya gerek yok.
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var problem = exception switch
        {
            ValidationException validation => Problem(
                StatusCodes.Status400BadRequest, "Doğrulama hatası",
                validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Gönderilen bilgiler geçersiz.",
                "validation_failed",
                ToErrorDictionary(validation)),

            DomainException domain => Problem(
                StatusCodes.Status422UnprocessableEntity, "İşlem yapılamadı", domain.Message, domain.Code),

            NotFoundException notFound => Problem(
                StatusCodes.Status404NotFound, "Bulunamadı", notFound.Message, notFound.Code),

            ForbiddenException forbidden => Problem(
                StatusCodes.Status403Forbidden, "Yetkisiz işlem", forbidden.Message, forbidden.Code),

            ConflictException conflict => Problem(
                StatusCodes.Status409Conflict, "Çakışma", conflict.Message, conflict.Code),

            UnauthorizedException unauthorized => Problem(
                StatusCodes.Status401Unauthorized, "Giriş gerekli", unauthorized.Message, unauthorized.Code),

            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => Problem(
                StatusCodes.Status409Conflict, "Çakışma", "Bu kayıt zaten mevcut.", "db.unique_violation"),

            _ => null
        };

        if (problem is null)
        {
            // İstek yolu kullanıcıdan gelir: satır sonu içerirse loga sahte satır yazdırılamasın.
            var path = (httpContext.Request.Path.Value ?? string.Empty).Replace("\r", "").Replace("\n", "");
            logger.LogError(exception, "Beklenmeyen hata: {Path}", path);
            problem = Problem(
                StatusCodes.Status500InternalServerError, "Sunucu hatası",
                "Beklenmeyen bir hata oluştu, lütfen tekrar dene.", "server_error");
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    /// <summary>
    /// ProblemDetails nesnesini kurar: durum kodu, başlık, kullanıcıya gösterilecek açıklama ve mobilin
    /// hatayı tanıması için "code" alanı.
    /// </summary>
    private static ProblemDetails Problem(
        int status, string title, string detail, string code,
        Dictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        if (errors is not null)
            problem.Extensions["errors"] = errors;
        return problem;
    }

    /// <summary>
    /// Doğrulama hatalarını alan adına göre gruplar ve alan adlarını camelCase yapar (DisplayName,
    /// displayName olur); mobil bunları doğrudan form alanlarının altına yazar.
    /// </summary>
    private static Dictionary<string, string[]> ToErrorDictionary(ValidationException exception) =>
        exception.Errors
            .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName ?? string.Empty))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
}
