-- Executed once, transactionally, as assetlibrary_migration_owner.
CREATE SCHEMA migration AUTHORIZATION assetlibrary_migration_owner;
REVOKE ALL ON SCHEMA migration FROM PUBLIC;

CREATE TABLE migration.bootstrap_state (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    format_version integer NOT NULL CHECK (format_version = 1),
    checksum character(64) NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    installed_at timestamp with time zone NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE migration.ledger (
    version integer PRIMARY KEY CHECK (version > 0),
    name text NOT NULL UNIQUE CHECK (name ~ '^[a-z][a-z0-9_]*$'),
    module text NOT NULL,
    owner_role text NOT NULL CHECK (owner_role ~ '^assetlibrary_[a-z0-9_]+_owner$'),
    checksum character(64) NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    manifest_checksum character(64) NOT NULL CHECK (manifest_checksum ~ '^[0-9a-f]{64}$'),
    backup_id text NOT NULL CHECK (backup_id <> ''),
    backup_sha256 character(64) NOT NULL CHECK (backup_sha256 ~ '^[0-9a-f]{64}$'),
    server_version_num integer NOT NULL CHECK (
        server_version_num >= 160000 AND server_version_num < 170000
    ),
    duration_ms bigint NOT NULL CHECK (duration_ms >= 0),
    applied_at timestamp with time zone NOT NULL DEFAULT clock_timestamp()
);

REVOKE ALL ON ALL TABLES IN SCHEMA migration FROM PUBLIC;
GRANT USAGE ON SCHEMA migration TO assetlibrary_database_auditor;
GRANT SELECT ON migration.bootstrap_state, migration.ledger
    TO assetlibrary_database_auditor;
GRANT USAGE ON SCHEMA migration TO assetlibrary_database_backup;
GRANT SELECT ON migration.bootstrap_state, migration.ledger
    TO assetlibrary_database_backup;

ALTER DEFAULT PRIVILEGES FOR ROLE assetlibrary_migration_owner
    REVOKE EXECUTE ON ROUTINES FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE assetlibrary_migration_owner
    REVOKE USAGE ON TYPES FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE assetlibrary_migration_owner IN SCHEMA migration
    REVOKE ALL ON TABLES FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE assetlibrary_migration_owner IN SCHEMA migration
    GRANT SELECT ON TABLES TO assetlibrary_database_auditor;
ALTER DEFAULT PRIVILEGES FOR ROLE assetlibrary_migration_owner IN SCHEMA migration
    GRANT SELECT ON TABLES TO assetlibrary_database_backup;
