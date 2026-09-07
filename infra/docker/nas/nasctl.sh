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
compose() (
    unset COMPOSE_PROJECT_NAME COMPOSE_FILE COMPOSE_PROFILES COMPOSE_PATH_SEPARATOR COMPOSE_REMOVE_ORPHANS
    for key in ASSETLIBRARY_DEPLOYMENT_NAME ASSETLIBRARY_DEPLOYMENT_ID ASSETLIBRARY_BIND_ADDRESS ASSETLIBRARY_HTTPS_PORT ASSETLIBRARY_SETTINGS_SHA256; do
        export "$key=$(value "$key" deployment.env)"
    done
    for key in ASSETLIBRARY_SOURCE_REVISION ASSETLIBRARY_CORE_IMAGE ASSETLIBRARY_CORE_IMAGE_ID ASSETLIBRARY_SETUP_IMAGE ASSETLIBRARY_SETUP_IMAGE_ID ASSETLIBRARY_POSTGRES_IMAGE ASSETLIBRARY_POSTGRES_IMAGE_ID; do
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
}
owned_resources() {
    name=$(value ASSETLIBRARY_DEPLOYMENT_NAME deployment.env)
    deployment=$(value ASSETLIBRARY_DEPLOYMENT_ID deployment.env)
    for suffix in core-state postgres-data postgres-tls setup-state; do
        volume="$name-$suffix"
        if docker volume inspect "$volume" >/dev/null 2>&1; then
            [ "$(docker volume inspect --format '{{index .Labels "io.assetlibrary.deployment"}}' "$volume")" = "$deployment" ] || die 'Foreign volume name collision; nothing was changed.'
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
    images
    deployment=$(value ASSETLIBRARY_DEPLOYMENT_ID deployment.env)
    current=$(docker run --rm -i --network none "$setup_image" render-env --deployment-id "$deployment" < settings.json)
    stored=$(for key in ASSETLIBRARY_DEPLOYMENT_NAME ASSETLIBRARY_DEPLOYMENT_ID ASSETLIBRARY_BIND_ADDRESS ASSETLIBRARY_HTTPS_PORT ASSETLIBRARY_SETTINGS_SHA256; do printf '%s=%s\n' "$key" "$(value "$key" deployment.env)"; done)
    [ "$current" = "$stored" ] || die 'Settings or deployment parameters changed; review an explicit reconfiguration.'
    mounts=$(docker run --rm -i --network none "$setup_image" render-mounts < settings.json)
    [ "$mounts" = "$(cat assets.compose.json)" ] || die 'Asset mount source or readonly settings changed; nothing was changed.'
    owned_resources
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
        compose up --detach --wait --wait-timeout 120 core
        ;;
    status)
        preflight
        compose ps
        ;;
    stop)
        preflight
        compose stop --timeout 60 core
        compose stop --timeout 60 postgres
        ;;
    down)
        preflight
        compose down --timeout 60
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
