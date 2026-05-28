# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["src/Nexus.Identity.Api/Nexus.Identity.Api.csproj", "src/Nexus.Identity.Api/"]
RUN dotnet restore "src/Nexus.Identity.Api/Nexus.Identity.Api.csproj"
COPY . .
RUN dotnet publish "src/Nexus.Identity.Api/Nexus.Identity.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs tzdata \
    && addgroup -S nexus && adduser -S -G nexus nexus
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:8000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8000
ENTRYPOINT ["dotnet", "Nexus.Identity.Api.dll"]
