from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path


def verify_interactive_upgrade(test, migrations, root: Path) -> None:
    """Use the existing required PG harness, with a pre-upgrade committed snapshot."""
    temporary = tempfile.TemporaryDirectory(prefix="interactive-manifest-")
    test.addCleanup(temporary.cleanup)
    old_root = Path(temporary.name) / "production"
    shutil.copytree(root / "database/migrations/production", old_root)
    manifest_path = old_root / "manifest.json"
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    for item in data["migrations"][18:]:
        (old_root / item["path"]).unlink()
    data["migrations"] = data["migrations"][:18]
    manifest_path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    database = test.web_gateway_database("interactive_upgrade", migrations.load_manifest(manifest_path))
    library, source, scan, principal, hidden = [uuid.uuid4() for _ in range(5)]
    stamp = "2026-09-04T00:00:00Z"
    registration_key = uuid.uuid4()
    legacy_body = json.dumps({"SourceKey": "fixture", "DisplayName": "Fixture", "RootPath": "C:/sandbox/interactive"})
    test.sql(database, test.admin, f"""
SET ROLE assetlibrary_library_storage_owner;
INSERT INTO library_storage.storage_source VALUES ('{source}','Fixture','online',false,'{stamp}');
SELECT library_storage.register_trial_library('{principal}','{registration_key}','{legacy_body}'::jsonb,
    '{library}','{source}','Fixture',false,'Fixture','C:/sandbox/interactive','{stamp}');
INSERT INTO library_storage.library_permission VALUES ('{library}','{principal}','read_only','{stamp}','{stamp}');
SELECT library_storage.register_library_root(md5('interactive-other-library')::uuid,'{source}','Other','C:/sandbox/interactive-other','{stamp}');
INSERT INTO library_storage.library_permission VALUES (md5('interactive-other-library')::uuid,'{principal}','read_only','{stamp}','{stamp}');
RESET ROLE;
SET ROLE assetlibrary_gateway_auth_owner;
INSERT INTO gateway_auth.authenticated_principal(principal_id,subject_key,display_name,created_at) VALUES
    ('{principal}','oidc:interactive-reader','Reader','{stamp}'),
    ('{hidden}','oidc:interactive-hidden','Hidden','{stamp}');
RESET ROLE;
SET ROLE assetlibrary_asset_identity_owner;
INSERT INTO asset_identity.filesystem_entry(entry_id,library_id,normalized_relative_path,kind,content_length,
    last_write_time_utc,first_seen_scan_id,last_seen_scan_id,first_seen_at,last_seen_at)
SELECT md5(item::text || '-interactive-folder')::uuid,'{library}',
    format('folder/entry_%s.txt',lpad(item::text,3,'0')),'file',item % 7,
    '{stamp}'::timestamptz + (item % 3) * interval '1 microsecond','{scan}','{scan}','{stamp}','{stamp}'
FROM generate_series(1,150) item;
INSERT INTO asset_identity.filesystem_entry(entry_id,library_id,normalized_relative_path,kind,content_length,
    last_write_time_utc,first_seen_scan_id,last_seen_scan_id,first_seen_at,last_seen_at)
SELECT md5(path)::uuid,'{library}',path,kind::asset_identity.entry_kind,size,'{stamp}',
    '{scan}','{scan}','{stamp}','{stamp}' FROM (VALUES
    ('folder/literal%_name.txt','file',12::bigint),
    ('folder/huge-number.txt','file',9007199254740999::bigint),
    ('folder/link-file','reparse_file',NULL::bigint),
    ('folder/dir','directory',NULL::bigint),
    ('folder/link-dir','reparse_directory',NULL::bigint),
    ('folder/nested/boundary.txt','file',1::bigint),
    ('folderx/boundary.txt','file',1::bigint)) fixture(path,kind,size);
INSERT INTO asset_identity.filesystem_entry(entry_id,library_id,normalized_relative_path,kind,content_length,
    last_write_time_utc,first_seen_scan_id,last_seen_scan_id,first_seen_at,last_seen_at)
SELECT md5(item::text || '-interactive-scale')::uuid,'{library}',
    format('huge/entry_%s.txt',lpad(item::text,6,'0')),'file',item,
    '{stamp}'::timestamptz + item * interval '1 microsecond','{scan}','{scan}','{stamp}','{stamp}'
FROM generate_series(1,200000) item;
INSERT INTO asset_identity.library_index_snapshot SELECT '{library}','{scan}',count(*),'{stamp}'
FROM asset_identity.filesystem_entry WHERE library_id = '{library}';
ANALYZE asset_identity.filesystem_entry;
""")
    facts = ("SELECT count(*) || ':' || md5(string_agg(md5(entry::text),'' ORDER BY entry_id)) "
             "FROM asset_identity.filesystem_entry entry;")
    before = test.sql(database, test.admin, facts).stdout.strip()
    entry_count = int(before.split(":", 1)[0])
    test.assertEqual(200157, entry_count)
    snapshot = test.sql(database, test.admin, "SELECT row_to_json(snapshot) FROM asset_identity.library_index_snapshot snapshot;").stdout.strip()
    migrations.apply_migrations(test.runner_tools(database), test.manifest, test.backup_directory("interactive-upgrade"))
    test.assertEqual(before, test.sql(database, test.admin, facts).stdout.strip())
    test.assertEqual(snapshot, test.sql(database, test.admin, "SELECT row_to_json(snapshot) FROM asset_identity.library_index_snapshot snapshot;").stdout.strip())
    print(json.dumps({"v026_upgrade": "18_to_21", "entries": entry_count, "facts_preserved": True,
        "snapshot_preserved": json.loads(snapshot)}, sort_keys=True))
    test.assertEqual("general", test.gateway_sql(database, f"SELECT category FROM gateway_auth.find_authorized_library_v2('oidc:interactive-reader','{library}');").stdout.strip())
    _category_checks(test, database, library, principal, source, registration_key, legacy_body, stamp)
    _index_checks(test, database, library, stamp)
    _dotnet_checks(test, database, library, root)


