#!/usr/bin/env bash
set -euo pipefail

action="${1:-preflight}"
evidence_root="${ASSETLIBRARY_EVIDENCE_ROOT:-}"
expected_host="${ASSETLIBRARY_EXPECTED_HOSTNAME:-}"
approve="${ASSETLIBRARY_APPROVE_SYSTEM_CHANGES:-0}"
expected_source_revision="${ASSETLIBRARY_EXPECTED_SOURCE_REVISION:-}"
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

artifact="$evidence_root/artifact/linux-x64/AssetLibrary.CoreServer.Host"
root_marker="$evidence_root/.assetlibrary-v01-008-evidence-root"
state="$evidence_root/state"
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
    && command -v realpath >/dev/null 2>&1
}

has_reparse_or_link() {
  local current="$1"
  while [[ "$current" == "$task_root"/* ]]; do
    [[ -L "$current" ]] && return 0
    current="$(dirname "$current")"
  done
  return 1
}

residue_json() {
  local unit=false state_present=false process_count=0 listener_count=0
  [[ -e "$unit_target" ]] && unit=true
  [[ -e "$state" ]] && state_present=true
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
  printf '{"unit":%s,"state":%s,"process_count":%s,"listener_count":%s}' \
    "$unit" "$state_present" "$process_count" "$listener_count"
}

if [[ "$action" == preflight ]]; then
  boundary=false artifact_present=false marker_present=false
  [[ -f "$root_marker" ]] && marker_present=true
  [[ -x "$artifact" ]] && artifact_present=true
  if [[ "$marker_present" == true ]] && ! has_reparse_or_link "$evidence_root"; then boundary=true; fi
  printf '{"contract":"v01-008/1","action":"preflight","root":%s,"host_matches":%s,"source_revision_supplied":%s,"tools_present":%s,"boundary_valid":%s,"artifact_present":%s,"residue":%s}\n' \
    "$([[ "$(id -u)" == 0 ]] && echo true || echo false)" \
    "$([[ -n "$expected_host" && "$(hostname)" == "$expected_host" ]] && echo true || echo false)" \
    "$([[ "$expected_source_revision" =~ ^[0-9a-f]{40}$ ]] && echo true || echo false)" \
    "$(tools_present && echo true || echo false)" "$boundary" "$artifact_present" "$(residue_json)"
  exit 0
fi

[[ "$approve" == 1 ]] || { echo 'system changes require ASSETLIBRARY_APPROVE_SYSTEM_CHANGES=1' >&2; exit 64; }
[[ "$(id -u)" == 0 ]] || { echo 'systemd evidence requires root' >&2; exit 77; }
tools_present || { echo 'systemd, systemd-analyze, pgrep, ss, sed and realpath are required' >&2; exit 77; }
[[ -n "$expected_host" && "$(hostname)" == "$expected_host" ]] || { echo 'hostname confirmation mismatch' >&2; exit 64; }
[[ -f "$root_marker" ]] || { echo 'evidence root marker missing' >&2; exit 64; }
! has_reparse_or_link "$evidence_root" || { echo 'symlink boundary refused' >&2; exit 64; }
id "$evidence_user" >/dev/null 2>&1 || { echo 'approved non-root evidence user does not exist' >&2; exit 77; }
evidence_group="$(id -gn "$evidence_user")"
[[ "$evidence_user" =~ ^[a-z_][a-z0-9_-]*$ && "$evidence_group" =~ ^[a-z_][a-z0-9_-]*$ ]] \
  || { echo 'evidence identity contains unsupported characters' >&2; exit 64; }
case "$artifact:$state" in
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
  if [[ -e "$state" ]]; then
    case "$state" in "$evidence_root"/*) ;; *) echo 'state escaped evidence root' >&2; return 1 ;; esac
    [[ ! -L "$state" && -f "$state_marker" ]] || { echo 'refusing unowned state' >&2; return 1; }
    rm -rf --one-file-system -- "$state"
  fi
}

wait_for_health() {
  local deadline=$((SECONDS + 30))
  while (( SECONDS < deadline )); do
    if "$artifact" --health-probe --probe-host 127.0.0.1 --port 5089 >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.5
  done
  return 1
}

if [[ "$action" == cleanup ]]; then cleanup_owned; action=verify-absent; fi
if [[ "$action" == verify-absent ]]; then
  residue="$(residue_json)"
  [[ "$residue" == '{"unit":false,"state":false,"process_count":0,"listener_count":0}' ]] || { echo 'systemd residue present' >&2; exit 1; }
  printf '{"contract":"v01-008/1","action":"verify-absent","residue":%s}\n' "$residue"
  exit 0
fi

[[ -x "$artifact" ]] || { echo 'linux-x64 artifact missing or not executable' >&2; exit 66; }
[[ "$expected_source_revision" =~ ^[0-9a-f]{40}$ ]] \
  || { echo 'cycle requires ASSETLIBRARY_EXPECTED_SOURCE_REVISION as a full lowercase commit' >&2; exit 64; }
build_info="$("$artifact" --build-info)"
[[ "$build_info" == *'"contract":"v01-008/1"'* \
  && "$build_info" == *"\"source_revision\":\"$expected_source_revision\""* ]] \
  || { echo 'artifact build-info does not match expected release provenance' >&2; exit 1; }
[[ "$(residue_json)" == '{"unit":false,"state":false,"process_count":0,"listener_count":0}' ]] || { echo 'pre-existing evidence state' >&2; exit 1; }

passed=false
started_at="$(date +%s)"
trap 'cleanup_owned' EXIT
mkdir -m 0700 -- "$state"
touch -- "$state_marker"
chown -R "$evidence_user:$evidence_group" -- "$state"
chmod 0555 -- "$artifact"
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
passed=true
cleanup_owned
trap - EXIT
residue="$(residue_json)"
[[ "$passed" == true && "$residue" == '{"unit":false,"state":false,"process_count":0,"listener_count":0}' ]] || { echo 'systemd cycle failed or left residue' >&2; exit 1; }
printf '{"contract":"v01-008/1","status":"passed","target":"linux_systemd","identity":"%s","source_revision":"%s","elapsed_seconds":%s,"residue":%s}\n' \
  "$evidence_user" "$expected_source_revision" "$(( $(date +%s) - started_at ))" "$residue"
