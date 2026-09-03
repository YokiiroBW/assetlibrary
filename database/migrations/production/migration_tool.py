#!/usr/bin/env python3
"""Validate, back up, apply, and restore AssetLibrary PostgreSQL migrations."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Sequence


HERE = Path(__file__).resolve().parent
DEFAULT_MANIFEST = HERE / "manifest.json"
FOUNDATION_MODULE = "database-migration-foundation"
NAME_RE = re.compile(r"^[a-z][a-z0-9_]*$")
ROLE_RE = re.compile(r"^assetlibrary_[a-z0-9_]+$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
VERSION_OUTPUT_RE = re.compile(r"\b(\d+)\.(\d+)(?:\.(\d+))?\b")
FORBIDDEN_SQL = (
    (re.compile(r"\bDROP\s+DATABASE\b", re.IGNORECASE), "DROP DATABASE"),
    (re.compile(r"\bCREATE\s+DATABASE\b", re.IGNORECASE), "CREATE DATABASE"),
    (re.compile(r"\bALTER\s+SYSTEM\b", re.IGNORECASE), "ALTER SYSTEM"),
    (re.compile(r"\bCOPY\b[\s\S]*?\bPROGRAM\b", re.IGNORECASE), "COPY PROGRAM"),
    (re.compile(r"(?m)^\s*\\"), "psql meta-command"),
    (re.compile(r"(?m)^\s*(?:BEGIN|START\s+TRANSACTION|COMMIT|ROLLBACK)\s*;", re.IGNORECASE), "transaction control"),
    (re.compile(r"\bSET\s+(?:LOCAL\s+|SESSION\s+)?ROLE\b", re.IGNORECASE), "SET ROLE"),
    (re.compile(r"\bRESET\s+ROLE\b", re.IGNORECASE), "RESET ROLE"),
    (
        re.compile(r"\b(?:SET|RESET)\s+SESSION\s+AUTHORIZATION\b", re.IGNORECASE),
        "SESSION AUTHORIZATION",
    ),
    (re.compile(r"\b(?:CREATE|ALTER|DROP)\s+ROLE\b", re.IGNORECASE), "role DDL"),
    (re.compile(r"\bPASSWORD\b", re.IGNORECASE), "password material"),
)
EXPECTED_MODULES = (
    ("GatewayAuth", "gateway_auth", "assetlibrary_gateway_auth_owner", "assetlibrary_gateway_auth_runtime"),
    ("LibraryStorage", "library_storage", "assetlibrary_library_storage_owner", "assetlibrary_library_storage_runtime"),
    ("AssetIdentity", "asset_identity", "assetlibrary_asset_identity_owner", "assetlibrary_asset_identity_runtime"),
    ("MetadataSidecar", "metadata_sidecar", "assetlibrary_metadata_sidecar_owner", "assetlibrary_metadata_sidecar_runtime"),
    ("ScanReconciliation", "scan_reconciliation", "assetlibrary_scan_reconciliation_owner", "assetlibrary_scan_reconciliation_runtime"),
    ("TransferSync", "transfer_sync", "assetlibrary_transfer_sync_owner", "assetlibrary_transfer_sync_runtime"),
    ("OperationTrash", "operation_trash", "assetlibrary_operation_trash_owner", "assetlibrary_operation_trash_runtime"),
    ("SearchDedup", "search_dedup", "assetlibrary_search_dedup_owner", "assetlibrary_search_dedup_runtime"),
    ("PreviewProvider", "preview_provider", "assetlibrary_preview_provider_owner", "assetlibrary_preview_provider_runtime"),
    ("TaskHealth", "task_health", "assetlibrary_task_health_owner", "assetlibrary_task_health_runtime"),
    ("BackupUpdate", "backup_update", "assetlibrary_backup_update_owner", "assetlibrary_backup_update_runtime"),
)


class MigrationError(RuntimeError):
    """A deterministic migration safety or execution failure."""


@dataclass(frozen=True)
class ModuleOwnership:
    module: str
    schema: str
    owner_role: str
    runtime_role: str


@dataclass(frozen=True)
class Migration:
    version: int
    name: str
    module: str
    owner_role: str
    path: Path
    checksum: str


@dataclass(frozen=True)
class Manifest:
    path: Path
    raw_checksum: str
    postgresql_major: int
    verified_patch: str
    advisory_lock_key: int
    connect_timeout: int
    lock_timeout: int
    statement_timeout: int
    process_timeout: int
    role_path: Path
    role_checksum: str
    migration_owner: str
    migration_executor: str
    backup_reader: str
    auditor: str
    bootstrap_path: Path
    bootstrap_checksum: str
    modules: tuple[ModuleOwnership, ...]
    migrations: tuple[Migration, ...]


@dataclass(frozen=True)
class Connection:
    host: str
    port: int
    username: str
    database: str
    confirm_database: str
    postgres_bin: Path | None


@dataclass(frozen=True)
class Backup:
    backup_id: str
    archive_path: Path
    metadata_path: Path
    checksum: str


def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_dict(value: Any, label: str, errors: list[str]) -> dict[str, Any]:
    if not isinstance(value, dict):
        errors.append(f"{label} must be an object")
        return {}
    return value


def require_list(value: Any, label: str, errors: list[str]) -> list[Any]:
    if not isinstance(value, list):
        errors.append(f"{label} must be an array")
        return []
    return value


def resolved_child(base: Path, relative: Any, label: str, errors: list[str]) -> Path:
    if not isinstance(relative, str) or not relative or Path(relative).is_absolute():
        errors.append(f"{label} must be a non-empty relative path")
        return base / "__invalid__"
    candidate = (base / relative).resolve()
    try:
        candidate.relative_to(base.resolve())
    except ValueError:
        errors.append(f"{label} escapes the manifest directory")
    return candidate


def checked_text(path: Path, label: str, errors: list[str]) -> str:
    if not path.is_file():
        errors.append(f"{label} is missing: {path.name}")
        return ""
    content = path.read_bytes()
    try:
        text = content.decode("utf-8")
    except UnicodeDecodeError:
        errors.append(f"{label} must be UTF-8: {path.name}")
        return ""
    if "\r" in text:
        errors.append(f"{label} must use LF line endings: {path.name}")
    if text and not text.endswith("\n"):
        errors.append(f"{label} must end with a newline: {path.name}")
    return text


def validate_sql(text: str, label: str, errors: list[str]) -> None:
    for pattern, marker in FORBIDDEN_SQL:
        if pattern.search(text):
            errors.append(f"{label} contains forbidden {marker}")


def validate_role_sql(text: str, errors: list[str]) -> None:
    for pattern, marker in FORBIDDEN_SQL:
        if marker != "role DDL" and pattern.search(text):
            errors.append(f"role provisioning SQL contains forbidden {marker}")


def load_manifest(path: Path = DEFAULT_MANIFEST) -> Manifest:
    path = path.resolve()
    errors: list[str] = []
    if not path.is_file():
        raise MigrationError(f"manifest does not exist: {path}")
    raw = path.read_bytes()
    try:
        data = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise MigrationError(f"manifest is not valid UTF-8 JSON: {error}") from error
    data = require_dict(data, "manifest", errors)
    base = path.parent

    if data.get("format_version") != 1:
        errors.append("format_version must equal 1")
    postgresql = require_dict(data.get("postgresql"), "postgresql", errors)
    major = postgresql.get("major")
    verified_patch = postgresql.get("verified_patch")
    if major != 16:
        errors.append("postgresql.major must equal the frozen major 16")
    if verified_patch != "16.15":
        errors.append("postgresql.verified_patch must equal 16.15")

    lock_key = data.get("advisory_lock_key")
    if not isinstance(lock_key, int) or not -(2**63) <= lock_key < 2**63:
        errors.append("advisory_lock_key must be a signed 64-bit integer")
        lock_key = 0

    timeouts = require_dict(data.get("timeouts_seconds"), "timeouts_seconds", errors)
    timeout_values: dict[str, int] = {}
    for key, minimum, maximum in (
        ("connect", 1, 30),
        ("lock", 1, 60),
        ("statement", 1, 600),
        ("process", 1, 900),
    ):
        value = timeouts.get(key)
        if not isinstance(value, int) or not minimum <= value <= maximum:
            errors.append(f"timeouts_seconds.{key} must be between {minimum} and {maximum}")
            value = maximum
        timeout_values[key] = value

    roles = require_dict(data.get("role_provisioning"), "role_provisioning", errors)
    role_path = resolved_child(base, roles.get("path"), "role_provisioning.path", errors)
    role_checksum = roles.get("sha256")
    migration_owner = roles.get("migration_owner")
    migration_executor = roles.get("migration_executor")
    backup_reader = roles.get("backup_reader")
    auditor = roles.get("auditor")
    for label, role in (
        ("migration_owner", migration_owner),
        ("migration_executor", migration_executor),
        ("backup_reader", backup_reader),
        ("auditor", auditor),
    ):
        if not isinstance(role, str) or not ROLE_RE.fullmatch(role):
            errors.append(f"role_provisioning.{label} is invalid")

    role_text = checked_text(role_path, "role provisioning SQL", errors)
    validate_role_sql(role_text, errors)
    if not isinstance(role_checksum, str) or not SHA256_RE.fullmatch(role_checksum):
        errors.append("role_provisioning.sha256 must be lowercase SHA-256")
    elif role_path.is_file() and sha256_path(role_path) != role_checksum:
        errors.append("role provisioning checksum drift")
    if re.search(r"(?<!NO)\bLOGIN\b", role_text, re.IGNORECASE):
        errors.append("role provisioning must not create or enable LOGIN roles")
    if re.search(r"\bPASSWORD\b", role_text, re.IGNORECASE):
        errors.append("role provisioning must not contain passwords")

    bootstrap = require_dict(data.get("bootstrap"), "bootstrap", errors)
    bootstrap_path = resolved_child(base, bootstrap.get("path"), "bootstrap.path", errors)
    bootstrap_checksum = bootstrap.get("sha256")
    bootstrap_text = checked_text(bootstrap_path, "bootstrap SQL", errors)
    validate_sql(bootstrap_text, "bootstrap SQL", errors)
    if not isinstance(bootstrap_checksum, str) or not SHA256_RE.fullmatch(
        bootstrap_checksum
    ):
        errors.append("bootstrap.sha256 must be lowercase SHA-256")
    elif bootstrap_path.is_file() and sha256_path(bootstrap_path) != bootstrap_checksum:
        errors.append("bootstrap checksum drift")
    for marker in ("migration.bootstrap_state", "migration.ledger"):
        if marker not in bootstrap_text:
            errors.append(f"bootstrap SQL does not define {marker}")

    modules: list[ModuleOwnership] = []
    for index, raw_module in enumerate(require_list(data.get("modules"), "modules", errors)):
        item = require_dict(raw_module, f"modules[{index}]", errors)
        values = tuple(item.get(key) for key in ("module", "schema", "owner_role", "runtime_role"))
        if not all(isinstance(value, str) for value in values):
            errors.append(f"modules[{index}] fields must be strings")
            continue
        module, schema, owner_role, runtime_role = values
        if not NAME_RE.fullmatch(schema):
            errors.append(f"modules[{index}].schema is invalid")
        if not ROLE_RE.fullmatch(owner_role) or not owner_role.endswith("_owner"):
            errors.append(f"modules[{index}].owner_role is invalid")
        if not ROLE_RE.fullmatch(runtime_role) or not runtime_role.endswith("_runtime"):
            errors.append(f"modules[{index}].runtime_role is invalid")
        modules.append(ModuleOwnership(module, schema, owner_role, runtime_role))

    actual_modules = tuple(
        (item.module, item.schema, item.owner_role, item.runtime_role) for item in modules
    )
    if actual_modules != EXPECTED_MODULES:
        errors.append("modules must exactly match the 11 frozen ownership mappings in order")
    if len({item.schema for item in modules}) != len(modules):
        errors.append("module schema names must be unique")
    if len({item.owner_role for item in modules}) != len(modules):
        errors.append("module owner roles must be unique")
    if len({item.runtime_role for item in modules}) != len(modules):
        errors.append("module runtime roles must be unique")
    expected_roles = {
        role
        for item in modules
        for role in (item.owner_role, item.runtime_role)
    }
    expected_roles.update(
        role for role in (migration_owner, migration_executor, backup_reader, auditor)
        if isinstance(role, str)
    )
    for role in sorted(expected_roles):
        if f"'{role}'" not in role_text and role not in role_text:
            errors.append(f"role provisioning does not mention {role}")

    module_owner = {item.module: item.owner_role for item in modules}
    migrations: list[Migration] = []
    raw_migrations = require_list(data.get("migrations"), "migrations", errors)
    for index, raw_migration in enumerate(raw_migrations):
        item = require_dict(raw_migration, f"migrations[{index}]", errors)
        version = item.get("version")
        name = item.get("name")
        module = item.get("module")
        owner_role = item.get("owner_role")
        checksum = item.get("sha256")
        migration_path = resolved_child(base, item.get("path"), f"migrations[{index}].path", errors)
        if not isinstance(version, int) or version <= 0:
            errors.append(f"migrations[{index}].version must be a positive integer")
            version = index + 1
        if not isinstance(name, str) or not NAME_RE.fullmatch(name):
            errors.append(f"migrations[{index}].name is invalid")
            name = "invalid"
        if migration_path.name != f"{version:04d}_{name}.sql":
            errors.append(f"migrations[{index}].path must match its version and name")
        expected_owner = migration_owner if module == FOUNDATION_MODULE else module_owner.get(module)
        if expected_owner is None:
            errors.append(f"migrations[{index}].module is not owned")
        elif owner_role != expected_owner:
            errors.append(f"migrations[{index}].owner_role does not match its module")
        if not isinstance(checksum, str) or not SHA256_RE.fullmatch(checksum):
            errors.append(f"migrations[{index}].sha256 must be lowercase SHA-256")
            checksum = "0" * 64
        sql_text = checked_text(migration_path, f"migration {version} SQL", errors)
        validate_sql(sql_text, f"migration {version} SQL", errors)
        if migration_path.is_file() and sha256_path(migration_path) != checksum:
            errors.append(f"migration {version} checksum drift")
        migrations.append(Migration(version, name, module, owner_role, migration_path, checksum))

    if [item.version for item in migrations] != list(range(1, len(migrations) + 1)):
        errors.append("migration versions must be contiguous and ordered from 1")
    listed_sql = {role_path.resolve(), bootstrap_path.resolve()}
    listed_sql.update(item.path.resolve() for item in migrations)
    actual_sql = {item.resolve() for item in base.glob("*.sql")}
    extra = sorted(item.name for item in actual_sql - listed_sql)
    missing = sorted(item.name for item in listed_sql - actual_sql)
    if extra:
        errors.append(f"unlisted production SQL files: {', '.join(extra)}")
    if missing:
        errors.append(f"listed production SQL files are missing: {', '.join(missing)}")

    if errors:
        raise MigrationError("manifest validation failed:\n- " + "\n- ".join(errors))
    return Manifest(
        path=path,
        raw_checksum=sha256_bytes(raw),
        postgresql_major=major,
        verified_patch=verified_patch,
        advisory_lock_key=lock_key,
        connect_timeout=timeout_values["connect"],
        lock_timeout=timeout_values["lock"],
        statement_timeout=timeout_values["statement"],
        process_timeout=timeout_values["process"],
        role_path=role_path,
        role_checksum=role_checksum,
        migration_owner=migration_owner,
        migration_executor=migration_executor,
        backup_reader=backup_reader,
        auditor=auditor,
        bootstrap_path=bootstrap_path,
        bootstrap_checksum=bootstrap_checksum,
        modules=tuple(modules),
        migrations=tuple(migrations),
    )


class PostgresTools:
    def __init__(self, manifest: Manifest, connection: Connection):
        self.manifest = manifest
        self.connection = connection
        if connection.database != connection.confirm_database:
            raise MigrationError("--confirm-database must exactly match --database")
        if not connection.host or not connection.username or not connection.database:
            raise MigrationError("host, username, and database must be non-empty")
        if not 1 <= connection.port <= 65535:
            raise MigrationError("port must be between 1 and 65535")
        self.executables = {"psql": self._resolve_executable("psql")}
        self.environment = os.environ.copy()
        self.environment["PGCONNECT_TIMEOUT"] = str(manifest.connect_timeout)
        self.environment["PGAPPNAME"] = "assetlibrary-database-migration"

    def _resolve_executable(self, name: str) -> str:
        suffix = ".exe" if os.name == "nt" else ""
        if self.connection.postgres_bin is not None:
            candidate = self.connection.postgres_bin.resolve() / f"{name}{suffix}"
            if not candidate.is_file():
                raise MigrationError(f"PostgreSQL tool is missing: {candidate}")
            return str(candidate)
        found = shutil.which(name)
        if found is None:
            raise MigrationError(f"PostgreSQL tool is not on PATH: {name}")
        return found

    def executable(self, name: str) -> str:
        if name not in self.executables:
            self.executables[name] = self._resolve_executable(name)
        return self.executables[name]

    def _redact(self, value: str) -> str:
        password = self.environment.get("PGPASSWORD")
        return value.replace(password, "<redacted>") if password else value

    def run(
        self,
        command: Sequence[str],
        *,
        input_text: str | None = None,
        timeout: int | None = None,
    ) -> subprocess.CompletedProcess[str]:
        try:
            result = subprocess.run(
                list(command),
                input=input_text,
                text=True,
                encoding="utf-8",
                errors="replace",
                capture_output=True,
                env=self.environment,
                timeout=timeout or self.manifest.process_timeout,
                check=False,
            )
        except subprocess.TimeoutExpired as error:
            raise MigrationError(f"PostgreSQL command timed out after {error.timeout} seconds") from error
        except OSError as error:
            raise MigrationError(f"could not start PostgreSQL command: {error}") from error
        if result.returncode != 0:
            detail = self._redact((result.stderr or result.stdout).strip())
            raise MigrationError(f"PostgreSQL command failed with exit code {result.returncode}: {detail}")
        return result

    def connection_args(self) -> list[str]:
        return [
            "--host", self.connection.host,
            "--port", str(self.connection.port),
            "--username", self.connection.username,
            "--dbname", self.connection.database,
            "--no-password",
        ]

    def psql(self, sql: str, *, quiet: bool = True) -> str:
        command = [
            self.executables["psql"],
            "--no-psqlrc",
            "--set", "ON_ERROR_STOP=1",
            "--no-align",
            "--tuples-only",
        ]
        if quiet:
            command.append("--quiet")
        command.extend(self.connection_args())
        return self.run(command, input_text=sql).stdout

    def verify_versions(self, *, require_backup_tools: bool) -> tuple[str, int]:
        names = ("psql", "pg_dump", "pg_restore") if require_backup_tools else ("psql",)
        versions: dict[str, str] = {}
        for name in names:
            output = self.run([self.executable(name), "--version"]).stdout.strip()
            match = VERSION_OUTPUT_RE.search(output)
            if match is None or int(match.group(1)) != self.manifest.postgresql_major:
                raise MigrationError(
                    f"{name} major version must be {self.manifest.postgresql_major}: {output}"
                )
            versions[name] = ".".join(part for part in match.groups() if part is not None)
        server = self.psql(
            "SELECT current_setting('server_version_num'), current_database();\n"
        ).strip().split("|")
        if len(server) != 2 or not server[0].isdigit():
            raise MigrationError("could not read PostgreSQL server identity")
        server_version_num = int(server[0])
        if server[1] != self.connection.database:
            raise MigrationError("connected database does not match --database")
        if server_version_num // 10000 != self.manifest.postgresql_major:
            raise MigrationError(
                f"server major version must be {self.manifest.postgresql_major}: {server_version_num}"
            )
        return versions["psql"], server_version_num


def sql_literal(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def sql_identifier(value: str) -> str:
    if not ROLE_RE.fullmatch(value):
        raise MigrationError(f"unsafe SQL identifier: {value}")
    return '"' + value.replace('"', '""') + '"'


def ledger_rows(tools: PostgresTools, manifest: Manifest) -> tuple[str | None, list[dict[str, Any]]]:
    existence = tools.psql(
        f"SET ROLE {sql_identifier(manifest.migration_owner)};\n"
        "SELECT to_regclass('migration.bootstrap_state') IS NOT NULL, "
        "to_regclass('migration.ledger') IS NOT NULL;\n"
    ).strip()
    if existence == "f|f":
        return None, []
    if existence != "t|t":
        raise MigrationError("migration control objects are partial or inconsistent")
    bootstrap = tools.psql(
        f"SET ROLE {sql_identifier(manifest.migration_owner)};\n"
        "SELECT checksum FROM migration.bootstrap_state WHERE singleton;\n"
    ).strip()
    if bootstrap != manifest.bootstrap_checksum:
        raise MigrationError("installed bootstrap checksum differs from the manifest")
    output = tools.psql(
        f"SET ROLE {sql_identifier(manifest.migration_owner)};\n"
        "SELECT version, name, module, owner_role, checksum, manifest_checksum, "
        "backup_id, backup_sha256, server_version_num, duration_ms, "
        "to_char(applied_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"') "
        "FROM migration.ledger ORDER BY version;\n"
    )
    rows: list[dict[str, Any]] = []
    for row in csv.reader(output.splitlines(), delimiter="|"):
        if not row:
            continue
        if len(row) != 11:
            raise MigrationError("migration ledger returned an unexpected row shape")
        rows.append(
            {
                "version": int(row[0]),
                "name": row[1],
                "module": row[2],
                "owner_role": row[3],
                "checksum": row[4],
                "manifest_checksum": row[5],
                "backup_id": row[6],
                "backup_sha256": row[7],
                "server_version_num": int(row[8]),
                "duration_ms": int(row[9]),
                "applied_at": row[10],
            }
        )
    return bootstrap, rows


def validate_ledger(manifest: Manifest, rows: Sequence[dict[str, Any]]) -> tuple[Migration, ...]:
    if len(rows) > len(manifest.migrations):
        raise MigrationError("ledger contains versions not present in the manifest")
    for index, row in enumerate(rows):
        migration = manifest.migrations[index]
        expected = {
            "version": migration.version,
            "name": migration.name,
            "module": migration.module,
            "owner_role": migration.owner_role,
            "checksum": migration.checksum,
        }
        differences = [key for key, value in expected.items() if row.get(key) != value]
        if differences:
            raise MigrationError(
                f"ledger drift at version {migration.version}: {', '.join(differences)}"
            )
    return manifest.migrations[len(rows):]


def require_executor_membership(tools: PostgresTools, manifest: Manifest) -> None:
    output = tools.psql(
        "SELECT rolsuper, "
        f"pg_has_role(current_user, {sql_literal(manifest.migration_executor)}, 'MEMBER') "
        "FROM pg_roles WHERE rolname = current_user;\n"
    ).strip()
    if output not in {"t|t", "t|f", "f|t"}:
        raise MigrationError("connection role is not a member of assetlibrary_migration_executor")


def expected_role_memberships(manifest: Manifest) -> set[tuple[str, str, str, str, str]]:
    memberships = {
        (manifest.migration_owner, manifest.migration_executor, "f", "t", "f"),
        (manifest.backup_reader, manifest.migration_executor, "f", "t", "f"),
    }
    memberships.update(
        (item.owner_role, manifest.migration_owner, "t", "t", "f")
        for item in manifest.modules
    )
    return memberships


def provision_roles(tools: PostgresTools, manifest: Manifest, explicit: bool) -> None:
    if not explicit:
        raise MigrationError("role provisioning requires --allow-cluster-role-changes")
    tools.verify_versions(require_backup_tools=False)
    role_sql = manifest.role_path.read_text(encoding="utf-8")
    script = (
        "BEGIN;\n"
        f"SELECT pg_advisory_xact_lock({manifest.advisory_lock_key + 1});\n"
        f"SET LOCAL lock_timeout = '{manifest.lock_timeout}s';\n"
        f"SET LOCAL statement_timeout = '{manifest.statement_timeout}s';\n"
        f"{role_sql}\n"
        "COMMIT;\n"
    )
    tools.psql(script, quiet=False)
    expected = 4 + len(manifest.modules) * 2
    role_names = [
        manifest.migration_owner,
        manifest.migration_executor,
        manifest.backup_reader,
        manifest.auditor,
        *(role for item in manifest.modules for role in (item.owner_role, item.runtime_role)),
    ]
    values = ", ".join(sql_literal(role) for role in role_names)
    result = tools.psql(
        "SELECT count(*), bool_and(NOT rolcanlogin AND NOT rolinherit AND NOT rolsuper "
        "AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls) "
        f"FROM pg_roles WHERE rolname IN ({values});\n"
    ).strip()
    if result != f"{expected}|t":
        raise MigrationError(f"role provisioning verification failed: {result}")
    membership_output = tools.psql(
        "SELECT granted.rolname, member.rolname, membership.inherit_option, "
        "membership.set_option, membership.admin_option "
        "FROM pg_auth_members AS membership "
        "JOIN pg_roles AS granted ON granted.oid = membership.roleid "
        "JOIN pg_roles AS member ON member.oid = membership.member "
        f"WHERE member.rolname IN ({values}) "
        "ORDER BY granted.rolname, member.rolname;\n"
    )
    actual_memberships: set[tuple[str, str, str, str, str]] = set()
    for row in csv.reader(membership_output.splitlines(), delimiter="|"):
        if len(row) != 5:
            raise MigrationError("role membership verification returned an unexpected row shape")
        actual_memberships.add((row[0], row[1], row[2], row[3], row[4]))
    if actual_memberships != expected_role_memberships(manifest):
        raise MigrationError("fixed AssetLibrary roles have unexpected role memberships")
    print(f"ROLE_PROVISIONING_OK roles={expected} database={tools.connection.database}")


def safe_backup_directory(path: Path) -> Path:
    try:
        resolved = path.resolve()
        if resolved == Path(resolved.anchor):
            raise MigrationError("backup directory must not be a filesystem root")
        resolved.mkdir(parents=True, exist_ok=True)
        if not resolved.is_dir():
            raise MigrationError("backup directory is not a directory")
        return resolved
    except OSError as error:
        raise MigrationError(f"backup directory is unavailable: {error}") from error


def create_backup(
    tools: PostgresTools,
    manifest: Manifest,
    backup_dir: Path,
    server_version_num: int,
    rows: Sequence[dict[str, Any]],
) -> Backup:
    directory = safe_backup_directory(backup_dir)
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    database_label = re.sub(r"[^A-Za-z0-9_.-]", "_", tools.connection.database)[:64]
    backup_id = f"assetlibrary-{database_label}-{timestamp}-{uuid.uuid4().hex[:8]}"
    published_directory = directory / backup_id
    partial_directory = directory / f".{backup_id}.partial"
    archive = published_directory / "archive.dump"
    metadata = published_directory / "metadata.json"
    partial_archive = partial_directory / archive.name
    partial_metadata = partial_directory / metadata.name
    try:
        partial_directory.mkdir()
        command = [
            tools.executable("pg_dump"),
            *tools.connection_args(),
            "--role", manifest.backup_reader,
            "--format", "custom",
            "--compress", "gzip:6",
            "--file", str(partial_archive),
        ]
        tools.run(command)
        if not partial_archive.is_file() or partial_archive.stat().st_size == 0:
            raise MigrationError("pg_dump did not produce a non-empty archive")
        tools.run([tools.executable("pg_restore"), "--list", str(partial_archive)])
        checksum = sha256_path(partial_archive)
        payload = {
            "format_version": 1,
            "backup_id": backup_id,
            "archive_file": archive.name,
            "archive_sha256": checksum,
            "created_at": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "source_database": tools.connection.database,
            "server_version_num": server_version_num,
            "postgresql_major": manifest.postgresql_major,
            "migration_manifest_sha256": manifest.raw_checksum,
            "role_provisioning_sha256": manifest.role_checksum,
            "ledger": list(rows),
        }
        partial_metadata.write_text(
            json.dumps(payload, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        os.replace(partial_directory, published_directory)
    except OSError as error:
        raise MigrationError(f"could not publish verified backup: {error}") from error
    finally:
        shutil.rmtree(partial_directory, ignore_errors=True)
    print(f"BACKUP_VERIFIED id={backup_id} sha256={checksum}")
    return Backup(backup_id, archive, metadata, checksum)


def expected_ledger_values(manifest: Manifest) -> str:
    values = []
    for item in manifest.migrations:
        values.append(
            "(" + ", ".join(
                (
                    str(item.version),
                    sql_literal(item.name),
                    sql_literal(item.module),
                    sql_literal(item.owner_role),
                    sql_literal(item.checksum),
                )
            ) + ")"
        )
    return ",\n        ".join(values)


def migration_script(
    manifest: Manifest,
    migration: Migration,
    backup: Backup,
    server_version_num: int,
) -> str:
    bootstrap = manifest.bootstrap_path.read_text(encoding="utf-8")
    body = migration.path.read_text(encoding="utf-8")
    expected = expected_ledger_values(manifest)
    return rf"""\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '{manifest.lock_timeout}s';
