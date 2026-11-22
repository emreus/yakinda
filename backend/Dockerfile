# Yakında API imajı. Derleme bağlamı: backend klasörü.
#   docker build -t yakinda-api backend
# Sunucuda deploy/docker-compose.prod.yml bunu kendisi derler.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce yalnız proje dosyaları: paketler değişmedikçe restore katmanı önbellekten gelir.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Yakinda.Domain/Yakinda.Domain.csproj src/Yakinda.Domain/
COPY src/Yakinda.Application/Yakinda.Application.csproj src/Yakinda.Application/
COPY src/Yakinda.Infrastructure/Yakinda.Infrastructure.csproj src/Yakinda.Infrastructure/
COPY src/Yakinda.Api/Yakinda.Api.csproj src/Yakinda.Api/
RUN dotnet restore src/Yakinda.Api/Yakinda.Api.csproj

COPY src/ src/
RUN dotnet publish src/Yakinda.Api/Yakinda.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Media__RootPath=/data/media

# Görsel klasörü root olmayan "app" kullanıcısına ait (yeni Docker volume bu sahipliği devralır).
RUN mkdir -p /data/media && chown -R "$APP_UID" /data

COPY --from=build /app .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Yakinda.Api.dll"]
