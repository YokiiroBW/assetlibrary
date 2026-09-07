#!/usr/bin/env python3
"""Private local PostgreSQL lifecycle for trial.ps1; never targets an external cluster."""
from __future__ import annotations

import argparse
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import secrets
import stat
import subprocess
import sys


class TrialDatabaseError(RuntimeError):
    pass


def safe_path(path: Path) -> None:
    if not path.is_absolute() or str(path).startswith("\\\\"):
        raise TrialDatabaseError("database_path_invalid")
    for candidate in (path, *path.parents):
        try:
            metadata = candidate.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(metadata.st_mode) or getattr(metadata, "st_file_attributes", 0) & 0x400:
            raise TrialDatabaseError("database_reparse_path_rejected")


def read_json(path: Path) -> dict:
    safe_path(path)
    if path.stat().st_size > 65536:
        raise TrialDatabaseError("database_state_invalid")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, payload: dict) -> None:
    safe_path(path)
    temporary = path.with_name(path.name + ".pending")
    with temporary.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(payload, stream, sort_keys=True)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


PROCESS_SPEC = importlib.util.spec_from_file_location("trial_process_identity", Path(__file__).resolve().parent / "trial_process.py")
assert PROCESS_SPEC and PROCESS_SPEC.loader
PROCESS = importlib.util.module_from_spec(PROCESS_SPEC)
PROCESS_SPEC.loader.exec_module(PROCESS)
process_identity = PROCESS.process_identity

