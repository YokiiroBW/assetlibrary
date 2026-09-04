#!/usr/bin/env bash
set -euo pipefail

action="${1:-preflight}"
evidence_root="${ASSETLIBRARY_EVIDENCE_ROOT:-}"
expected_host="${ASSETLIBRARY_EXPECTED_HOSTNAME:-}"
approve="${ASSETLIBRARY_APPROVE_SYSTEM_CHANGES:-0}"
expected_source_revision="${ASSETLIBRARY_EXPECTED_SOURCE_REVISION:-}"
expected_artifact_tree_sha256="${ASSETLIBRARY_EXPECTED_ARTIFACT_TREE_SHA256:-}"
expected_runtime_evidence_binding_sha256="${ASSETLIBRARY_EXPECTED_RUNTIME_EVIDENCE_BINDING_SHA256:-}"
service_name='assetlibrary-v01-008-evidence.service'
owner_marker='AssetLibrary/V01-008/linux-systemd-evidence/v1'

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo="$(cd "$script_dir/../.." && pwd -P)"
task_root="$repo/.runtime/sandbox-storage/V01-008"
if [[ -z "$evidence_root" ]]; then evidence_root="$task_root/linux-systemd"; fi
command -v realpath >/dev/null 2>&1 || { echo 'realpath is required for boundary validation' >&2; exit 77; }
task_root="$(realpath -m -- "$task_root")"
canonical_evidence_root="$(realpath -m -- "$evidence_root")"
[[ "$canonical_evidence_root" == "$evidence_root" ]] \
  || { echo 'evidence root must already be canonical and contain no links or traversal' >&2; exit 64; }
evidence_root="$canonical_evidence_root"

