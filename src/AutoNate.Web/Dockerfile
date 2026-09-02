# Auton8 application image.
#
# Three stages: the SPA is built with Node, the backend is published with the
# .NET SDK, and the runtime stage carries neither toolchain. Bases are pinned
# by digest — PinnedImageTests fails the build otherwise (#52), and the tag is
# kept as a trailing comment so a reader can still tell what it is.
#
# Build from the repository root, not from this directory:
#   docker build -t auton8:dev -f src/AutoNate.Web/Dockerfile .

# ── SPA ─────────────────────────────────────────────────────────────────────
# node:24-alpine
FROM node@sha256:e67514e5d0f6c46656005e1b693b2ec9d52e80b641307de684d4a015ba7a4eaf AS spa

WORKDIR /spa

# Manifests first so a source-only change does not reinstall dependencies.
COPY src/AutoNate.Spa/package.json src/AutoNate.Spa/package-lock.json ./
RUN npm ci

COPY src/AutoNate.Spa/ ./
RUN npm run build

# ── Backend ─────────────────────────────────────────────────────────────────
# mcr.microsoft.com/dotnet/sdk:10.0
FROM mcr.microsoft.com/dotnet/sdk@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build

WORKDIR /src

# Project files and lock files first, for the same layer-caching reason.
COPY Directory.Build.props ./
COPY src/AutoNate.Web/AutoNate.Web.csproj src/AutoNate.Web/packages.lock.json src/AutoNate.Web/
COPY src/AutoNate.Plugin.Abstractions/AutoNate.Plugin.Abstractions.csproj \
     src/AutoNate.Plugin.Abstractions/packages.lock.json src/AutoNate.Plugin.Abstractions/

# Locked mode: the committed lock files are the contract, and a drifted
# dependency graph fails here rather than resolving something new inside an
# image nobody will re-inspect.
RUN dotnet restore src/AutoNate.Web/AutoNate.Web.csproj --locked-mode

COPY src/ src/

# BuildSpa=false: the bundle is copied in from the spa stage below rather than
# built again by the MSBuild target, which would need Node in this image and
# would build the same bundle twice.
COPY --from=spa /spa/dist/ src/AutoNate.Web/wwwroot/
RUN dotnet publish src/AutoNate.Web/AutoNate.Web.csproj \
        -c Release \
        -p:BuildSpa=false \
        --no-restore \
        -o /app/publish

# appsettings.Development.json must not ship. `dotnet publish` includes it by
# default, and it carries the local stack's Postgres password and both
# `dev-only-*` shared secrets. They are already public in the repository, so
# this is not a new disclosure — but an image published to a registry that
# happens to be started with ASPNETCORE_ENVIRONMENT=Development would pick them
# up as live configuration, which turns three placeholders into three working
# credentials. The file stays in the repo for `dotnet run`; it has no business
# in a runtime image.
RUN rm -f /app/publish/appsettings.Development.json

# ── Runtime ─────────────────────────────────────────────────────────────────
# mcr.microsoft.com/dotnet/aspnet:10.0
FROM mcr.microsoft.com/dotnet/aspnet@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94 AS runtime

# curl, for the HEALTHCHECK below. The .NET runtime images ship neither curl
# nor wget, so a healthcheck that shells out to either reports the container
# unhealthy forever while the app is serving perfectly well — which is exactly
# what this image did on its first run.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# Non-root. The data root has to be owned by this user before the volume is
# declared, or the app cannot write uploads, plugins or public /files assets.
RUN groupadd --system --gid 64198 auton8 \
 && useradd --system --uid 64198 --gid 64198 --create-home --home-dir /home/auton8 auton8 \
 && mkdir -p /data \
 && chown -R auton8:auton8 /data

WORKDIR /app
COPY --from=build --chown=auton8:auton8 /app/publish ./

# Runtime data: uploads, persisted plugins, public /files assets, per-plugin
# scratch. Its content is the runtime's source of truth between restarts, so a
# deployment that does not mount storage here loses it on replacement.
ENV Data__Root=/data
VOLUME ["/data"]

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true
EXPOSE 8080

# Probes the one deliberately anonymous endpoint. The other two health
# endpoints require SiteConfig:View because they expose component status and
# internal topology, and a container healthcheck carries no credentials.
HEALTHCHECK --interval=15s --timeout=5s --start-period=45s --retries=4 \
    CMD ["/bin/sh", "-c", "curl -fsS http://127.0.0.1:8080/api/health/live || exit 1"]

USER auton8
ENTRYPOINT ["dotnet", "AutoNate.Web.dll"]
