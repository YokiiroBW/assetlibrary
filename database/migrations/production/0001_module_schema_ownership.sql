-- Reserves module schemas and least-privilege runtime defaults; no domain tables.
CREATE TABLE migration.module_ownership (
    module_name text PRIMARY KEY CHECK (module_name ~ '^[A-Z][A-Za-z]+$'),
    schema_name name NOT NULL UNIQUE,
    owner_role name NOT NULL UNIQUE,
    runtime_role name NOT NULL UNIQUE,
    CHECK (owner_role <> runtime_role)
);

INSERT INTO migration.module_ownership (
    module_name,
    schema_name,
    owner_role,
    runtime_role
)
VALUES
    ('GatewayAuth', 'gateway_auth', 'assetlibrary_gateway_auth_owner', 'assetlibrary_gateway_auth_runtime'),
    ('LibraryStorage', 'library_storage', 'assetlibrary_library_storage_owner', 'assetlibrary_library_storage_runtime'),
    ('AssetIdentity', 'asset_identity', 'assetlibrary_asset_identity_owner', 'assetlibrary_asset_identity_runtime'),
    ('MetadataSidecar', 'metadata_sidecar', 'assetlibrary_metadata_sidecar_owner', 'assetlibrary_metadata_sidecar_runtime'),
    ('ScanReconciliation', 'scan_reconciliation', 'assetlibrary_scan_reconciliation_owner', 'assetlibrary_scan_reconciliation_runtime'),
    ('TransferSync', 'transfer_sync', 'assetlibrary_transfer_sync_owner', 'assetlibrary_transfer_sync_runtime'),
    ('OperationTrash', 'operation_trash', 'assetlibrary_operation_trash_owner', 'assetlibrary_operation_trash_runtime'),
    ('SearchDedup', 'search_dedup', 'assetlibrary_search_dedup_owner', 'assetlibrary_search_dedup_runtime'),
    ('PreviewProvider', 'preview_provider', 'assetlibrary_preview_provider_owner', 'assetlibrary_preview_provider_runtime'),
    ('TaskHealth', 'task_health', 'assetlibrary_task_health_owner', 'assetlibrary_task_health_runtime'),
    ('BackupUpdate', 'backup_update', 'assetlibrary_backup_update_owner', 'assetlibrary_backup_update_runtime');

DO $module_schemas$
DECLARE
    module_record record;
BEGIN
    FOR module_record IN
        SELECT schema_name::text, owner_role::text, runtime_role::text
        FROM migration.module_ownership
        ORDER BY module_name
    LOOP
        EXECUTE format(
            'CREATE SCHEMA %I AUTHORIZATION assetlibrary_migration_owner',
            module_record.schema_name
        );
        EXECUTE format(
            'REVOKE ALL ON SCHEMA %I FROM PUBLIC',
            module_record.schema_name
        );
        EXECUTE format(
            'GRANT USAGE ON SCHEMA %I TO %I',
            module_record.schema_name,
            module_record.runtime_role
        );
        EXECUTE format(
            'GRANT USAGE ON SCHEMA %I TO assetlibrary_database_backup',
            module_record.schema_name
        );
        EXECUTE format(
            'ALTER SCHEMA %I OWNER TO %I',
            module_record.schema_name,
            module_record.owner_role
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE EXECUTE ON ROUTINES FROM PUBLIC',
            module_record.owner_role
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE USAGE ON TYPES FROM PUBLIC',
            module_record.owner_role
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TABLES FROM PUBLIC',
            module_record.owner_role,
            module_record.schema_name
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I',
            module_record.owner_role,
            module_record.schema_name,
            module_record.runtime_role
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT SELECT ON TABLES TO assetlibrary_database_backup',
            module_record.owner_role,
            module_record.schema_name
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I',
            module_record.owner_role,
            module_record.schema_name,
            module_record.runtime_role
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT SELECT ON SEQUENCES TO assetlibrary_database_backup',
            module_record.owner_role,
            module_record.schema_name
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT EXECUTE ON ROUTINES TO %I',
            module_record.owner_role,
            module_record.schema_name,
            module_record.runtime_role
        );
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT USAGE ON TYPES TO %I',
            module_record.owner_role,
            module_record.schema_name,
            module_record.runtime_role
        );
    END LOOP;
END
$module_schemas$;

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON migration.module_ownership FROM PUBLIC;
GRANT SELECT ON migration.module_ownership TO assetlibrary_database_auditor;
GRANT SELECT ON migration.module_ownership TO assetlibrary_database_backup;
