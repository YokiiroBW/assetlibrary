"""Persistence checks for the real service-read fixture; never logs credential material."""
from __future__ import annotations


def verify_persistence(module, fixture, database):
    privilege_sql = """
SELECT NOT has_table_privilege('assetlibrary_gateway_auth_runtime','library_storage.library_permission','INSERT,UPDATE,DELETE')
 AND NOT has_table_privilege('assetlibrary_library_storage_runtime','library_storage.library_permission','INSERT,UPDATE,DELETE')
 AND NOT has_table_privilege('assetlibrary_gateway_auth_runtime','gateway_auth.service_read_credential','SELECT,INSERT,UPDATE,DELETE')
 AND NOT has_function_privilege('assetlibrary_gateway_auth_runtime','library_storage.set_service_read_grants(uuid,uuid[],boolean,text,uuid,timestamptz)','EXECUTE');
"""
    if fixture.sql(database, fixture.admin, privilege_sql).stdout.strip() != "t":
        raise AssertionError("Service management runtime database privileges are too broad")
    snapshot_sql = """
SELECT (SELECT count(*) FROM gateway_auth.service_principal),
 (SELECT count(*) FROM gateway_auth.service_read_credential),
 (SELECT count(*) FROM gateway_auth.service_read_credential WHERE revoked_at IS NOT NULL),
 (SELECT count(*) FROM gateway_auth.authenticated_principal p JOIN gateway_auth.service_principal s USING(principal_id) WHERE p.disabled_at IS NOT NULL),
 (SELECT count(*) FROM gateway_auth.service_read_credential c CROSS JOIN LATERAL gateway_auth.authenticate_service_read_credential(c.secret_digest) a),
 (SELECT count(*) FROM library_storage.library_permission p JOIN gateway_auth.service_principal s USING(principal_id) WHERE p.access_level='read_only');
"""
    before = fixture.sql(database, fixture.admin, snapshot_sql).stdout.strip()
    values = [int(value) for value in before.split('|')]
    if values[0] < 2 or values[1] < 5 or values[2] < 2 or values[3] < 1 or values[4] < 1:
        raise AssertionError("The service credential lifecycle fixture did not persist expected states")
    fixture._run([str(fixture.pg_ctl), "--pgdata", str(fixture.cluster_data), "--wait", "--timeout", "30",
                  "--mode", "fast", "restart"], timeout=45, capture_output=False)
    if fixture.sql(database, fixture.admin, snapshot_sql).stdout.strip() != before:
        raise AssertionError("Service state changed across database restart")
    tools = fixture.runner_tools(database)
    _, rows = module.MIGRATIONS.ledger_rows(tools, fixture.manifest)
    backup = module.MIGRATIONS.create_backup(tools, fixture.manifest, fixture.backup_directory("service-state"), 160015, rows)
    restored = fixture.fresh_database("service_restore")
    module.MIGRATIONS.restore_backup(fixture.runner_tools(restored), fixture.manifest, backup.metadata_path)
    if fixture.sql(restored, fixture.admin, snapshot_sql).stdout.strip() != before:
        raise AssertionError("Service state changed across verified backup restore")
    failing = fixture.staged_manifest("service_failure_probe", "CREATE TABLE library_storage.ts063_failure(id integer);\nSELECT 1/0;\n")
    try:
        module.MIGRATIONS.apply_migrations(fixture.runner_tools(database, failing), failing, fixture.backup_directory("service-failure"))
    except module.MIGRATIONS.MigrationError:
        pass
    else:
        raise AssertionError("Injected migration failure was not rejected")
    if fixture.sql(database, fixture.admin, "SELECT to_regclass('library_storage.ts063_failure') IS NULL;").stdout.strip() != "t":
        raise AssertionError("Failed migration left partial schema")
    if fixture.sql(database, fixture.admin, snapshot_sql).stdout.strip() != before:
        raise AssertionError("Failed migration changed service state")
    module.MIGRATIONS.apply_migrations(tools, fixture.manifest, fixture.backup_directory("service-recovery"))
    return ["database_restart_lifecycle_state", "verified_backup_restore_lifecycle_state", "migration_failure_atomic_recovery"]
