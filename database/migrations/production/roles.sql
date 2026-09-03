-- Cluster-global NOLOGIN roles and database-local connection/create grants.
-- Run only through the explicit provision-roles command on the intended database.
DO $assetlibrary_roles$
DECLARE
    role_name text;
    role_is_safe boolean;
BEGIN
    FOREACH role_name IN ARRAY ARRAY[
        'assetlibrary_migration_owner',
        'assetlibrary_migration_executor',
        'assetlibrary_database_backup',
        'assetlibrary_database_auditor',
        'assetlibrary_gateway_auth_owner',
        'assetlibrary_gateway_auth_runtime',
        'assetlibrary_library_storage_owner',
        'assetlibrary_library_storage_runtime',
        'assetlibrary_asset_identity_owner',
        'assetlibrary_asset_identity_runtime',
        'assetlibrary_metadata_sidecar_owner',
        'assetlibrary_metadata_sidecar_runtime',
        'assetlibrary_scan_reconciliation_owner',
        'assetlibrary_scan_reconciliation_runtime',
        'assetlibrary_transfer_sync_owner',
        'assetlibrary_transfer_sync_runtime',
        'assetlibrary_operation_trash_owner',
        'assetlibrary_operation_trash_runtime',
        'assetlibrary_search_dedup_owner',
        'assetlibrary_search_dedup_runtime',
        'assetlibrary_preview_provider_owner',
        'assetlibrary_preview_provider_runtime',
        'assetlibrary_task_health_owner',
        'assetlibrary_task_health_runtime',
        'assetlibrary_backup_update_owner',
        'assetlibrary_backup_update_runtime'
    ]
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format(
                'CREATE ROLE %I NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS',
                role_name
            );
        ELSE
            SELECT NOT rolcanlogin
                AND NOT rolinherit
                AND NOT rolsuper
                AND NOT rolcreatedb
                AND NOT rolcreaterole
                AND NOT rolreplication
                AND NOT rolbypassrls
            INTO role_is_safe
            FROM pg_roles
            WHERE rolname = role_name;

            IF NOT role_is_safe THEN
                RAISE EXCEPTION 'existing AssetLibrary role % has unsafe attributes', role_name;
            END IF;
        END IF;
    END LOOP;
END
$assetlibrary_roles$;

GRANT assetlibrary_migration_owner
    TO assetlibrary_migration_executor
    WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;

GRANT assetlibrary_database_backup
    TO assetlibrary_migration_executor
    WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;

-- The NOLOGIN migration owner inherits NOLOGIN module-owner privileges only so
-- the foundation can configure their default ACLs. Deployment/runtime boundaries
-- above and in deployment remain non-inheriting and require explicit role switching.
GRANT
    assetlibrary_gateway_auth_owner,
    assetlibrary_library_storage_owner,
    assetlibrary_asset_identity_owner,
    assetlibrary_metadata_sidecar_owner,
    assetlibrary_scan_reconciliation_owner,
    assetlibrary_transfer_sync_owner,
    assetlibrary_operation_trash_owner,
    assetlibrary_search_dedup_owner,
    assetlibrary_preview_provider_owner,
    assetlibrary_task_health_owner,
    assetlibrary_backup_update_owner
TO assetlibrary_migration_owner
WITH INHERIT TRUE, SET TRUE, ADMIN FALSE;

DO $assetlibrary_database_grants$
DECLARE
    database_name text := current_database();
BEGIN
    EXECUTE format('REVOKE CONNECT, TEMPORARY ON DATABASE %I FROM PUBLIC', database_name);
    EXECUTE format(
        'GRANT CONNECT, CREATE ON DATABASE %I TO assetlibrary_migration_owner',
        database_name
    );
    EXECUTE format(
        'GRANT CONNECT ON DATABASE %I TO assetlibrary_migration_executor, assetlibrary_database_backup, assetlibrary_database_auditor',
        database_name
    );
    EXECUTE format(
        'GRANT CONNECT ON DATABASE %I TO '
        || 'assetlibrary_gateway_auth_runtime, '
        || 'assetlibrary_library_storage_runtime, '
        || 'assetlibrary_asset_identity_runtime, '
        || 'assetlibrary_metadata_sidecar_runtime, '
        || 'assetlibrary_scan_reconciliation_runtime, '
        || 'assetlibrary_transfer_sync_runtime, '
        || 'assetlibrary_operation_trash_runtime, '
        || 'assetlibrary_search_dedup_runtime, '
        || 'assetlibrary_preview_provider_runtime, '
        || 'assetlibrary_task_health_runtime, '
        || 'assetlibrary_backup_update_runtime',
        database_name
    );
END
$assetlibrary_database_grants$;
