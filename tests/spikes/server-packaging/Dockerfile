FROM mcr.microsoft.com/dotnet/sdk:10.0.111 AS build
WORKDIR /src
COPY src/ ./
RUN dotnet publish ServerPackagingSpike.csproj -c Release -r linux-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV SPIKE_BIND_HOST=0.0.0.0
ENV SPIKE_PROBE_HOST=127.0.0.1
ENV SPIKE_PORT=8080
ENV SPIKE_DATA_PATH=/var/lib/assetlibrary-spike
COPY --from=build /out/ ./
RUN mkdir -p /var/lib/assetlibrary-spike && chown $APP_UID:$APP_UID /var/lib/assetlibrary-spike
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --start-period=5s --retries=6 CMD ["/app/ServerPackagingSpike", "--health-probe"]
ENTRYPOINT ["/app/ServerPackagingSpike"]
