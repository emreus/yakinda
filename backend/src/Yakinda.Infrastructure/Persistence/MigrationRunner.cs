using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Yakinda.Infrastructure.Persistence;

/// <summary>
/// Basit, bağımlılıksız migration çalıştırıcı.
/// Persistence/Migrations altındaki *.sql dosyaları (embedded resource) isim sırasına göre,
/// her biri kendi transaction'ında bir kez çalıştırılır ve schema_migrations tablosuna yazılır.
/// Birden fazla API instance'ı aynı anda açılırsa advisory lock sayesinde çakışmazlar.
/// Yeni migration eklemek için: 0003_xxx.sql dosyası oluşturun, bu kadar.
/// </summary>
public sealed class MigrationRunner(NpgsqlDataSource dataSource, ILogger<MigrationRunner> logger)
{
    private const long AdvisoryLockKey = 7_316_001;
    private const string Marker = ".Migrations.";

    /// <summary>
    /// Uygulanmamış migration'ları sırayla çalıştırır. Assembly'ye gömülü .sql dosyalarını ada göre
    /// sıralar, pg_advisory_lock ile kilidi alır (aynı anda açılan ikinci kopya bekler), schema_migrations
    /// tablosunu yoksa oluşturur ve daha önce uygulanmamış her dosyayı kendi transaction'ında çalıştırıp
    /// sürümünü kaydeder. Sonunda, hata olsa bile, kilidi bırakır.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && name.Contains(Marker))
            .Select(name => (Resource: name, Version: name[(name.LastIndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..^4]))
            .OrderBy(x => x.Version, StringComparer.Ordinal)
            .ToList();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync("SELECT pg_advisory_lock(@Key)", new { Key = AdvisoryLockKey });

        try
        {
            await connection.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version     varchar(200) PRIMARY KEY,
                    applied_at  timestamptz  NOT NULL DEFAULT now()
                )
                """);

            var applied = (await connection.QueryAsync<string>("SELECT version FROM schema_migrations"))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var (resource, version) in scripts)
            {
                if (applied.Contains(version))
                    continue;

                var sql = await ReadResourceAsync(assembly, resource);

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken));
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO schema_migrations (version) VALUES (@Version)",
                    new { Version = version },
                    transaction,
                    cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);

                logger.LogInformation("Migration uygulandı: {Version}", version);
            }
        }
        finally
        {
            await connection.ExecuteAsync("SELECT pg_advisory_unlock(@Key)", new { Key = AdvisoryLockKey });
        }
    }

    /// <summary>Gömülü SQL dosyasının içeriğini metin olarak okur; kaynak bulunamazsa hata fırlatır.</summary>
    private static async Task<string> ReadResourceAsync(Assembly assembly, string resourceName)
    {
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Migration kaynağı bulunamadı: {resourceName}");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
