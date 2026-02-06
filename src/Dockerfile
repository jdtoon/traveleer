FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/wwwroot ./wwwroot

RUN mkdir -p /app/db /app/db/keys && chmod -R 700 /app/db/keys
ENV ConnectionStrings__CoreDatabase="Data Source=/app/db/core.db"
ENV ConnectionStrings__AuditDatabase="Data Source=/app/db/audit.db"
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "saas.dll"]
