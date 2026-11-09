# syntax=docker/dockerfile:1

# ── Frontend ────────────────────────────────────────────────────────────────
# Build the Svelte bundle into WarTable.Api/wwwroot, which the API project
# embeds into the assembly (see WarTable.Api.csproj).
FROM node:22-alpine AS client
WORKDIR /src

COPY WarTable.Client/package.json WarTable.Client/package-lock.json ./WarTable.Client/
RUN npm ci --prefix WarTable.Client --no-audit --no-fund

COPY WarTable.Client/ ./WarTable.Client/
# Vite resolves its outDir relative to the client, so the sibling API wwwroot
# directory has to exist before the build runs.
RUN mkdir -p /src/WarTable.Api \
    && npm --prefix WarTable.Client run build

# ── Backend ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY WarTable.Api/WarTable.Api.csproj WarTable.Api/
RUN dotnet restore WarTable.Api/WarTable.Api.csproj

COPY WarTable.Api/ WarTable.Api/
COPY --from=client /src/WarTable.Api/wwwroot/ WarTable.Api/wwwroot/

RUN dotnet publish WarTable.Api/WarTable.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ── Runtime ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# The base image sets ASPNETCORE_HTTP_PORTS=8080. WarTable binds explicitly,
# but pin this to 5110 so the runtime can never open a second, unintended
# listener.
ENV ASPNETCORE_HTTP_PORTS=5110 \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 wartable \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin wartable \
    && mkdir -p /data \
    && chown -R 10001:10001 /data

WORKDIR /app
COPY --from=build /app/publish ./

EXPOSE 5110

USER 10001:10001

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:5110/api/health || exit 1

ENTRYPOINT ["dotnet", "WarTable.Api.dll"]
CMD ["--mode", "serve", "--host", "0.0.0.0", "--port", "5110", "--db", "/data/wartable.db"]