SET LOCAL statement_timeout = '{manifest.statement_timeout}s';
SELECT pg_advisory_xact_lock({manifest.advisory_lock_key});
SET LOCAL ROLE {sql_identifier(manifest.migration_owner)};
SELECT to_regclass('migration.bootstrap_state') IS NULL AS bootstrap_needed \gset
\if :bootstrap_needed
{bootstrap}
INSERT INTO migration.bootstrap_state (format_version, checksum)
VALUES (1, {sql_literal(manifest.bootstrap_checksum)});
\else
DO $assetlibrary_bootstrap_check$
BEGIN
    IF to_regclass('migration.ledger') IS NULL THEN
        RAISE EXCEPTION 'migration control objects are partial';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM migration.bootstrap_state
        WHERE singleton AND format_version = 1
          AND checksum = {sql_literal(manifest.bootstrap_checksum)}
    ) THEN
        RAISE EXCEPTION 'bootstrap checksum drift';
    END IF;
END
$assetlibrary_bootstrap_check$;
\endif
DO $assetlibrary_ledger_check$
BEGIN
    IF EXISTS (
        WITH expected(version, name, module, owner_role, checksum) AS (
            VALUES
        {expected}
        )
        SELECT 1
        FROM migration.ledger AS actual
        LEFT JOIN expected USING (version)
        WHERE expected.version IS NULL
           OR actual.name <> expected.name
           OR actual.module <> expected.module
           OR actual.owner_role <> expected.owner_role
           OR actual.checksum <> expected.checksum
    ) THEN
        RAISE EXCEPTION 'migration ledger contains unknown or drifted rows';
    END IF;
    IF EXISTS (
        WITH expected(version) AS (VALUES
            {', '.join(f'({item.version})' for item in manifest.migrations)}
        )
        SELECT 1 FROM expected
        WHERE version < {migration.version}
          AND NOT EXISTS (
              SELECT 1 FROM migration.ledger WHERE ledger.version = expected.version
          )
    ) THEN
        RAISE EXCEPTION 'migration ledger has a missing prefix';
    END IF;
