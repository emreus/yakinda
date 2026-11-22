using MediatR;
using Yakinda.Application.Categories;

namespace Yakinda.Api.Endpoints;

public static class CategoryEndpoints
{
    /// <summary>Kategori ucunu bağlar.</summary>
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder api)
    {
        // GET /api/categories: kategori listesini döner; giriş gerektirmez.
        api.MapGet("/categories", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetCategoriesQuery(), ct)))
            .WithTags("Categories")
            .AllowAnonymous();

        return api;
    }
}
