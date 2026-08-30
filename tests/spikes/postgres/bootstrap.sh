#!/usr/bin/env bash
set -euo pipefail
base="${M005_RUNTIME:-$(pwd)/.runtime/sandbox-storage/M0-005}"
mkdir -p "$base/source" "$base/build" "$base/install"
url="https://ftp.postgresql.org/pub/source/v16.15/postgresql-16.15.tar.bz2"
tarball="$base/source/postgresql-16.15.tar.bz2"
if [[ ! -f "$tarball" ]]; then curl -L --fail --retry 3 -o "$tarball" "$url"; fi
if [[ -n "${M005_PG_SHA256:-}" ]]; then echo "${M005_PG_SHA256}  $tarball" | sha256sum -c -; fi
if [[ ! -x "$base/install/bin/initdb" ]]; then
  rm -rf "$base/build/postgresql-16.15"; tar -xf "$tarball" -C "$base/build"
  cd "$base/build/postgresql-16.15"; ./configure --prefix="$base/install" --without-readline --without-zlib
  make -j"${M005_BUILD_JOBS:-2}"; make install
  (cd contrib/pg_trgm && make -j"${M005_BUILD_JOBS:-2}" && make install)
fi
echo "M005_PG_BIN=$base/install/bin"; "$base/install/bin/postgres" --version