class TrialDatabase:
    def __init__(self, state: Path, package: Path):
        self.stage = "configuration"
        self.state = state.absolute()
        safe_path(self.state)
        self.owner = read_json(self.state / "trial-owner.json")
        if self.owner.get("product") != "AssetLibrary/read-only-trial" or self.owner.get("state_path") != str(self.state):
            raise TrialDatabaseError("database_state_owner_mismatch")
        self.package = package.absolute()
        self.bin = Path(self.owner["postgres_bin"])
        self.data = self.state / "pgdata"
        safe_path(self.bin)
        safe_path(self.data)
        self.port = self.owner["database_port"]
        if type(self.port) is not int or not 1024 <= self.port <= 65535:
            raise TrialDatabaseError("database_port_invalid")
        self.environment = {key: value for key, value in os.environ.items() if not key.upper().startswith("PG")}
        self.environment.update({"PGCONNECT_TIMEOUT": "5", "PGAPPNAME": "AssetLibrary.Trial.LocalSetup"})
        self.passwords_path = self.state / "secrets/database-passwords.json"
        self.process_path = self.state / "postgres-process.json"
        self.cluster_path = self.state / "postgres-cluster.json"
        spec = importlib.util.spec_from_file_location("trial_migrations", self.package / "migrations/migration_tool.py")
        assert spec and spec.loader
        self.migrations = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = self.migrations
        spec.loader.exec_module(self.migrations)
        self.manifest = self.migrations.load_manifest(self.package / "migrations/manifest.json")

    def executable(self, name: str) -> Path:
        path = self.bin / f"{name}.exe"
        safe_path(path)
        if not path.is_file():
            raise TrialDatabaseError("database_tool_missing")
        return path

    def run(self, name: str, arguments: list[str], *, input_text: str | None = None, timeout: int = 60) -> str:
        output_options = {"stdout": subprocess.DEVNULL, "stderr": subprocess.DEVNULL} if name == "pg_ctl" else {"capture_output": True}
        result = subprocess.run(
            [str(self.executable(name)), *arguments], input=input_text,
            **output_options, text=True, encoding="utf-8", errors="replace",
            timeout=timeout, env=self.environment, creationflags=subprocess.CREATE_NO_WINDOW,
        )
        if result.returncode != 0:
            # PostgreSQL errors can contain SQL literals; never echo its captured output.
            raise TrialDatabaseError(f"database_{name}_failed")
        return result.stdout or ""

    def control_identity(self) -> str:
        # pg_controldata field labels are fixed to C locale to keep the ownership check unambiguous.
        old = self.environment.get("LC_ALL")
        self.environment["LC_ALL"] = "C"
        try:
            output = self.run("pg_controldata", ["-D", str(self.data)])
        finally:
            if old is None:
                self.environment.pop("LC_ALL", None)
            else:
                self.environment["LC_ALL"] = old
        values = [line.split(":", 1)[1].strip() for line in output.splitlines() if line.startswith("Database system identifier:")]
        if len(values) != 1 or not values[0].isdigit():
            raise TrialDatabaseError("database_cluster_identity_unavailable")
        return values[0]

    def verify_cluster(self) -> None:
        record = read_json(self.cluster_path)
        if record != {"deployment_id": self.owner["deployment_id"], "system_identifier": self.control_identity()}:
            raise TrialDatabaseError("database_cluster_owner_mismatch")

    def actual_process(self) -> dict | None:
        pid_file = self.data / "postmaster.pid"
        safe_path(pid_file)
        if not pid_file.exists():
            return None
        lines = pid_file.read_text(encoding="utf-8").splitlines()
        if len(lines) < 4 or not lines[0].isdigit() or not lines[2].isdigit():
            raise TrialDatabaseError("database_pid_invalid")
        actual = process_identity(int(lines[0]))
        if actual is None:
            return None
        started = (actual["created"] / 10_000_000) - 11_644_473_600
        if (Path(actual["image"]) != self.executable("postgres") or Path(lines[1]) != self.data
                or abs(started - int(lines[2])) > 3 or lines[3] != str(self.port)):
            raise TrialDatabaseError("database_process_owner_mismatch")
        return actual

    def start(self) -> bool:
        self.verify_cluster()
        process = self.actual_process()
        if process is not None:
            if not self.process_path.is_file() or read_json(self.process_path) != process:
                raise TrialDatabaseError("database_process_record_mismatch")
            return False
        try:
            self.run("pg_ctl", ["-D", str(self.data), "-l", str(self.state / "logs/postgres.log"), "-w", "-t", "30", "start"], timeout=40)
            process = self.actual_process()
            if process is None:
                raise TrialDatabaseError("database_start_identity_missing")
            write_json(self.process_path, process)
        except Exception:
            # Startup belongs to this operation. If recording its identity fails (for example disk full),
            # validate the just-started cluster process before stopping it rather than leaving it untracked.
            if self.actual_process() is not None:
                self.run("pg_ctl", ["-D", str(self.data), "-w", "-t", "30", "-m", "fast", "stop"], timeout=40)
            raise
        return True

    def stop(self) -> None:
        self.verify_cluster()
        process = self.actual_process()
        if process is None:
            return
        if not self.process_path.is_file() or read_json(self.process_path) != process:
            raise TrialDatabaseError("database_process_record_mismatch")
        self.run("pg_ctl", ["-D", str(self.data), "-w", "-t", "30", "-m", "fast", "stop"], timeout=40)
        if self.actual_process() is not None:
            raise TrialDatabaseError("database_stop_not_confirmed")

    def tools(self, username: str, database: str = "assetlibrary_trial"):
        tools = self.migrations.PostgresTools(self.manifest, self.migrations.Connection(
            host="127.0.0.1", port=self.port, username=username, database=database,
            confirm_database=database, postgres_bin=self.bin,
        ))
        tools.environment = self.environment.copy()
        tools.environment["PGPASSWORD"] = read_json(self.passwords_path)[username]
        return tools

    def initialize(self) -> dict:
        self.stage = "cluster_initialize"
        if not self.passwords_path.exists():
            if self.data.exists():
                raise TrialDatabaseError("database_existing_data_without_credentials")
            write_json(self.passwords_path, {name: secrets.token_urlsafe(36) for name in (
                "assetlibrary_trial_cluster_admin", "assetlibrary_trial_migrator", "assetlibrary_trial_audit", "assetlibrary_trial_gateway", "assetlibrary_trial_library", "assetlibrary_trial_asset", "assetlibrary_trial_scan", "assetlibrary_trial_task",
            )})
        passwords = read_json(self.passwords_path)
        if not self.data.exists():
            password_file = self.state / "secrets/initdb-password.txt"
            with password_file.open("x", encoding="utf-8") as stream:
                stream.write(passwords["assetlibrary_trial_cluster_admin"])
            try:
                self.run("initdb", ["-D", str(self.data), "-U", "assetlibrary_trial_cluster_admin", "--auth=scram-sha-256", "--encoding=UTF8", "--no-locale", "--pwfile", str(password_file)])
                with (self.data / "postgresql.conf").open("a", encoding="utf-8") as stream:
                    stream.write(f"\nlisten_addresses = '127.0.0.1'\nport = {self.port}\nmax_connections = 60\n")
                    stream.write("password_encryption = 'scram-sha-256'\nlog_statement = 'none'\nlog_min_error_statement = 'panic'\n")
                write_json(self.cluster_path, {"deployment_id": self.owner["deployment_id"], "system_identifier": self.control_identity()})
            finally:
                password_file.unlink(missing_ok=True)
        self.verify_cluster()
        started = self.start()
        try:
            admin = self.tools("assetlibrary_trial_cluster_admin", "postgres")
            self.stage = "database_create"
            if admin.psql("SELECT 1 FROM pg_database WHERE datname='assetlibrary_trial';").strip() != "1":
                admin.psql("CREATE DATABASE assetlibrary_trial;")
            admin = self.tools("assetlibrary_trial_cluster_admin")
            self.stage = "fixed_roles"
            with contextlib.redirect_stdout(io.StringIO()):
                self.migrations.provision_roles(admin, self.manifest, explicit=True)
            memberships = {
                "assetlibrary_trial_migrator": "assetlibrary_migration_executor",
                "assetlibrary_trial_audit": "assetlibrary_database_auditor",
                "assetlibrary_trial_gateway": "assetlibrary_gateway_auth_runtime",
                "assetlibrary_trial_library": "assetlibrary_library_storage_runtime",
                "assetlibrary_trial_asset": "assetlibrary_asset_identity_runtime",
                "assetlibrary_trial_scan": "assetlibrary_scan_reconciliation_runtime",
                "assetlibrary_trial_task": "assetlibrary_task_health_runtime",
            }
            statements = ["BEGIN;"]
            for login, role in memberships.items():
                identifier = self.migrations.sql_identifier(login)
                if admin.psql(f"SELECT 1 FROM pg_roles WHERE rolname='{login}';").strip() != "1":
                    statements.append(f"CREATE ROLE {identifier} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD {self.migrations.sql_literal(passwords[login])};")
                statements.extend([
                    f"GRANT {role} TO {identifier} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;",
                    f"GRANT CONNECT ON DATABASE assetlibrary_trial TO {identifier};",
                ])
            statements.append("COMMIT;")
            admin.psql("\n".join(statements))
            self.stage = "migrations"
            with contextlib.redirect_stdout(io.StringIO()):
                self.migrations.apply_migrations(self.tools("assetlibrary_trial_migrator"), self.manifest, self.state / "backups")
            for login, role in memberships.items():
                self.stage = "login_verification"
                details = admin.psql(f"SELECT rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_roles WHERE rolname='{login}';").strip()
                grants = admin.psql(f"SELECT granted.rolname, m.inherit_option, m.set_option, m.admin_option FROM pg_auth_members m JOIN pg_roles granted ON granted.oid=m.roleid JOIN pg_roles member ON member.oid=m.member WHERE member.rolname='{login}';").strip()
                if details != "t" or grants != f"{role}|f|t|f":
                    raise TrialDatabaseError("database_login_privileges_invalid")
                self.tools(login).psql(f"SET ROLE {role}; SELECT 1;")
                if login != "assetlibrary_trial_migrator":
                    name = login.removeprefix("assetlibrary_trial_")
                    path = self.state / f"secrets/{name}.connection"
                    content = f"Host=127.0.0.1;Port={self.port};Database=assetlibrary_trial;Username={login};Password={passwords[login]};Timeout=5;Command Timeout=15;SSL Mode=Disable\n"
                    if path.exists() and path.read_text(encoding="utf-8") != content:
                        raise TrialDatabaseError("database_connection_file_mismatch")
                    if not path.exists():
                        path.write_text(content, encoding="utf-8", newline="\n")
            _, ledger = self.migrations.ledger_rows(self.tools("assetlibrary_trial_migrator"), self.manifest)
            return {"status": "initialized", "migrations": len(ledger), "module_logins": 6}
        finally:
            if started:
                self.stop()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("initialize", "start", "stop", "status"))
    parser.add_argument("--state", required=True, type=Path)
    args = parser.parse_args()
    if os.name != "nt" or not sys.flags.isolated:
        print('{"status":"failed","code":"windows_isolated_python_required"}')
        return 1
    try:
        database = TrialDatabase(args.state, Path(__file__).resolve().parent)
        if args.action == "initialize":
            result = database.initialize()
        elif args.action == "start":
            result = {"status": "started" if database.start() else "already_running"}
        elif args.action == "stop":
            database.stop()
            result = {"status": "stopped"}
        else:
            database.verify_cluster()
            result = {"status": "running" if database.actual_process() else "stopped"}
        print(json.dumps(result, sort_keys=True))
        return 0
    except TrialDatabaseError as error:
        print(json.dumps({"status": "failed", "code": str(error)}))
        return 1
    except Exception as error:
        print(json.dumps({"status": "failed", "code": "database_operation_failed", "stage": getattr(locals().get("database"), "stage", "configuration"), "error_type": type(error).__name__}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
