#!/usr/bin/env python3
"""Run the explicit V01-008 Docker lifecycle on an isolated real daemon."""
from __future__ import annotations

import sys

if not sys.flags.isolated:
    raise SystemExit("release tooling must run with Python isolated mode (-I)")

import argparse
import json
import os
import shutil
import socket
import subprocess
import time
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import build_server_release as release  # noqa: E402


CONTRACT = "v01-008/1"
PROJECT_NAME = "assetlibrary-v01-008-evidence"
IMAGE_NAME = "assetlibrary/core-server:v01-008-evidence"
COMPOSE_FILE = ROOT / "infra/docker/compose.yaml"


class DockerEvidenceError(RuntimeError):
    pass


def run(
    arguments: list[str],
    *,
    environment: dict[str, str],
    check: bool = True,
    timeout: int = 120,
) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(
            arguments,
            cwd=ROOT,
            env=environment,
            text=True,
            capture_output=True,
            timeout=timeout,
            check=check,
        )
    except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as exception:
        raise DockerEvidenceError(f"Docker command failed at stage {arguments[1]}") from exception


def docker_available(docker: str, environment: dict[str, str]) -> bool:
    try:
        result = subprocess.run(
            [docker, "info", "--format", "{{.ServerVersion}}"],
            cwd=ROOT,
            env=environment,
            text=True,
            capture_output=True,
            timeout=15,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    return result.returncode == 0 and bool(result.stdout.strip())


def compose_command(
    docker: str,
    *arguments: str,
    compose_file: Path = COMPOSE_FILE,
) -> list[str]:
    return [
        docker,
        "compose",
        "--project-name",
        PROJECT_NAME,
        "--file",
        str(compose_file),
        *arguments,
    ]


def count_lines(result: subprocess.CompletedProcess[str]) -> int:
    return len([line for line in result.stdout.splitlines() if line.strip()])


def residue(docker: str, environment: dict[str, str]) -> dict[str, int]:
    label = f"com.docker.compose.project={PROJECT_NAME}"
    return {
        "containers": count_lines(
            run([docker, "ps", "-aq", "--filter", f"label={label}"], environment=environment)
        ),
        "networks": count_lines(
            run([docker, "network", "ls", "-q", "--filter", f"label={label}"], environment=environment)
        ),
        "volumes": count_lines(
            run([docker, "volume", "ls", "-q", "--filter", f"label={label}"], environment=environment)
        ),
        "images": count_lines(
            run([docker, "image", "ls", "-q", IMAGE_NAME], environment=environment)
        ),
    }


def wait_healthy(docker: str, container: str, environment: dict[str, str]) -> None:
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        result = run(
            [docker, "inspect", "--format", "{{if .State.Health}}{{.State.Health.Status}}{{end}}", container],
            environment=environment,
        )
        status = result.stdout.strip()
        if status == "healthy":
            return
        if status == "unhealthy":
            raise DockerEvidenceError("container became unhealthy")
        time.sleep(1)
    raise DockerEvidenceError("container did not become healthy within 90 seconds")


def inspect_container(docker: str, container: str, environment: dict[str, str]) -> dict[str, Any]:
    payload = json.loads(run([docker, "inspect", container], environment=environment).stdout)
    if not isinstance(payload, list) or len(payload) != 1:
        raise DockerEvidenceError("Docker inspect returned an unexpected payload")
    return payload[0]


def assert_hardening(details: dict[str, Any], source_revision: str) -> None:
    config = details.get("Config", {})
    host = details.get("HostConfig", {})
    if config.get("User") != "1654:1654":
        raise DockerEvidenceError("container identity is not 1654:1654")
    if host.get("ReadonlyRootfs") is not True:
        raise DockerEvidenceError("container root filesystem is not read-only")
    security = host.get("SecurityOpt") or []
    if "no-new-privileges:true" not in security:
        raise DockerEvidenceError("no-new-privileges is not active")
    drops = {str(value).upper() for value in (host.get("CapDrop") or [])}
    if "ALL" not in drops:
        raise DockerEvidenceError("container capabilities were not all dropped")
    if config.get("Labels", {}).get("org.opencontainers.image.revision") != source_revision:
        raise DockerEvidenceError("image revision label does not match the issuance commit")


def zero_residue(value: dict[str, int]) -> bool:
    return all(count == 0 for count in value.values())


def free_loopback_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", 0))
        return int(listener.getsockname()[1])


def write_evidence(output_root: Path, payload: dict[str, Any]) -> None:
    release.write_json(output_root / "docker-evidence.json", payload)


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--execute",
        action="store_true",
        help="explicitly authorize build/up/restart/down on the isolated Docker daemon",
    )
    parser.add_argument("--docker", help="Path to the Docker CLI")
    parser.add_argument("--source-revision", help="Full issuance commit; defaults to the clean-tree policy revision")
    parser.add_argument("--output-root", type=Path)
    return parser.parse_args()


