FROM mcr.microsoft.com/dotnet/sdk:10.0.111 AS build
ARG SOURCE_REVISION
WORKDIR /src
COPY . ./
RUN test "${#SOURCE_REVISION}" -eq 40 \
    && dotnet restore services/core-server/Host/AssetLibrary.CoreServer.Host.csproj \
      --locked-mode \
      -p:DirectoryBuildPropsPath=/src/Directory.Build.props \
      -p:DirectoryBuildTargetsPath=/src/Directory.Build.targets \
    && dotnet publish services/core-server/Host/AssetLibrary.CoreServer.Host.csproj \
      --configuration Release --self-contained false --no-restore \
      -p:ContinuousIntegrationBuild=true \
      -p:Deterministic=true -p:DeterministicSourcePaths=true \
      -p:SourceRevisionId="$SOURCE_REVISION" \
      -p:AssetLibrarySourceRevision="$SOURCE_REVISION" \
      -p:DirectoryBuildPropsPath=/src/Directory.Build.props \
      -p:DirectoryBuildTargetsPath=/src/Directory.Build.targets \
      -p:PathMap=/src=/_/src \
      -p:UseSharedCompilation=false \
      -p:DebugType=None -p:DebugSymbols=false \
      --output /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
ARG SOURCE_REVISION
WORKDIR /app
COPY --from=build /out/ ./
RUN mkdir -p /var/lib/assetlibrary \
    && chown 1654:1654 /var/lib/assetlibrary
USER 1654:1654
LABEL org.opencontainers.image.title="AssetLibrary Core Server" \
      org.opencontainers.image.revision="$SOURCE_REVISION" \
      org.opencontainers.image.vendor="AssetLibrary"
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=6 \
  CMD ["/app/AssetLibrary.CoreServer.Host", "--health-probe", "--probe-host", "127.0.0.1", "--port", "8080"]
ENTRYPOINT ["/app/AssetLibrary.CoreServer.Host", "--state-path", "/var/lib/assetlibrary", "--environment", "Production", "--bind-host", "0.0.0.0", "--probe-host", "127.0.0.1", "--port", "8080"]
