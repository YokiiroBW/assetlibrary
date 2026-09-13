#!/bin/sh
# Thin Docker/Compose adapter. Python, PG clients and certificate tools stay in setup.
set -eu
umask 0077
cd "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"

die() { printf '%s\n' "$1" >&2; exit 1; }
value() {
    result=$(sed -n "s/^$1=//p" "$2")
    [ -n "$result" ] || die "Missing deployment field: $1"
    printf '%s' "$result"
}
optional_value() {
    result=$(awk -v key="$1" 'index($0, key "=") == 1 {count++; value=substr($0, length(key)+2)} END {if (count > 1) exit 2; if (count == 1) print value}' "$2") \
        || die 'Duplicate optional deployment field.'
    printf '%s' "$result"
}
image_backend() {
    stored_socket=$(optional_value ASSETLIBRARY_IMAGE_PREVIEW_SOCKET deployment.env)
    stored_worker=$(optional_value ASSETLIBRARY_IMAGE_PREVIEW_WORKER deployment.env)
    image_socket=${ASSETLIBRARY_IMAGE_PREVIEW_SOCKET-$stored_socket}
    image_worker=${ASSETLIBRARY_IMAGE_PREVIEW_WORKER-$stored_worker}
    case "$image_socket" in ''|/run/assetlibrary-image/decoder.sock) ;; *) die 'Invalid fixed image socket path.' ;; esac
    case "$image_worker" in ''|/app/workers/image-preview/AssetLibrary.ImagePreview.Worker) ;; *) die 'Invalid packaged image worker path.' ;; esac
    [ -z "$image_socket" ] || [ -z "$image_worker" ] || die 'Select either the image socket or the local worker, not both.'
    export ASSETLIBRARY_IMAGE_PREVIEW_SOCKET="$image_socket" ASSETLIBRARY_IMAGE_PREVIEW_WORKER="$image_worker"
}
verify_public_files() {
    # Recheck small immutable control files before starting a container. The
    # potentially large images.tar is checked by load, not by every status call.
    [ "$(wc -c < SHA256SUMS)" -le 8192 ] || die 'Deployment checksum list exceeds its bound.'
    for public in compose.yaml nasctl.sh images.env build-manifest.json settings.example.json README.md; do
        digest=$(awk -v file="$public" '$2 == file && NF == 2 {print $1}' SHA256SUMS)
        [ "${#digest}" -eq 64 ] || die 'Invalid deployment checksum list.'
        case "$digest" in *[!0-9a-f]*) die 'Invalid deployment checksum.' ;; esac
        printf '%s  %s\n' "$digest" "$public" | sha256sum --check >/dev/null || die 'Deployment control file changed; use the verified package.'
    done
}
compose() (
    unset COMPOSE_PROJECT_NAME COMPOSE_FILE COMPOSE_PROFILES COMPOSE_PATH_SEPARATOR COMPOSE_REMOVE_ORPHANS
    read_group=$(optional_value ASSETLIBRARY_ASSET_READ_GROUP deployment.env)
    read_group=${read_group:-1654}
    case "$read_group" in *[!0-9]*|0) die 'Invalid asset read group.' ;; esac
    export ASSETLIBRARY_ASSET_READ_GROUP="$read_group"
    for key in ASSETLIBRARY_DEPLOYMENT_NAME ASSETLIBRARY_DEPLOYMENT_ID ASSETLIBRARY_BIND_ADDRESS ASSETLIBRARY_HTTPS_PORT ASSETLIBRARY_SETTINGS_SHA256; do
        export "$key=$(value "$key" deployment.env)"
    done
    for key in ASSETLIBRARY_SOURCE_REVISION ASSETLIBRARY_CORE_IMAGE ASSETLIBRARY_CORE_IMAGE_ID ASSETLIBRARY_SETUP_IMAGE ASSETLIBRARY_SETUP_IMAGE_ID ASSETLIBRARY_POSTGRES_IMAGE ASSETLIBRARY_POSTGRES_IMAGE_ID ASSETLIBRARY_IMAGE_PREVIEW_IMAGE ASSETLIBRARY_IMAGE_PREVIEW_IMAGE_ID; do
        export "$key=$(value "$key" images.env)"
    done
    exec docker compose --project-name "$ASSETLIBRARY_DEPLOYMENT_NAME" --env-file deployment.env -f compose.yaml -f assets.compose.json "$@"
)
images() {
    setup_image=$(value ASSETLIBRARY_SETUP_IMAGE images.env)
    core_image=$(value ASSETLIBRARY_CORE_IMAGE images.env)
    [ "$(docker image inspect --format '{{.Id}}' "$setup_image")" = "$(value ASSETLIBRARY_SETUP_IMAGE_ID images.env)" ] || die 'Setup image identity changed.'
    [ "$(docker image inspect --format '{{.Id}}' "$core_image")" = "$(value ASSETLIBRARY_CORE_IMAGE_ID images.env)" ] || die 'Core image identity changed.'
    pg_image=$(value ASSETLIBRARY_POSTGRES_IMAGE images.env)
    [ "$(docker image inspect --format '{{.Id}}' "$pg_image")" = "$(value ASSETLIBRARY_POSTGRES_IMAGE_ID images.env)" ] || die 'PostgreSQL image identity changed.'
    image_image=$(value ASSETLIBRARY_IMAGE_PREVIEW_IMAGE images.env)
    [ "$(docker image inspect --format '{{.Id}}' "$image_image")" = "$(value ASSETLIBRARY_IMAGE_PREVIEW_IMAGE_ID images.env)" ] || die 'Image supervisor image identity changed.'
}
owned_resources() {
    name=$(value ASSETLIBRARY_DEPLOYMENT_NAME deployment.env)
    deployment=$(value ASSETLIBRARY_DEPLOYMENT_ID deployment.env)
    for suffix in core-state postgres-data postgres-tls setup-state image-ipc; do
        volume="$name-$suffix"
        if docker volume inspect "$volume" >/dev/null 2>&1; then
            [ "$(docker volume inspect --format '{{index .Labels "io.assetlibrary.deployment"}}' "$volume")" = "$deployment" ] || die 'Foreign volume name collision; nothing was changed.'
            if [ "$suffix" = image-ipc ]; then
                [ "$(docker volume inspect --format '{{.Driver}}' "$volume")" = local ] || die 'Image IPC volume must use the local driver.'
                options=$(docker volume inspect --format '{{json .Options}}' "$volume")
                case "$options" in null|'{}') ;; *) die 'Image IPC volume must not alias a host or remote path.' ;; esac
            fi
        fi
    done
    for suffix in database egress; do
        network="${name}_$suffix"
        if docker network inspect "$network" >/dev/null 2>&1; then
            [ "$(docker network inspect --format '{{index .Labels "io.assetlibrary.deployment"}}' "$network")" = "$deployment" ] || die 'Foreign network name collision; nothing was changed.'
        fi
    done
    for container in $(docker ps -aq --filter "label=com.docker.compose.project=$name"); do
        [ "$(docker inspect --format '{{index .Config.Labels "io.assetlibrary.deployment"}}' "$container")" = "$deployment" ] || die 'Foreign Compose container; nothing was changed.'
    done
}
preflight() {
    [ -f deployment.env ] && [ -f assets.compose.json ] || die 'Run configure first.'
    verify_public_files
    image_backend
    images
    deployment=$(value ASSETLIBRARY_DEPLOYMENT_ID deployment.env)
    current=$(docker run --rm -i --network none "$setup_image" render-env --deployment-id "$deployment" < settings.json)
    stored=$(for key in ASSETLIBRARY_DEPLOYMENT_NAME ASSETLIBRARY_DEPLOYMENT_ID ASSETLIBRARY_BIND_ADDRESS ASSETLIBRARY_HTTPS_PORT ASSETLIBRARY_SETTINGS_SHA256; do printf '%s=%s\n' "$key" "$(value "$key" deployment.env)"; done)
    [ "$current" = "$stored" ] || die 'Settings or deployment parameters changed; review an explicit reconfiguration.'
    mounts=$(docker run --rm -i --network none "$setup_image" render-mounts < settings.json)
    [ "$mounts" = "$(cat assets.compose.json)" ] || die 'Asset mount source or readonly settings changed; nothing was changed.'
    owned_resources
}
prepare_image_volume() {
    name=$(value ASSETLIBRARY_DEPLOYMENT_NAME deployment.env)
    deployment=$(value ASSETLIBRARY_DEPLOYMENT_ID deployment.env)
    volume="$name-image-ipc"
    if ! docker volume inspect "$volume" >/dev/null 2>&1; then
        docker volume create --label "io.assetlibrary.deployment=$deployment" --label io.assetlibrary.product=nas-read-only-v1 "$volume" >/dev/null
    fi
    owned_resources
    # The same immutable supervisor image prepares Docker's empty-volume
    # copy-up metadata. No process starts; no state/fuse file is removed.
    preparation=$(docker create --name "$name-image-ipc-prepare-$$" --network none --read-only --cap-drop ALL \
        --label "io.assetlibrary.deployment=$deployment" --entrypoint /bin/false \
        --mount "type=volume,source=$volume,target=/run/assetlibrary-image" "$image_image")
    [ "${#preparation}" -eq 64 ] || die 'Invalid IPC preparation container identity; inspect the failed operation.'
    case "$preparation" in ''|*[!0-9a-f]*) die 'Invalid IPC preparation container identity; inspect the failed operation.' ;; esac
    docker rm "$preparation" >/dev/null
}
verify_image() {
    container=$(compose --profile image ps --all --quiet image)
    case "$container" in ''|*[!0-9a-f]*) return 1 ;; esac
    docker inspect "$container" | docker run --rm -i --network none --read-only --cap-drop ALL \
        --security-opt no-new-privileges:true "$setup_image" verify-image-container \
        --deployment-id "$(value ASSETLIBRARY_DEPLOYMENT_ID deployment.env)" \
        --deployment-name "$(value ASSETLIBRARY_DEPLOYMENT_NAME deployment.env)" \
        --image-id "$(value ASSETLIBRARY_IMAGE_PREVIEW_IMAGE_ID images.env)" \
        --source-revision "$(value ASSETLIBRARY_SOURCE_REVISION images.env)" "$@"
}

