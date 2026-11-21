using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Application.Categories;

/// <summary>
/// Kategoriler neredeyse hiç değişmez: 1 saat cache'lenir.
/// Yeni bir kategori migration'ı eklerseniz anahtardaki sürümü artırın (v1 -> v2).
/// </summary>
public sealed record GetCategoriesQuery : ICachedQuery<IReadOnlyList<CategoryDto>>
{
    public string CacheKey => "categories:v1";
    public TimeSpan CacheDuration => TimeSpan.FromHours(1);
    public IReadOnlyCollection<string> CacheTags => new[] { CacheTag.Categories };
}

public sealed class CategoryDto
{
    public short Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
}

internal sealed class GetCategoriesQueryHandler(IDbConnectionFactory db)
    : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<CategoryDto>(new CommandDefinition(
            "SELECT id, slug, name, emoji FROM categories ORDER BY sort_order, id",
            cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
