# Build context is apps/api. Used by render.yaml; also runs anywhere Docker does.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Host/ExamPlatform.Api/ExamPlatform.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Render tells the service which port to listen on through PORT; 10000 is its default.
# TLS ends at Render's proxy, so the app listens on plain HTTP.
ENV ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet ExamPlatform.Api.dll"]
