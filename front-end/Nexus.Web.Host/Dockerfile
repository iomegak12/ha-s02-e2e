# syntax=docker/dockerfile:1

# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Nexus.Web.sln ./
COPY Nexus.Web.Client/Nexus.Web.Client.csproj Nexus.Web.Client/
COPY Nexus.Web.Host/Nexus.Web.Host.csproj     Nexus.Web.Host/

RUN dotnet restore Nexus.Web.Host/Nexus.Web.Host.csproj

COPY Nexus.Web.Client/ Nexus.Web.Client/
COPY Nexus.Web.Host/   Nexus.Web.Host/

RUN dotnet publish Nexus.Web.Host/Nexus.Web.Host.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Nexus.Web.Host.dll"]