action=${1:-help}
[ "$#" -eq 0 ] || shift
mkdir .nasctl.lock 2>/dev/null || die 'Another deployment operation is running (or its lock requires inspection).'
trap 'rmdir .nasctl.lock' EXIT HUP INT TERM
case "$action" in
    load)
        sha256sum --check SHA256SUMS
        docker load --input images.tar
        images
        ;;
    configure)
        verify_public_files
        [ -f settings.json ] || die 'Copy and edit settings.example.json as settings.json first.'
        [ ! -e deployment.env ] && [ ! -e assets.compose.json ] || die 'Configuration already exists; keep the original deployment identity.'
        images
        deployment=$(docker run --rm --network none "$setup_image" identity)
        [ ! -e deployment.env.pending ] && [ ! -e assets.compose.json.pending ] || die 'An incomplete configuration exists; preserve and inspect it first.'
        docker run --rm -i --network none "$setup_image" render-env --deployment-id "$deployment" < settings.json > deployment.env.pending
        cat images.env >> deployment.env.pending
        docker run --rm -i --network none "$setup_image" render-mounts < settings.json > assets.compose.json.pending
        mv deployment.env.pending deployment.env
        mv assets.compose.json.pending assets.compose.json
        printf '%s\n' 'Configured. Run initialize, then operator bootstrap, then start.'
        ;;
    initialize)
        preflight
        prepare_image_volume
        compose run --rm --no-deps -T setup prepare
        compose up --detach --wait --wait-timeout 120 postgres
        compose run --rm --no-deps -T setup migrate
        compose run --rm --no-deps -T setup ensure-key
        printf '%s\n' 'Initialized; data and private keys persist in this deployment volumes.'
        ;;
    operator)
        preflight
        operation=${1:?select bootstrap, recover or rotate-key}
        shift
        compose up --detach --wait --wait-timeout 120 postgres
        if [ -t 0 ]; then
            compose run --rm --no-deps setup operator --operation "$operation" "$@"
        else
            compose run --rm --no-deps -T setup operator --operation "$operation" "$@"
        fi
        ;;
    start)
        preflight
        prepare_image_volume
        compose up --detach --wait --wait-timeout 120 core
        if [ -n "$image_socket" ]; then
            if [ "$(docker info --format '{{.MemoryLimit}}')" = true ] \
                && compose --profile image create image && verify_image \
                && compose --profile image up --detach --no-deps --wait --wait-timeout 40 image \
                && verify_image --running; then
                printf '%s\n' 'Core is running; image container configuration and health verified. Target platform isolation evidence remains required.'
            else
                # Only this already ownership-checked service is stopped. A
                # failed policy check must not leave a decoder accepting work.
                compose --profile image stop --timeout 10 image || printf '%s\n' 'Image service stop did not complete; inspect this deployment before retrying.' >&2
                printf '%s\n' 'Core is running, but image preview is unavailable. Inspect image health and the retained IPC state; do not delete its failure counter.' >&2
                exit 1
            fi
        else
            compose --profile image stop --timeout 10 image
            printf '%s\n' 'Core is running; the image socket backend is disabled.'
        fi
        ;;
    status)
        preflight
        compose --profile image ps
        [ -z "$image_socket" ] || verify_image --running
        ;;
    stop)
        preflight
        compose stop --timeout 60 core
        compose --profile image stop --timeout 10 image
        compose stop --timeout 60 postgres
        ;;
    down)
        preflight
        compose --profile image --profile setup down --timeout 60
        printf '%s\n' 'Containers/network removed; persistent volumes and assets retained.'
        ;;
    certificate)
        preflight
        compose run --rm --no-deps -T setup certificate
        ;;
    *)
        printf '%s\n' 'Usage: ./nasctl.sh load|configure|initialize|start|status|stop|down|certificate' \
            '       ./nasctl.sh operator bootstrap --account admin --display-name Administrator' \
            '       ./nasctl.sh operator recover --account admin --new-attempt' \
            '       ./nasctl.sh operator rotate-key' \
            'Automation may supply --password-stdin; never put a password in an argument.'
        ;;
esac
