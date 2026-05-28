# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["src/Nexus.Doctor.Api/Nexus.Doctor.Api.csproj", "src/Nexus.Doctor.Api/"]
RUN dotnet restore "src/Nexus.Doctor.Api/Nexus.Doctor.Api.csproj"
COPY . .
RUN dotnet publish "src/Nexus.Doctor.Api/Nexus.Doctor.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs tzdata \
    && addgroup -S nexus && adduser -S -G nexus nexus \
    && mkdir -p /var/data/nexus/doctor-docs \
    && chown -R nexus:nexus /var/data/nexus/doctor-docs
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:10000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 10000
VOLUME ["/var/data/nexus/doctor-docs"]
ENTRYPOINT ["dotnet", "Nexus.Doctors.Api.dll"]
