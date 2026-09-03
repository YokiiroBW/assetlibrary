-- LibraryStorage owns per-library access state and exposes only approved read projections.
CREATE TYPE library_storage.library_access_level AS ENUM (
    'read_only',
    'read_write',
    'organize',
    'library_administrator'
);

CREATE TABLE library_storage.library_permission (
    library_id uuid NOT NULL REFERENCES library_storage.library_root(library_id),
    principal_id uuid NOT NULL
        CHECK (principal_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    access_level library_storage.library_access_level NOT NULL,
    granted_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (library_id, principal_id),
    CHECK (updated_at >= granted_at)
);

CREATE INDEX library_permission_principal_index
    ON library_storage.library_permission (principal_id, library_id, access_level);

CREATE VIEW library_storage.library_catalog_read_projection
WITH (security_barrier = true)
AS
SELECT
    root.library_id,
    root.display_name,
    source.availability
FROM library_storage.library_root AS root
JOIN library_storage.storage_source AS source
  ON source.storage_source_id = root.storage_source_id;

CREATE VIEW library_storage.library_permission_read_projection
WITH (security_barrier = true)
AS
SELECT
    permission.library_id,
    permission.principal_id,
    permission.access_level::text AS access_level
FROM library_storage.library_permission AS permission;

REVOKE ALL ON library_storage.library_permission
    FROM assetlibrary_library_storage_runtime;
GRANT SELECT ON library_storage.library_permission
    TO assetlibrary_library_storage_runtime;

GRANT USAGE ON SCHEMA library_storage
    TO assetlibrary_gateway_auth_owner;
GRANT SELECT ON
    library_storage.library_catalog_read_projection,
    library_storage.library_permission_read_projection
    TO assetlibrary_gateway_auth_owner;