END
$assetlibrary_ledger_check$;
SELECT EXISTS (
    SELECT 1 FROM migration.ledger WHERE version = {migration.version}
) AS migration_already_applied \gset
\if :migration_already_applied
\echo MIGRATION_ALREADY_APPLIED version={migration.version}
\else
SELECT clock_timestamp() AS migration_started_at \gset
SET LOCAL ROLE {sql_identifier(migration.owner_role)};
{body}
SET LOCAL ROLE {sql_identifier(manifest.migration_owner)};
INSERT INTO migration.ledger (
    version, name, module, owner_role, checksum, manifest_checksum,
    backup_id, backup_sha256, server_version_num, duration_ms
)
VALUES (
    {migration.version},
    {sql_literal(migration.name)},
    {sql_literal(migration.module)},
    {sql_literal(migration.owner_role)},
    {sql_literal(migration.checksum)},
    {sql_literal(manifest.raw_checksum)},
    {sql_literal(backup.backup_id)},
    {sql_literal(backup.checksum)},
    {server_version_num},
    GREATEST(
        0,
        (EXTRACT(EPOCH FROM (clock_timestamp() - :'migration_started_at'::timestamptz)) * 1000)::bigint
    )
);
\echo MIGRATION_APPLIED version={migration.version}
\endif
COMMIT;
"""


def apply_migrations(tools: PostgresTools, manifest: Manifest, backup_dir: Path) -> Backup | None:
    _, server_version_num = tools.verify_versions(require_backup_tools=True)
    require_executor_membership(tools, manifest)
    _, rows = ledger_rows(tools, manifest)
    pending = validate_ledger(manifest, rows)
    if not pending:
        print(f"NO_PENDING_MIGRATIONS count={len(rows)}")
        return None
    backup = create_backup(tools, manifest, backup_dir, server_version_num, rows)
    for migration in pending:
        output = tools.psql(
            migration_script(manifest, migration, backup, server_version_num),
            quiet=False,
        )
        markers = [line for line in output.splitlines() if line.startswith("MIGRATION_")]
        if not markers:
            raise MigrationError(f"migration {migration.version} did not emit a completion marker")
        print(markers[-1])
    _, final_rows = ledger_rows(tools, manifest)
    remaining = validate_ledger(manifest, final_rows)
    if remaining:
        raise MigrationError("migration run completed with pending versions")
    print(f"MIGRATIONS_CURRENT count={len(final_rows)}")
    return backup


def status(tools: PostgresTools, manifest: Manifest) -> None:
    tools.verify_versions(require_backup_tools=False)
    require_executor_membership(tools, manifest)
    _, rows = ledger_rows(tools, manifest)
    pending = validate_ledger(manifest, rows)
    print(
        f"MIGRATION_STATUS applied={len(rows)} pending={len(pending)} "
        f"database={tools.connection.database}"
    )


def load_backup_metadata(path: Path) -> tuple[dict[str, Any], Path]:
    try:
        metadata = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise MigrationError(f"backup metadata is invalid: {error}") from error
    if not isinstance(metadata, dict) or metadata.get("format_version") != 1:
        raise MigrationError("backup metadata format_version must equal 1")
    archive_name = metadata.get("archive_file")
    checksum = metadata.get("archive_sha256")
    if not isinstance(archive_name, str) or Path(archive_name).name != archive_name:
        raise MigrationError("backup archive_file must be a local filename")
    if not isinstance(checksum, str) or not SHA256_RE.fullmatch(checksum):
        raise MigrationError("backup archive_sha256 is invalid")
    metadata_directory = path.resolve().parent
    archive = metadata_directory / archive_name
    if not archive.is_file():
        raise MigrationError("backup archive is missing or its SHA-256 does not match metadata")
    resolved_archive = archive.resolve()
    if resolved_archive.parent != metadata_directory:
        raise MigrationError("backup archive must not escape the metadata directory")
    if sha256_path(resolved_archive) != checksum:
        raise MigrationError("backup archive is missing or its SHA-256 does not match metadata")
    return metadata, resolved_archive


def validate_backup_contract(metadata: dict[str, Any], manifest: Manifest) -> list[dict[str, Any]]:
    if not isinstance(metadata.get("backup_id"), str) or not metadata["backup_id"]:
        raise MigrationError("backup metadata backup_id is invalid")
    if not isinstance(metadata.get("source_database"), str) or not metadata["source_database"]:
        raise MigrationError("backup metadata source_database is invalid")
    if metadata.get("postgresql_major") != manifest.postgresql_major:
        raise MigrationError("backup PostgreSQL major differs from the manifest")
    server_version_num = metadata.get("server_version_num")
    if (
        not isinstance(server_version_num, int)
        or server_version_num // 10000 != manifest.postgresql_major
    ):
        raise MigrationError("backup server version is invalid")
    expected_hashes = {
        "migration_manifest_sha256": manifest.raw_checksum,
        "role_provisioning_sha256": manifest.role_checksum,
    }
    for field_name, expected_hash in expected_hashes.items():
        value = metadata.get(field_name)
        if not isinstance(value, str) or not SHA256_RE.fullmatch(value):
            raise MigrationError(f"backup metadata {field_name} is invalid")
        if value != expected_hash:
            raise MigrationError(f"backup metadata {field_name} differs from the manifest")
    ledger = metadata.get("ledger")
    if not isinstance(ledger, list) or any(not isinstance(row, dict) for row in ledger):
        raise MigrationError("backup metadata ledger is invalid")
    validate_ledger(manifest, ledger)
    return ledger


def ensure_empty_restore_target(tools: PostgresTools) -> None:
    result = tools.psql(
        "SELECT "
        "(SELECT count(*) FROM pg_namespace WHERE nspname <> 'public' "
        "AND nspname !~ '^pg_' AND nspname <> 'information_schema') + "
        "(SELECT count(*) FROM pg_class AS class JOIN pg_namespace AS namespace "
        "ON namespace.oid = class.relnamespace WHERE namespace.nspname = 'public' "
        "AND class.relkind IN ('r','p','v','m','S','f')) + "
        "(SELECT count(*) FROM pg_proc AS routine JOIN pg_namespace AS namespace "
        "ON namespace.oid = routine.pronamespace WHERE namespace.nspname = 'public') + "
        "(SELECT count(*) FROM pg_type AS type JOIN pg_namespace AS namespace "
        "ON namespace.oid = type.typnamespace WHERE namespace.nspname = 'public' "
        "AND type.typisdefined) + "
        "(SELECT count(*) FROM pg_extension WHERE extname <> 'plpgsql');\n"
    ).strip()
    if result != "0":
        raise MigrationError("restore target is not empty; no objects were changed")


def restore_backup(tools: PostgresTools, manifest: Manifest, metadata_path: Path) -> None:
    tools.verify_versions(require_backup_tools=True)
    require_executor_membership(tools, manifest)
    metadata, archive = load_backup_metadata(metadata_path.resolve())
    expected_ledger = validate_backup_contract(metadata, manifest)
    ensure_empty_restore_target(tools)
    tools.run([tools.executable("pg_restore"), "--list", str(archive)])
    command = [
        tools.executable("pg_restore"),
        *tools.connection_args(),
        "--role", manifest.migration_owner,
        "--single-transaction",
        "--exit-on-error",
        str(archive),
    ]
    tools.run(command)
    _, restored_ledger = ledger_rows(tools, manifest)
    if restored_ledger != expected_ledger:
        raise MigrationError("restored migration ledger differs from verified backup metadata")
    print(
        f"RESTORE_COMPLETE backup_id={metadata.get('backup_id')} "
        f"database={tools.connection.database}"
    )


def add_connection_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--host", required=True)
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--username", required=True)
    parser.add_argument("--database", required=True)
    parser.add_argument("--confirm-database", required=True)
    parser.add_argument("--postgres-bin", type=Path)


def parse_connection(args: argparse.Namespace) -> Connection:
    return Connection(
        host=args.host,
        port=args.port,
        username=args.username,
        database=args.database,
        confirm_database=args.confirm_database,
        postgres_bin=args.postgres_bin,
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("validate", help="validate files without connecting")

    provision = commands.add_parser("provision-roles", help="provision fixed NOLOGIN roles")
    add_connection_arguments(provision)
    provision.add_argument("--allow-cluster-role-changes", action="store_true")

    show_status = commands.add_parser("status", help="show applied and pending versions")
    add_connection_arguments(show_status)

    apply = commands.add_parser("apply", help="back up and apply pending migrations")
    add_connection_arguments(apply)
    apply.add_argument("--backup-dir", type=Path, required=True)

    restore = commands.add_parser("restore", help="restore a verified archive to an empty database")
    add_connection_arguments(restore)
    restore.add_argument("--metadata", type=Path, required=True)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        manifest = load_manifest(args.manifest)
        if args.command == "validate":
            print(
                f"MIGRATION_MANIFEST_OK migrations={len(manifest.migrations)} "
                f"modules={len(manifest.modules)} postgresql={manifest.verified_patch}"
            )
            return 0
        tools = PostgresTools(manifest, parse_connection(args))
        if args.command == "provision-roles":
            provision_roles(tools, manifest, args.allow_cluster_role_changes)
        elif args.command == "status":
            status(tools, manifest)
        elif args.command == "apply":
            apply_migrations(tools, manifest, args.backup_dir)
        elif args.command == "restore":
            restore_backup(tools, manifest, args.metadata)
        return 0
    except MigrationError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
