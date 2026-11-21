using MediatR;
using Yakinda.Application.Categories;

namespace Yakinda.Api.Endpoints;

public static class CategoryEndpoints
{
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/categories", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetCategoriesQuery(), ct)))
            .WithTags("Categories")
            .AllowAnonymous();

        return api;
    }
}
