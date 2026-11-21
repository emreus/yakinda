namespace Yakinda.Application.Exceptions;

// Uygulama katmanı hataları. API katmanı bunları HTTP durum kodlarına çevirir
// (GlobalExceptionHandler). Code alanı mobil tarafta hata tipini ayırt etmek içindir.

public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>404</summary>
public sealed class NotFoundException(string code, string message) : AppException(code, message)
{
}

/// <summary>403</summary>
public sealed class ForbiddenException(string code, string message) : AppException(code, message)
{
}

/// <summary>409</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message)
{
}

/// <summary>401</summary>
public sealed class UnauthorizedException(string code, string message) : AppException(code, message)
{
}
