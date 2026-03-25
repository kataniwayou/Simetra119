# =============================================================================
# SnmpCollector Container Image
# Multi-stage build: SDK (build) -> ASP.NET Runtime (final)
# =============================================================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Copy project files first for NuGet restore layer caching
COPY src/SnmpCollector/SnmpCollector.csproj src/SnmpCollector/
RUN dotnet restore src/SnmpCollector/SnmpCollector.csproj

# Copy source code and publish
COPY src/SnmpCollector/. src/SnmpCollector/
RUN dotnet publish src/SnmpCollector/SnmpCollector.csproj \
    -c Release \
    --no-restore \
    -o /app/publish

# Stage 2: Runtime
# CRITICAL: Must use aspnet image (not runtime) because FrameworkReference
# to Microsoft.AspNetCore.App requires the ASP.NET Core shared framework.
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS final
WORKDIR /app

LABEL maintainer="snmpcollector-team"
LABEL description="SnmpCollector SNMP Metric Collection Service"

# Expose ports:
#   10162/udp = SNMP trap listener (remapped from privileged 162)
#   8080/tcp  = Health probe endpoints (Kestrel default in .NET 8+ containers)
EXPOSE 10162/udp
EXPOSE 8080/tcp

# Override SNMP listener port for non-root operation
ENV SnmpListener__Port=10162

# Enable console logging for container log capture
ENV Logging__EnableConsole=true

# Copy published application from build stage
COPY --from=build /app/publish .

# Run as non-root user (built into .NET 8+ official images)
USER $APP_UID

ENTRYPOINT ["dotnet", "SnmpCollector.dll"]