def _category_checks(test, database, library, principal, source, registration_key, legacy_body, stamp):
    key = uuid.uuid4()
    update = (f"SELECT library_storage.update_library_category('{principal}','{key}','{library}',"
              f"'images','general','{stamp}');")
    def execute(statement, check=True):
        return test.sql(database, test.RUNTIME, "SET ROLE assetlibrary_library_storage_runtime;\n" + statement, check=check)
    test.assertEqual("images", execute(update).stdout.strip())
    test.assertEqual("images", execute(update).stdout.strip())
    conflict = execute(update.replace("'images'", "'photos'"), check=False)
    test.assertNotEqual(0, conflict.returncode)
    test.assertIn("idempotency_conflict", conflict.stderr)
    stale = execute(update.replace(str(key), str(uuid.uuid4())), check=False)
    test.assertNotEqual(0, stale.returncode)
    test.assertIn("state_conflict", stale.stderr)
    replay = execute(f"SELECT library_storage.register_trial_library_v2('{principal}','{registration_key}','{legacy_body}'::jsonb,"
                     f"'{uuid.uuid4()}','{source}','Fixture',false,'Fixture','C:/sandbox/interactive','{stamp}','general');")
    test.assertEqual(str(library), replay.stdout.strip())
    test.assertEqual("images", execute(f"SELECT category FROM library_storage.library_root WHERE library_id='{library}';").stdout.strip())
    execute(f"UPDATE library_storage.library_root SET availability='offline' WHERE library_id='{library}';")
    for sql in (
        f"SELECT library_storage.update_library_category('{principal}','{uuid.uuid4()}','{library}','photos','images','{stamp}');",
        "SELECT * FROM library_storage.library_root;",
        "SELECT * FROM asset_identity.filesystem_entry;",
        f"SELECT * FROM asset_identity.find_read_entry('{library}','{uuid.uuid4()}');",
    ):
        denied = test.gateway_sql(database, sql, check=False)
        test.assertNotEqual(0, denied.returncode)
        test.assertIn("permission denied", denied.stderr)
    hidden = test.gateway_sql(database, f"SELECT * FROM gateway_auth.find_authorized_library_v2('oidc:interactive-hidden','{library}');")
    test.assertEqual("", hidden.stdout.strip())
    test.assertEqual(["Fixture", "Other"], test.gateway_sql(database, "SELECT display_name FROM gateway_auth.list_authorized_libraries('oidc:interactive-reader',NULL,NULL,101);").stdout.strip().splitlines())
    test.assertEqual("101", test.gateway_sql(database, "SELECT count(*) FROM gateway_auth.browse_authorized_entries("
        f"'oidc:interactive-reader','{library}','folder',NULL,NULL,101);").stdout.strip())
    test.assertEqual("2", test.gateway_sql(database, "SELECT count(*) FROM gateway_auth.search_authorized_entries("
        "'oidc:interactive-reader','boundary',NULL,NULL,NULL,101);").stdout.strip())


