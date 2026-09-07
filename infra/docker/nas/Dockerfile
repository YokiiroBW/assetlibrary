ARG DEBIAN_IMAGE=debian:bookworm-slim
ARG NODE_IMAGE=node:24.20.0-bookworm-slim
ARG PYTHON_IMAGE=python:3.13-slim-bookworm
ARG POSTGRES_IMAGE=postgres:16.15-bookworm

FROM ${NODE_IMAGE} AS web
WORKDIR /src
RUN npm install --global pnpm@11.19.0
COPY apps/web/ ./apps/web/
COPY packages/sdk/assetlink/typescript/ ./packages/sdk/assetlink/typescript/
RUN pnpm --dir packages/sdk/assetlink/typescript install --frozen-lockfile --ignore-scripts --network-concurrency=2 --fetch-retries=4 --fetch-retry-maxtimeout=60000 \
    && pnpm --dir apps/web install --frozen-lockfile --ignore-scripts --network-concurrency=2 --fetch-retries=4 --fetch-retry-maxtimeout=60000 \
    && pnpm --dir apps/web run build

FROM ${DEBIAN_IMAGE} AS native-base
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates libicu72 libssl3 libstdc++6 zlib1g tzdata \
    && rm -rf /var/lib/apt/lists/*

# The exact feature-band SDK is available as an official archive even when MCR
# has no sdk:10.0.111 tag. Its published SHA-512 is part of the build contract.
FROM native-base AS server
RUN apt-get update && apt-get install --yes --no-install-recommends curl \
    && curl --fail --show-error --location --retry 3 --connect-timeout 15 --max-time 600 \
       https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.111/dotnet-sdk-10.0.111-linux-x64.tar.gz -o /tmp/dotnet.tar.gz \
    && printf '%s  %s\n' aae221be96a3b510d5b6fffefc69d8ad2fa595a1430299419316bb71c65f260a457ca9af24d044e1709b28a9118798caafec535ccfe58f7767c5acb735c00392 /tmp/dotnet.tar.gz | sha512sum --check - \
    && mkdir -p /opt/dotnet && tar -xzf /tmp/dotnet.tar.gz -C /opt/dotnet \
    && rm /tmp/dotnet.tar.gz && rm -rf /var/lib/apt/lists/*
ENV DOTNET_ROOT=/opt/dotnet PATH=/opt/dotnet:$PATH \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1
ARG SOURCE_REVISION
WORKDIR /src
COPY . ./
RUN test "${#SOURCE_REVISION}" -eq 40 \
    && dotnet restore services/core-server/Host/AssetLibrary.CoreServer.Host.csproj --locked-mode \
       -p:DirectoryBuildPropsPath=/src/Directory.Build.props -p:DirectoryBuildTargetsPath=/src/Directory.Build.targets \
    && dotnet publish services/core-server/Host/AssetLibrary.CoreServer.Host.csproj \
       --configuration Release --self-contained false --no-restore \
       -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:DeterministicSourcePaths=true \
       -p:SourceRevisionId="$SOURCE_REVISION" -p:AssetLibrarySourceRevision="$SOURCE_REVISION" \
       -p:DirectoryBuildPropsPath=/src/Directory.Build.props -p:DirectoryBuildTargetsPath=/src/Directory.Build.targets \
       -p:PathMap=/src=/_/src -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false \
       --output /out

FROM native-base AS core
ARG SOURCE_REVISION
ENV DOTNET_ROOT=/opt/dotnet PATH=/opt/dotnet:$PATH \
    ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
COPY --from=server /opt/dotnet/dotnet /opt/dotnet/dotnet
COPY --from=server /opt/dotnet/host/ /opt/dotnet/host/
COPY --from=server /opt/dotnet/shared/ /opt/dotnet/shared/
WORKDIR /app
COPY --from=server /out/ ./
COPY --from=web /src/apps/web/dist/ ./wwwroot/
COPY infra/docker/nas/entrypoint.sh /app/entrypoint.sh
RUN chmod 0555 /app/entrypoint.sh && mkdir -p /var/lib/assetlibrary && chown 1654:1654 /var/lib/assetlibrary
USER 1654:1654
LABEL org.opencontainers.image.title="AssetLibrary NAS read-only server" \
      org.opencontainers.image.revision="$SOURCE_REVISION" io.assetlibrary.role="nas-core"
HEALTHCHECK --interval=15s --timeout=7s --start-period=20s --retries=4 \
    CMD ["/app/AssetLibrary.CoreServer.Host", "--trial-health-probe", "/var/lib/assetlibrary/trial.json"]
ENTRYPOINT ["/app/entrypoint.sh"]
CMD ["--read-only-trial", "/var/lib/assetlibrary/trial.json"]

FROM ${PYTHON_IMAGE} AS python-tools
FROM ${POSTGRES_IMAGE} AS setup
ARG SOURCE_REVISION
# Same Debian family; the full Python interpreter is only in this one-shot image.
# No Python, migration runner or PostgreSQL client is added to the Core image.
COPY --from=python-tools /usr/local/ /usr/local/
RUN apt-get update && apt-get install --yes --no-install-recommends \
       openssl libbz2-1.0 libexpat1 libffi8 libgdbm6 libgdbm-compat4 liblzma5 libncursesw6 libreadline8 libsqlite3-0 \
    && ldconfig && rm -rf /var/lib/apt/lists/*
COPY --from=core /opt/dotnet/ /opt/dotnet/
COPY --from=core /app/ /app/
COPY database/migrations/production/ /bootstrap/migrations/
COPY infra/docker/nas/bootstrap.py /bootstrap/bootstrap.py
ENV DOTNET_ROOT=/opt/dotnet PATH=/opt/dotnet:/usr/local/bin:$PATH \
    ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production \
    PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
USER 0:0
LABEL org.opencontainers.image.title="AssetLibrary NAS one-shot deployment tools" \
      org.opencontainers.image.revision="$SOURCE_REVISION" io.assetlibrary.role="nas-setup"
ENTRYPOINT ["python3", "-I", "-B", "/bootstrap/bootstrap.py"]
