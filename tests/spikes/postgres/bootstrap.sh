#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../../.." && pwd)"
base="${M005_RUNTIME:-$repo_root/.runtime/sandbox-storage/M0-005}"
if [[ -n "${M005_PG_BIN:-}" && -x "$M005_PG_BIN/initdb" ]]; then
  printf 'M005_PG_BIN=%s\n' "$M005_PG_BIN"
  "$M005_PG_BIN/postgres" --version
  exit 0
fi
mkdir -p "$base/source" "$base/build" "$base/install"
url="https://ftp.postgresql.org/pub/source/v16.15/postgresql-16.15.tar.bz2"
tarball="$base/source/postgresql-16.15.tar.bz2"
if [[ ! -f "$tarball" ]]; then curl -L --fail --retry 3 -o "$tarball" "$url"; fi
sha256="${M005_PG_SHA256:-c1575341fa7bd40f5274ea465b34390f4dc64cdd0770af327005caaeb9f6b7ed}"
echo "$sha256  $tarball" | sha256sum -c -
if [[ ! -x "$base/install/bin/initdb" ]]; then
  if [[ -e "$base/build/postgresql-16.15" ]]; then rm -rf -- "$base/build/postgresql-16.15"; fi
  cd "$base/build/postgresql-16.15"; ./configure --prefix="$base/install" --without-readline --without-zlib
  make -j"${M005_BUILD_JOBS:-2}"; make install
  (cd contrib/pg_trgm && make -j"${M005_BUILD_JOBS:-2}" && make install)
fi
printf 'M005_RUNTIME=%s\nM005_PG_BIN=%s\n' "$base" "$base/install/bin"
"$base/install/bin/postgres" --version