def _index_checks(test, database, library, stamp):
    # Check the actual fixed inner ORDER BY and keyset predicates used by the projection.
    checks = (
        ("last_write_time_utc ASC,lower(entry_name) ASC,entry_id ASC", "filesystem_entry_browse_modified_index",
         f"(last_write_time_utc,lower(entry_name),entry_id) > ('{stamp}'::timestamptz + interval '0.15 seconds','entry_150000.txt',md5('150000-interactive-scale')::uuid)"),
        ("content_length ASC NULLS LAST,lower(entry_name) ASC,entry_id ASC", "filesystem_entry_browse_size_asc_index",
         "content_length IS NOT NULL AND (content_length,lower(entry_name),entry_id) > (150000,'entry_150000.txt',md5('150000-interactive-scale')::uuid)"),
        ("content_length DESC NULLS LAST,lower(entry_name) DESC,entry_id DESC", "filesystem_entry_browse_size_desc_index",
         "content_length IS NOT NULL AND (content_length,lower(entry_name),entry_id) < (150000,'entry_150000.txt',md5('150000-interactive-scale')::uuid)"),
    )
    for ordering, index, after in checks:
        for predicate in ("true", after):
            plan = test.sql(database, test.admin, f"""
SET ROLE assetlibrary_asset_identity_owner;
EXPLAIN (ANALYZE, TIMING OFF)
SELECT entry_id FROM asset_identity.filesystem_entry
WHERE library_id='{library}' AND parent_relative_path='huge' AND state='present' AND ({predicate})
ORDER BY {ordering} LIMIT 101;
""").stdout
            test.assertIn(index, plan)
            test.assertNotIn("Sort Method", plan)
    print(json.dumps({"v026_indexes": "first_and_later_pages", "scale_directory_entries": 200000,
        "plans_checked": 6, "full_sort": False}, sort_keys=True))
    bounded = test.gateway_sql(database, "SELECT count(*) FROM gateway_auth.browse_authorized_entries_v2("
        f"'oidc:interactive-reader','{library}','huge','modified','desc','all','',NULL,NULL,NULL,NULL,NULL,101);")
    test.assertEqual("101", bounded.stdout.strip())


def _dotnet_checks(test, database, library, root):
    dotnet = os.environ.get("ASSETLIBRARY_TEST_DOTNET") or shutil.which("dotnet")
    if not dotnet:
        test.unavailable("dotnet is required for the interactive read adapter test")
    project = root / "tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj"
    if not (project.parent / "bin/Release/net10.0/AssetLibrary.WebGateway.Tests.dll").is_file():
        test.unavailable("the interactive read test assembly is not built")
    environment = test.environment.copy()
    environment["ASSETLIBRARY_TEST_INTERACTIVE_CONNECTION"] = (
        f"Host={test.host};Port={test.port};Database={database};Username={test.RUNTIME};"
        "Pooling=false;Timeout=5;Command Timeout=5;SSL Mode=Disable")
    environment["ASSETLIBRARY_TEST_INTERACTIVE_LIBRARY"] = str(library)
    results = test.backup_directory("interactive-dotnet-results")
    result = subprocess.run([str(dotnet), "test", str(project), "--configuration", "Release", "--no-build", "--no-restore",
        "--filter", "FullyQualifiedName~InteractiveReadModelIntegrationTests", "--logger", "console;verbosity=minimal",
        "--logger", "trx;LogFileName=interactive.trx", "--results-directory", str(results)],
        cwd=root, env=environment, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120, check=False)
    test.assertEqual(0, result.returncode, result.stdout + result.stderr)
    counters = ET.parse(results / "interactive.trx").find("{*}ResultSummary/{*}Counters")
    test.assertIsNotNone(counters, result.stdout)
    observed = {key: int(counters.attrib[key]) for key in ("total", "executed", "passed", "failed", "notExecuted")}
    test.assertEqual({"total": 1, "executed": 1, "passed": 1, "failed": 0, "notExecuted": 0}, observed)
    print(json.dumps({"v026_dotnet": observed}, sort_keys=True))
