# syntax=docker/dockerfile:1
# ETEK WCS backend (ASP.NET Core .NET 10). Build context = repository root (see deployment/docker-compose.yml).
# Stages: build -> publish (runtime image) and bundle (one-shot EF migration runner). No secrets are baked into any image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# restore layer (cached until a project file changes)
COPY src/backend/WCS.sln src/backend/
COPY src/backend/WCS.Domain/WCS.Domain.csproj src/backend/WCS.Domain/
COPY src/backend/WCS.Application/WCS.Application.csproj src/backend/WCS.Application/
COPY src/backend/WCS.Infrastructure/WCS.Infrastructure.csproj src/backend/WCS.Infrastructure/
COPY src/backend/WCS.Runtime/WCS.Runtime.csproj src/backend/WCS.Runtime/
COPY src/backend/WCS.Api/WCS.Api.csproj src/backend/WCS.Api/
RUN dotnet restore src/backend/WCS.Api/WCS.Api.csproj
COPY src/backend/ src/backend/
RUN dotnet publish src/backend/WCS.Api/WCS.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- migration bundle: a self-contained executable that applies the existing EF Core migrations (forward only, never drops data)
FROM build AS bundle
RUN dotnet tool install --global dotnet-ef --version 10.0.*
ENV PATH="$PATH:/root/.dotnet/tools"
# The bundle is built from the host project, which needs *some* connection settings to be constructed; these are throw-away build-time values (not secrets, not kept in the image).
RUN ConnectionStrings__WcsDatabase="Host=build;Database=build;Username=build" Database__Password="build-only" \
    dotnet ef migrations bundle --project src/backend/WCS.Infrastructure --startup-project src/backend/WCS.Api \
    --self-contained -r linux-x64 -o /app/efbundle --force

# ---- migrator image (runs once, then exits)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrator
WORKDIR /app
COPY --from=bundle /app/efbundle ./efbundle
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
# connection settings come from the environment (ConnectionStrings__WcsDatabase, Database__Password) at run time
ENTRYPOINT ["./efbundle"]

# ---- runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
# key ring for encrypting PLC/RCS secrets (Data Protection) lives on a volume, owned by the non-root app user
RUN mkdir -p /keys && chown app:app /keys
VOLUME ["/keys"]
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DataProtection__KeysPath=/keys \
    DOTNET_gcServer=0
USER app
EXPOSE 8080
# liveness + database readiness (anonymous /healthz). bash is available in the image, so no extra packages are installed.
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /healthz HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -n1 <&3 | grep -q " 200 "' || exit 1
ENTRYPOINT ["dotnet", "WCS.Api.dll"]