case "$action" in preflight|cycle|cleanup|verify-absent) ;; *) echo 'invalid action' >&2; exit 64 ;; esac
case "$evidence_root" in "$task_root"/*) ;; *) echo 'evidence root must be a strict task-root descendant' >&2; exit 64 ;; esac

source_artifact="$evidence_root/artifact/linux-x64/AssetLibrary.CoreServer.Host"
root_marker="$evidence_root/.assetlibrary-v01-008-evidence-root"
secure_root='/var/tmp/assetlibrary-v01-008-evidence'
secure_marker="$secure_root/.assetlibrary-v01-008-secure-staging"
secure_marker_value='AssetLibrary/V01-008/linux-secure-staging/v1'
artifact="$secure_root/artifact/linux-x64/AssetLibrary.CoreServer.Host"
state="$secure_root/state"
state_marker="$state/.assetlibrary-v01-008-owned-state"
unit_source="$script_dir/assetlibrary-core-server.service"
unit_target="/etc/systemd/system/$service_name"
evidence_user="${ASSETLIBRARY_EVIDENCE_USER:-nobody}"
evidence_group=""

tools_present() {
  command -v systemctl >/dev/null 2>&1 \
    && command -v pgrep >/dev/null 2>&1 \
    && command -v ss >/dev/null 2>&1 \
    && command -v sed >/dev/null 2>&1 \
    && command -v systemd-analyze >/dev/null 2>&1 \
    && command -v realpath >/dev/null 2>&1 \
    && command -v curl >/dev/null 2>&1 \
    && command -v cp >/dev/null 2>&1 \
    && command -v mkdir >/dev/null 2>&1 \
    && command -v install >/dev/null 2>&1 \
    && command -v chmod >/dev/null 2>&1 \
    && command -v chown >/dev/null 2>&1 \
    && command -v find >/dev/null 2>&1 \
    && command -v sha256sum >/dev/null 2>&1 \
    && command -v sort >/dev/null 2>&1 \
    && command -v stat >/dev/null 2>&1 \
    && command -v id >/dev/null 2>&1 \
    && command -v grep >/dev/null 2>&1 \
    && command -v rm >/dev/null 2>&1 \
    && command -v wc >/dev/null 2>&1 \
    && command -v tr >/dev/null 2>&1 \
    && command -v hostname >/dev/null 2>&1 \
    && command -v date >/dev/null 2>&1 \
    && command -v sleep >/dev/null 2>&1
}

has_reparse_or_link() {
  local current="$1"
  while [[ "$current" == "$task_root"/* ]]; do
    [[ -L "$current" ]] && return 0
    current="$(dirname "$current")"
  done
  return 1
}

artifact_tree_sha256() {
  local root="$1" unexpected path relative digest_line digest length
  [[ -d "$root" ]] || return 66
  unexpected="$(find "$root" ! -type f ! -type d -print -quit)"
  [[ -z "$unexpected" ]] || return 64
  {
    while IFS= read -r -d '' path; do
      relative="${path#"$root"/}"
      case "$relative" in
        *$'\t'*|*$'\r'*|*$'\n'*) exit 64 ;;
      esac
      digest_line="$(sha256sum -- "$path")"
      digest="${digest_line%% *}"
      length="$(stat -c '%s' -- "$path")"
      printf '%s\t%s\t%s\n' "$digest" "$length" "$relative"
    done < <(find "$root" -type f -print0 | LC_ALL=C sort -z)
  } | sha256sum | {
    read -r digest _
    printf '%s' "$digest"
  }
}

runtime_evidence_binding_sha256() {
  printf 'AssetLibrary/V01-008/runtime-evidence-binding/v1\n%s\nlinux-x64\n%s\n' \
    "$expected_source_revision" "$expected_artifact_tree_sha256" \
    | sha256sum | {
      read -r digest _
      printf '%s' "$digest"
    }
}

residue_json() {
  local unit=false state_present=false secure_staging=false process_count=0 listener_count=0
  [[ -e "$unit_target" ]] && unit=true
  [[ -e "$state" ]] && state_present=true
  [[ -e "$secure_root" ]] && secure_staging=true
  if command -v pgrep >/dev/null 2>&1; then
    process_count="$(pgrep -fc 'AssetLibrary.CoreServer.Host.*--port 5089' || true)"
  else
    process_count=-1
  fi
  if command -v ss >/dev/null 2>&1; then
    listener_count="$(ss -lntH 'sport = :5089' 2>/dev/null | wc -l | tr -d ' ')"
  else
    listener_count=-1
  fi
  printf '{"unit":%s,"state":%s,"secure_staging":%s,"process_count":%s,"listener_count":%s}' \
    "$unit" "$state_present" "$secure_staging" "$process_count" "$listener_count"
}

if [[ "$action" == preflight ]]; then
  boundary=false artifact_present=false marker_present=false artifact_tree_sha256_matches=false
  [[ -f "$root_marker" ]] && marker_present=true
  [[ -f "$source_artifact" && -x "$source_artifact" && ! -L "$source_artifact" ]] && artifact_present=true
  if [[ "$marker_present" == true ]] && ! has_reparse_or_link "$evidence_root" && ! has_reparse_or_link "$source_artifact"; then
    boundary=true
  fi
  if tools_present \
    && [[ "$boundary" == true && "$artifact_present" == true && "$expected_artifact_tree_sha256" =~ ^[0-9a-f]{64}$ ]] \
    && [[ "$(artifact_tree_sha256 "$(dirname "$source_artifact")")" == "$expected_artifact_tree_sha256" ]]; then
    artifact_tree_sha256_matches=true
  fi
  printf '{"contract":"v01-008/1","action":"preflight","root":%s,"host_matches":%s,"source_revision_supplied":%s,"artifact_tree_sha256_supplied":%s,"artifact_tree_sha256_matches":%s,"runtime_evidence_binding_supplied":%s,"runtime_evidence_binding_matches":%s,"tools_present":%s,"boundary_valid":%s,"artifact_present":%s,"residue":%s}\n' \
    "$([[ "$(id -u)" == 0 ]] && echo true || echo false)" \
    "$([[ -n "$expected_host" && "$(hostname)" == "$expected_host" ]] && echo true || echo false)" \
    "$([[ "$expected_source_revision" =~ ^[0-9a-f]{40}$ ]] && echo true || echo false)" \
    "$([[ "$expected_artifact_tree_sha256" =~ ^[0-9a-f]{64}$ ]] && echo true || echo false)" \
    "$artifact_tree_sha256_matches" \
    "$([[ "$expected_runtime_evidence_binding_sha256" =~ ^[0-9a-f]{64}$ ]] && echo true || echo false)" \
    "$(tools_present && [[ "$expected_source_revision" =~ ^[0-9a-f]{40}$ && "$expected_artifact_tree_sha256" =~ ^[0-9a-f]{64}$ && "$(runtime_evidence_binding_sha256)" == "$expected_runtime_evidence_binding_sha256" ]] && echo true || echo false)" \
    "$(tools_present && echo true || echo false)" "$boundary" "$artifact_present" "$(residue_json)"
  exit 0
fi

[[ "$approve" == 1 ]] || { echo 'system changes require ASSETLIBRARY_APPROVE_SYSTEM_CHANGES=1' >&2; exit 64; }
[[ "$(id -u)" == 0 ]] || { echo 'systemd evidence requires root' >&2; exit 77; }
tools_present || { echo 'required systemd evidence tooling is unavailable' >&2; exit 77; }
[[ -n "$expected_host" && "$(hostname)" == "$expected_host" ]] || { echo 'hostname confirmation mismatch' >&2; exit 64; }
[[ -f "$root_marker" ]] || { echo 'evidence root marker missing' >&2; exit 64; }
! has_reparse_or_link "$evidence_root" || { echo 'symlink boundary refused' >&2; exit 64; }
id "$evidence_user" >/dev/null 2>&1 || { echo 'approved non-root evidence user does not exist' >&2; exit 77; }
evidence_group="$(id -gn "$evidence_user")"
evidence_uid="$(id -u "$evidence_user")"
evidence_gid="$(id -g "$evidence_user")"
[[ "$evidence_user" =~ ^[a-z_][a-z0-9_-]*$ && "$evidence_group" =~ ^[a-z_][a-z0-9_-]*$ ]] \
  || { echo 'evidence identity contains unsupported characters' >&2; exit 64; }
[[ "$evidence_uid" != 0 && "$evidence_gid" != 0 ]] \
  || { echo 'evidence identity and primary group must both be non-root' >&2; exit 64; }
[[ " $(id -G "$evidence_user") " != *' 0 '* ]] \
  || { echo 'evidence identity may not inherit the root group' >&2; exit 64; }
case "$source_artifact:$artifact:$state" in
  *[$' \t\r\n|']*) echo 'evidence paths may not contain whitespace or sed delimiters' >&2; exit 64 ;;
esac

cleanup_owned() {
  if [[ -e "$unit_target" ]]; then
    grep -Fq "$owner_marker" "$unit_target" || { echo 'refusing unowned unit' >&2; return 1; }
    systemctl stop "$service_name" >/dev/null 2>&1 || true
    systemctl disable "$service_name" >/dev/null 2>&1 || true
    rm -f -- "$unit_target"
    systemctl daemon-reload
  fi
  if [[ -e "$secure_root" ]]; then
    [[ "$secure_root" == /var/tmp/assetlibrary-v01-008-evidence && ! -L "$secure_root" ]] \
      || { echo 'refusing unsafe secure staging root' >&2; return 1; }
    [[ -f "$secure_marker" && ! -L "$secure_marker" ]] \
      && grep -Fxq "$secure_marker_value" "$secure_marker" \
      || { echo 'refusing unowned secure staging' >&2; return 1; }
    rm -rf --one-file-system -- "$secure_root"
  fi
}

create_secure_staging() {
  local path
  [[ ! -e "$secure_root" && ! -L "$secure_root" ]] \
    || { echo 'secure staging already exists; run owned cleanup first' >&2; return 1; }
  mkdir -m 0700 -- "$secure_root"
  chown root:root -- "$secure_root"
  printf '%s\n' "$secure_marker_value" >"$secure_marker"
  install -d -m 0700 -o root -g root -- "$(dirname "$(dirname "$artifact")")" "$(dirname "$artifact")"
  cp -a -- "$(dirname "$source_artifact")/." "$(dirname "$artifact")/"
  [[ -z "$(find "$(dirname "$artifact")" ! -type f ! -type d -print -quit)" ]] \
    || { echo 'secure artifact copy contains a non-regular entry' >&2; return 1; }
  chown -R root:root -- "$secure_root"
  while IFS= read -r -d '' path; do
    if [[ -x "$path" ]]; then chmod 0555 -- "$path"; else chmod 0444 -- "$path"; fi
  done < <(find "$(dirname "$artifact")" -type f -print0)
  find "$secure_root/artifact" -type d -exec chmod 0555 -- {} +
  install -d -m 0700 -o "$evidence_user" -g "$evidence_group" -- "$state"
  install -m 0600 -o "$evidence_user" -g "$evidence_group" /dev/null "$state_marker"
  chmod 0555 -- "$secure_root"
  [[ "$(artifact_tree_sha256 "$(dirname "$artifact")")" == "$expected_artifact_tree_sha256" ]] \
    || { echo 'secure artifact copy does not match the independently supplied SHA-256' >&2; return 1; }
}

wait_for_health() {
  local deadline=$((SECONDS + 30)) response
  while (( SECONDS < deadline )); do
    if response="$(curl --fail --silent --show-error --connect-timeout 1 --max-time 2 \
      --max-redirs 0 --proto '=http' --noproxy '*' 'http://127.0.0.1:5089/healthz' 2>/dev/null)" \
      && [[ "$response" == '{"status":"ok","contract":"v01-008/1"}' ]]; then
      return 0
    fi
    sleep 0.5
  done
  return 1
}

if [[ "$action" == cleanup ]]; then cleanup_owned; action=verify-absent; fi
if [[ "$action" == verify-absent ]]; then
  residue="$(residue_json)"
  [[ "$residue" == '{"unit":false,"state":false,"secure_staging":false,"process_count":0,"listener_count":0}' ]] || { echo 'systemd residue present' >&2; exit 1; }
  printf '{"contract":"v01-008/1","action":"verify-absent","residue":%s}\n' "$residue"
  exit 0
fi

[[ -x "$source_artifact" ]] || { echo 'linux-x64 artifact missing or not executable' >&2; exit 66; }
[[ "$expected_source_revision" =~ ^[0-9a-f]{40}$ ]] \
  || { echo 'cycle requires ASSETLIBRARY_EXPECTED_SOURCE_REVISION as a full lowercase commit' >&2; exit 64; }
[[ "$expected_artifact_tree_sha256" =~ ^[0-9a-f]{64}$ ]] \
  || { echo 'cycle requires ASSETLIBRARY_EXPECTED_ARTIFACT_TREE_SHA256 as a full lowercase digest' >&2; exit 64; }
[[ "$expected_runtime_evidence_binding_sha256" =~ ^[0-9a-f]{64}$ ]] \
  || { echo 'cycle requires ASSETLIBRARY_EXPECTED_RUNTIME_EVIDENCE_BINDING_SHA256 as a full lowercase digest' >&2; exit 64; }
[[ "$(runtime_evidence_binding_sha256)" == "$expected_runtime_evidence_binding_sha256" ]] \
  || { echo 'source revision and tree digest do not match the trusted runtime evidence binding' >&2; exit 1; }
artifact_tree_digest="$(artifact_tree_sha256 "$(dirname "$source_artifact")")"
[[ "$artifact_tree_digest" == "$expected_artifact_tree_sha256" ]] \
  || { echo 'artifact tree does not match the independently supplied SHA-256' >&2; exit 1; }
[[ "$(residue_json)" == '{"unit":false,"state":false,"secure_staging":false,"process_count":0,"listener_count":0}' ]] || { echo 'pre-existing evidence state' >&2; exit 1; }

passed=false
started_at="$(date +%s)"
trap 'cleanup_owned' EXIT
create_secure_staging
install -m 0644 "$unit_source" "$unit_target"
sed -i \
  -e "s|^Description=.*|Description=$owner_marker|" \
  -e "s|User=assetlibrary|User=$evidence_user|" \
  -e "s|Group=assetlibrary|Group=$evidence_group|" \
  -e '/^StateDirectory=/d' \
  -e '/^StateDirectoryMode=/d' \
  -e "s|^WorkingDirectory=.*|WorkingDirectory=$(dirname "$artifact")|" \
  -e "s|/opt/assetlibrary/AssetLibrary.CoreServer.Host|$artifact|g" \
  -e "s|/var/lib/assetlibrary|$state|g" \
  -e 's|--port 5080|--port 5089|g' \
  "$unit_target"
systemd-analyze verify "$unit_target"
systemctl daemon-reload
systemctl start "$service_name"
systemctl is-active --quiet "$service_name"
wait_for_health || { echo 'systemd service did not become healthy within 30 seconds' >&2; exit 1; }
systemctl stop "$service_name"
[[ "$(artifact_tree_sha256 "$(dirname "$artifact")")" == "$expected_artifact_tree_sha256" ]] \
  || { echo 'artifact tree changed during the systemd cycle' >&2; exit 1; }
passed=true
cleanup_owned
trap - EXIT
residue="$(residue_json)"
[[ "$passed" == true && "$residue" == '{"unit":false,"state":false,"secure_staging":false,"process_count":0,"listener_count":0}' ]] || { echo 'systemd cycle failed or left residue' >&2; exit 1; }
printf '{"contract":"v01-008/1","status":"passed","target":"linux_systemd","identity":"%s","source_revision":"%s","artifact_tree_sha256":"%s","runtime_evidence_binding_sha256":"%s","elapsed_seconds":%s,"residue":%s}\n' \
  "$evidence_user" "$expected_source_revision" "$expected_artifact_tree_sha256" "$expected_runtime_evidence_binding_sha256" \
  "$(( $(date +%s) - started_at ))" "$residue"
