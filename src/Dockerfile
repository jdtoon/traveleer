# =============================================================================
# Stage 1 — Build
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN dotnet tool install -g Microsoft.Web.LibraryManager.Cli
ENV PATH="$PATH:/root/.dotnet/tools"

WORKDIR /src
COPY ["saas.csproj", "./"]
RUN dotnet restore "saas.csproj"

COPY ["libman.json", "./"]
RUN libman restore

COPY . .
RUN dotnet publish "saas.csproj" -c Release -o /app/publish /p:UseAppHost=false

# =============================================================================
# Stage 2 — Runtime
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for Docker HEALTHCHECK
RUN apt-get update && apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=build /src/wwwroot ./wwwroot

# Create data directories with restricted permissions for keys
RUN mkdir -p /app/db/tenants /app/db/keys && chmod -R 700 /app/db/keys

ENV ConnectionStrings__CoreDatabase="Data Source=/app/db/core.db"
ENV ConnectionStrings__AuditDatabase="Data Source=/app/db/audit.db"
ENV Tenancy__DatabasePath=/app/db/tenants
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --retries=3 --start-period=10s \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "saas.dll"]
