# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["src/Nexus.Patient.Api/Nexus.Patient.Api.csproj", "src/Nexus.Patient.Api/"]
RUN dotnet restore "src/Nexus.Patient.Api/Nexus.Patient.Api.csproj"
COPY . .
RUN dotnet publish "src/Nexus.Patient.Api/Nexus.Patient.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs tzdata \
    && addgroup -S nexus && adduser -S -G nexus nexus
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:9000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 9000
ENTRYPOINT ["dotnet", "Nexus.Patients.Api.dll"]
