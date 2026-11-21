using System.Text.Json.Serialization;
using Yakinda.Api.Endpoints;
using Yakinda.Api.Infrastructure;
using Yakinda.Application;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Infrastructure;
using Yakinda.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Katmanlar
builder.Services.AddApplication(builder.Configuration["MediatR:LicenseKey"]);
builder.Services.AddInfrastructure(builder.Configuration);

// API'ye özel servisler
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Enum'lar JSON'da "Approved", "ApprovalRequired" gibi string olarak gider/gelir.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await app.Services.GetRequiredService<MigrationRunner>().RunAsync();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // http://localhost:5080/openapi/v1.json
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapCategoryEndpoints();
api.MapEventEndpoints();
api.MapUserEndpoints();

app.Run();
