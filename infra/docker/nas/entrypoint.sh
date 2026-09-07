#!/bin/sh
set -eu
umask 0077
exec /app/AssetLibrary.CoreServer.Host "$@"
