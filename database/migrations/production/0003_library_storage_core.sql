-- LibraryStorage owns storage availability and exactly one physical root per library.
CREATE TABLE library_storage.storage_source (
    storage_source_id uuid PRIMARY KEY,
    display_name text NOT NULL CHECK (length(btrim(display_name)) BETWEEN 1 AND 200),
    availability text NOT NULL CHECK (availability IN ('online', 'offline')),
    root_case_sensitive boolean NOT NULL,
    availability_observed_at timestamptz NOT NULL
);

CREATE TABLE library_storage.library_root (
    library_id uuid PRIMARY KEY,
    storage_source_id uuid NOT NULL REFERENCES library_storage.storage_source(storage_source_id),
    display_name text NOT NULL CHECK (length(btrim(display_name)) BETWEEN 1 AND 200),
    canonical_root text NOT NULL CHECK (length(canonical_root) BETWEEN 1 AND 4096),
    root_identity text NOT NULL CHECK (length(root_identity) BETWEEN 1 AND 4096),
    created_at timestamptz NOT NULL,
    UNIQUE (storage_source_id, root_identity)
);

CREATE INDEX library_root_source_index
    ON library_storage.library_root (storage_source_id, library_id);

CREATE INDEX storage_source_availability_index
    ON library_storage.storage_source (availability, availability_observed_at);

CREATE FUNCTION library_storage.register_library_root(
    requested_library_id uuid,
    requested_storage_source_id uuid,
    requested_display_name text,
    requested_canonical_root text,
    requested_created_at timestamptz
) RETURNS void
LANGUAGE plpgsql
SET search_path = pg_catalog, library_storage
AS $register_library_root$
DECLARE
    source_case_sensitive boolean;
    requested_root_identity text;
BEGIN
    PERFORM pg_advisory_xact_lock(
        hashtextextended(requested_storage_source_id::text, 109551004)
    );

    SELECT root_case_sensitive
    INTO source_case_sensitive
    FROM library_storage.storage_source
    WHERE storage_source_id = requested_storage_source_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION USING
            ERRCODE = '23503',
            MESSAGE = 'storage source does not exist';
    END IF;

    requested_root_identity := CASE
        WHEN source_case_sensitive THEN requested_canonical_root
        ELSE lower(requested_canonical_root)
    END;

    IF EXISTS (
        SELECT 1
        FROM library_storage.library_root AS existing
        WHERE existing.storage_source_id = requested_storage_source_id
          AND (
              existing.root_identity = requested_root_identity
              OR left(
                  requested_root_identity,
                  length(existing.root_identity)
                      + CASE WHEN right(existing.root_identity, 1) = '/' THEN 0 ELSE 1 END
              ) = existing.root_identity
                  || CASE WHEN right(existing.root_identity, 1) = '/' THEN '' ELSE '/' END
              OR left(
                  existing.root_identity,
                  length(requested_root_identity)
                      + CASE WHEN right(requested_root_identity, 1) = '/' THEN 0 ELSE 1 END
              ) = requested_root_identity
                  || CASE WHEN right(requested_root_identity, 1) = '/' THEN '' ELSE '/' END
          )
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23505',
            MESSAGE = 'library root overlaps an existing root';
    END IF;

    INSERT INTO library_storage.library_root (
        library_id,
        storage_source_id,
        display_name,
        canonical_root,
        root_identity,
        created_at
    ) VALUES (
        requested_library_id,
        requested_storage_source_id,
        requested_display_name,
        requested_canonical_root,
        requested_root_identity,
        requested_created_at
    );
END
$register_library_root$;
