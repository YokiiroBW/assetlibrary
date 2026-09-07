FROM sha256:7d5e945e9b7738f524bafa3c81678634a331dee130d838f8d0242ec5f8da76e3 AS core
USER 0:0
RUN chmod 0555 /app/entrypoint.sh && mkdir -p /var/lib/assetlibrary /etc/dotnet \
    && printf '%s\n' /opt/dotnet > /etc/dotnet/install_location_x64 \
    && chown 1654:1654 /var/lib/assetlibrary
USER 1654:1654
ARG SOURCE_REVISION
LABEL org.opencontainers.image.revision="$SOURCE_REVISION"

FROM sha256:b19b6d248f0fca6dd844661125bc300d3ab9ffbee58240aebe0fad5ef9b758bc AS setup
ARG SOURCE_REVISION
LABEL org.opencontainers.image.revision="$SOURCE_REVISION"
