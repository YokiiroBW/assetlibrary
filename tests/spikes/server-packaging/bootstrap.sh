#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$root/../../.." && pwd)"
runtime="$repo/.runtime/sandbox-storage/M0-004"
sdk="${M0_004_DOTNET_DIR:-$runtime/dotnet-10.0.111}"
obj="$runtime/obj"
bin="$runtime/bin"
if [[ ! -x "$sdk/dotnet" ]]; then
  mkdir -p "$(dirname "$sdk")"
  curl -fsSL --retry 2 https://dot.net/v1/dotnet-install.sh -o "$runtime/dotnet-install.sh"
  bash "$runtime/dotnet-install.sh" --version 10.0.111 --install-dir "$sdk" --no-path
fi
export DOTNET_ROOT="$sdk" PATH="$sdk:$PATH" NUGET_PACKAGES="$runtime/nuget"
echo "SDK: $($sdk/dotnet --version)"
git -C "$repo" status --porcelain --untracked-files=all | grep -q . && { echo 'working tree must be clean before provenance build' >&2; exit 2; } || true
"$sdk/dotnet" restore "$root/src/ServerPackagingSpike.csproj" --packages "$NUGET_PACKAGES" -p:BaseIntermediateOutputPath="$obj/" -p:OutputPath="$bin/"
artifact="$runtime/artifact"
for dir in "$obj" "$bin" "$artifact"; do
  case "$dir" in "$runtime/obj"|"$runtime/bin"|"$runtime/artifact") ;; (*) echo "refusing unexpected cleanup path: $dir" >&2; exit 2 ;; esac
  if [[ -e "$dir" ]]; then find "$dir" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; fi
done
mkdir -p "$artifact"
git -C "$repo" diff --quiet -- tests/spikes/server-packaging .codex/tasks/M0-004.md || { echo 'uncommitted issuance inputs' >&2; exit 2; }
source_commit="$(git -C "$repo" log -1 --format=%H -- tests/spikes/server-packaging .codex/tasks/M0-004.md)"
for rid in linux-x64 win-x64; do
  "$sdk/dotnet" publish "$root/src/ServerPackagingSpike.csproj" -c Release -r "$rid" --self-contained true -p:DebugType=None -p:DebugSymbols=false -p:BaseIntermediateOutputPath="$obj/" -p:OutputPath="$bin/" -o "$artifact/$rid"
done
chmod u+x "$artifact/linux-x64/ServerPackagingSpike" || true
printf '%s\n' "$source_commit" > "$artifact/source-commit.txt"
sha256sum "$artifact/linux-x64/ServerPackagingSpike" "$artifact/win-x64/ServerPackagingSpike.exe" > "$artifact/hashes.txt"
(
  cd "$artifact"
  find linux-x64 win-x64 -type f -printf '%p %s bytes\n' | sort > file-sizes.txt
  find linux-x64 win-x64 -type f -print0 | sort -z | xargs -0 sha256sum > files.sha256
)
sha256sum "$artifact/files.sha256" | cut -d' ' -f1 > "$artifact/aggregate-sha256.txt"
stat -c '%n %s bytes' "$artifact/linux-x64/ServerPackagingSpike" "$artifact/win-x64/ServerPackagingSpike.exe"