def main() -> int:
    arguments = parse_arguments()
    docker = arguments.docker or shutil.which("docker")
    environment = os.environ.copy()
    if not docker or not docker_available(docker, environment):
        print(
            json.dumps(
                {
                    "contract": CONTRACT,
                    "target": "docker",
                    "status": "blocked_missing_environment",
                    "reason": "usable_docker_daemon_absent",
                },
                sort_keys=True,
            )
        )
        return 3
    if not arguments.execute:
        print(
            json.dumps(
                {
                    "contract": CONTRACT,
                    "target": "docker",
                    "status": "blocked_missing_environment",
                    "reason": "explicit_execution_not_requested",
                },
                sort_keys=True,
            )
        )
        return 3

    policy = json.loads((ROOT / "eng/server-release-policy.json").read_text(encoding="utf-8"))
    task_root = release.initialize_task_root(ROOT, policy["artifact_root"])
    output_root = (arguments.output_root or task_root / "docker-evidence").absolute()
    release.reset_owned_output(task_root, output_root)
    source_revision, _ = release.resolve_provenance(ROOT)
    if arguments.source_revision is not None and arguments.source_revision != source_revision:
        raise DockerEvidenceError("explicit source revision does not match clean-tree provenance")
    environment["ASSETLIBRARY_SOURCE_REVISION"] = source_revision
    environment["ASSETLIBRARY_EVIDENCE_PORT"] = str(free_loopback_port())

    before = residue(docker, environment)
    if not zero_residue(before):
        payload = {
            "contract": CONTRACT,
            "target": "docker",
            "status": "failed",
            "reason": "preexisting_task_named_resources",
            "residue": before,
        }
        write_evidence(output_root, payload)
        print(json.dumps(payload, sort_keys=True))
        return 1

    snapshot_root = output_root / "source-snapshot"
    source_root = release.create_source_snapshot(ROOT, source_revision, snapshot_root)
    compose_file = source_root / "infra/docker/compose.yaml"
    environment["ASSETLIBRARY_BUILD_CONTEXT"] = str(source_root)
    started = time.monotonic()
    created = False
    failure: str | None = None
    observations: dict[str, Any] = {}
    try:
        run(
            compose_command(
                docker,
                "build",
                "--no-cache",
                "--pull",
                compose_file=compose_file,
            ),
            environment=environment,
            timeout=1200,
        )
        run(
            compose_command(
                docker,
                "up",
                "--detach",
                "--wait",
                "--wait-timeout",
                "90",
                compose_file=compose_file,
            ),
            environment=environment,
        )
        created = True
        container = run(
            compose_command(
                docker,
                "ps",
                "--quiet",
                "core-server",
                compose_file=compose_file,
            ),
            environment=environment,
        ).stdout.strip()
        if not container:
            raise DockerEvidenceError("Compose did not return the CoreServer container")
        wait_healthy(docker, container, environment)
        details = inspect_container(docker, container, environment)
        assert_hardening(details, source_revision)

        root_write = run(
            [docker, "exec", container, "sh", "-c", "touch /assetlibrary-root-write-must-fail"],
            environment=environment,
            check=False,
        )
        if root_write.returncode == 0:
            raise DockerEvidenceError("read-only root filesystem accepted a write")
        state_write = run(
            [
                docker,
                "exec",
                container,
                "sh",
                "-c",
                "p=/var/lib/assetlibrary/.v01-008-write-probe; printf probe >\"$p\" && rm -f \"$p\"",
            ],
            environment=environment,
            check=False,
        )
        if state_write.returncode != 0:
            raise DockerEvidenceError("declared state volume was not writable")
        build_info = json.loads(
            run(
                [docker, "exec", container, "/app/AssetLibrary.CoreServer.Host", "--build-info"],
                environment=environment,
            ).stdout
        )
        if build_info.get("contract") != CONTRACT or build_info.get("source_revision") != source_revision:
            raise DockerEvidenceError("container build-info provenance mismatch")

        run(
            compose_command(
                docker,
                "restart",
                "core-server",
                compose_file=compose_file,
            ),
            environment=environment,
        )
        wait_healthy(docker, container, environment)
        observations = {
            "identity": "1654:1654",
            "root_filesystem_read_only": True,
            "state_volume_writable": True,
            "no_new_privileges": True,
            "capabilities_dropped": "ALL",
            "health_after_restart": "healthy",
            "source_revision": source_revision,
        }
    except (DockerEvidenceError, json.JSONDecodeError) as exception:
        failure = str(exception)
    finally:
        if created or not zero_residue(residue(docker, environment)):
            try:
                run(
                    compose_command(
                        docker,
                        "down",
                        "--volumes",
                        "--remove-orphans",
                        "--rmi",
                        "all",
                        "--timeout",
                        "20",
                        compose_file=compose_file,
                    ),
                    environment=environment,
                    timeout=180,
                )
            except DockerEvidenceError as exception:
                failure = failure or str(exception)
        try:
            release.remove_source_snapshot(snapshot_root)
        except release.ReleaseBuildError as exception:
            failure = failure or str(exception)

    after = residue(docker, environment)
    if not zero_residue(after):
        failure = failure or "Docker lifecycle left task-named residue"
    payload = {
        "contract": CONTRACT,
        "target": "docker",
        "status": "failed" if failure else "passed",
        "source_revision": source_revision,
        "elapsed_seconds": round(time.monotonic() - started, 3),
        "observations": observations,
        "residue": after,
    }
    if failure:
        payload["reason"] = failure
    write_evidence(output_root, payload)
    print(json.dumps(payload, sort_keys=True))
    return 1 if failure else 0


if __name__ == "__main__":
    raise SystemExit(main())
