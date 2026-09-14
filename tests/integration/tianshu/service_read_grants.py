"""Real LibraryStorage runtime grant mutation boundaries on synthetic TS063 state."""
from __future__ import annotations

import uuid


def verify_grants(fixture, database, library_login):
    def admin(sql):
        return fixture.sql(database, fixture.admin, sql).stdout.strip()

    def apply(principal, libraries, granted, operation, error=None):
        values = ','.join("'" + str(uuid.UUID(value)) + "'::uuid" for value in libraries)
        statement = ("SET ROLE assetlibrary_library_storage_runtime; SELECT library_storage.set_service_read_grants("
                     f"'{uuid.UUID(principal)}',ARRAY[{values}],{str(granted).lower()},'TS063-db-verification','{operation}',clock_timestamp());")
        result = fixture.sql(database, library_login, statement, check=error is None)
        if error is not None and (result.returncode == 0 or error not in result.stderr):
            raise AssertionError("Expected grant denial: " + error)

    active = admin("SELECT p.principal_id FROM gateway_auth.authenticated_principal p JOIN gateway_auth.service_principal s USING(principal_id) WHERE p.disabled_at IS NULL LIMIT 1;")
    disabled = admin("SELECT p.principal_id FROM gateway_auth.authenticated_principal p JOIN gateway_auth.service_principal s USING(principal_id) WHERE p.disabled_at IS NOT NULL LIMIT 1;")
    ordinary = admin("SELECT principal_id FROM gateway_auth.authenticated_principal WHERE subject_key='local:trial-reader';")
    administrator = admin("SELECT principal_id FROM gateway_auth.authenticated_principal WHERE is_system_administrator LIMIT 1;")
    library = admin("SELECT library_id FROM library_storage.library_root ORDER BY library_id LIMIT 1;")
    def permissions():
        return admin(f"SELECT library_id,access_level FROM library_storage.library_permission WHERE principal_id='{active}' ORDER BY library_id;")
    before = permissions()
    apply(active, [library, str(uuid.uuid4())], True, uuid.uuid4(), "library_not_found")
    if permissions() != before:
        raise AssertionError("Failed batch changed prior existing permissions")
    for principal in (ordinary, administrator, disabled):
        apply(principal, [library], True, uuid.uuid4(), "service_principal_unavailable")
    apply(disabled, [library], False, uuid.uuid4())
    operation = uuid.uuid4()
    apply(active, [library], True, operation)
    apply(active, [library], True, operation)
    count = admin(f"SELECT count(*) FROM library_storage.service_read_grant_operation WHERE operator_id='TS063-db-verification' AND correlation_id='{operation}';")
    if count != "1":
        raise AssertionError("Matching retry duplicated its audit event")
    apply(active, [library], False, operation, "idempotency_conflict")
    apply(active, [library], False, uuid.uuid4())
    apply(active, [library], True, operation, "state_conflict")
    if admin(f"SELECT count(*) FROM library_storage.library_permission WHERE principal_id='{active}' AND library_id='{library}';") != "0":
        raise AssertionError("Stale retry resurrected revoked permission")
    apply(active, [library], True, uuid.uuid4())
    if admin(f"SELECT access_level FROM library_storage.library_permission WHERE principal_id='{active}' AND library_id='{library}';") != "read_only":
        raise AssertionError("Service grant exceeded read_only")
    denied = fixture.sql(database, library_login,
        "SET ROLE assetlibrary_library_storage_runtime; DELETE FROM library_storage.library_permission WHERE false;", check=False)
    if denied.returncode == 0 or "permission denied" not in denied.stderr:
        raise AssertionError("Runtime direct ACL write was not denied")
    return ["failed_grant_preserves_existing_acl", "ordinary_admin_disabled_grant_denied", "disabled_revoke_allowed",
            "grant_retry_single_audit", "conflicting_operation_denied", "stale_retry_no_resurrection", "runtime_direct_write_denied"]
