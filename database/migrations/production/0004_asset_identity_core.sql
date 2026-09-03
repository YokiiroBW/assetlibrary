-- AssetIdentity owns stable entry IDs, observed paths and content revisions.
CREATE TYPE asset_identity.entry_kind AS ENUM (
    'file',
    'directory',
    'reparse_file',
    'reparse_directory'
);

CREATE TYPE asset_identity.entry_state AS ENUM (
    'present',
    'missing',
    'pending_review'
);

CREATE TABLE asset_identity.library_index_snapshot (
    library_id uuid PRIMARY KEY,
    scan_id uuid NOT NULL UNIQUE,
    entry_count integer NOT NULL CHECK (entry_count >= 0),
    observed_at timestamptz NOT NULL
);

CREATE TABLE asset_identity.filesystem_entry (
    entry_id uuid PRIMARY KEY,
    library_id uuid NOT NULL,
    normalized_relative_path text NOT NULL
        CHECK (length(normalized_relative_path) BETWEEN 1 AND 4096),
    kind asset_identity.entry_kind NOT NULL,
    content_length bigint,
    last_write_time_utc timestamptz NOT NULL,
    content_hash bytea CHECK (content_hash IS NULL OR octet_length(content_hash) = 32),
    state asset_identity.entry_state NOT NULL DEFAULT 'present',
    first_seen_scan_id uuid NOT NULL,
    last_seen_scan_id uuid NOT NULL,
    first_seen_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,
    CHECK (
        (kind IN ('directory', 'reparse_file', 'reparse_directory') AND content_length IS NULL)
        OR (kind = 'file' AND content_length >= 0)
    ),
    UNIQUE (library_id, normalized_relative_path)
);

CREATE TABLE asset_identity.content_revision (
    entry_id uuid NOT NULL REFERENCES asset_identity.filesystem_entry(entry_id),
    revision_number bigint NOT NULL CHECK (revision_number > 0),
    content_hash bytea NOT NULL CHECK (octet_length(content_hash) = 32),
    content_length bigint NOT NULL CHECK (content_length >= 0),
    last_write_time_utc timestamptz NOT NULL,
    observed_at timestamptz NOT NULL,
    PRIMARY KEY (entry_id, revision_number)
);

CREATE TABLE asset_identity.scan_observation_stage (
    scan_id uuid NOT NULL,
    entry_id uuid NOT NULL,
    library_id uuid NOT NULL,
    normalized_relative_path text NOT NULL
        CHECK (length(normalized_relative_path) BETWEEN 1 AND 4096),
    kind asset_identity.entry_kind NOT NULL,
    content_length bigint,
    last_write_time_utc timestamptz NOT NULL,
    staged_at timestamptz NOT NULL,
    CHECK (
        (kind IN ('directory', 'reparse_file', 'reparse_directory') AND content_length IS NULL)
        OR (kind = 'file' AND content_length >= 0)
    ),
    PRIMARY KEY (scan_id, normalized_relative_path),
    UNIQUE (scan_id, entry_id)
);

CREATE INDEX filesystem_entry_library_state_index
    ON asset_identity.filesystem_entry (library_id, state, normalized_relative_path);

CREATE INDEX filesystem_entry_content_hash_index
    ON asset_identity.filesystem_entry (content_hash)
    WHERE content_hash IS NOT NULL;

CREATE INDEX scan_observation_stage_scan_index
    ON asset_identity.scan_observation_stage (scan_id, library_id);

CREATE FUNCTION asset_identity.commit_initial_scan(
    requested_scan_id uuid,
    requested_library_id uuid,
    requested_observed_at timestamptz
) RETURNS integer
LANGUAGE plpgsql
SET search_path = pg_catalog, asset_identity
AS $commit_initial_scan$
DECLARE
    inserted_count integer;
BEGIN
    IF EXISTS (
        SELECT 1
        FROM asset_identity.scan_observation_stage
        WHERE scan_id = requested_scan_id
          AND library_id <> requested_library_id
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            MESSAGE = 'scan contains observations for another library';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM asset_identity.library_index_snapshot
        WHERE library_id = requested_library_id
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23505',
            MESSAGE = 'initial scan cannot replace an existing asset index';
    END IF;

    INSERT INTO asset_identity.filesystem_entry (
        entry_id,
        library_id,
        normalized_relative_path,
        kind,
        content_length,
        last_write_time_utc,
        first_seen_scan_id,
        last_seen_scan_id,
        first_seen_at,
        last_seen_at
    )
    SELECT
        entry_id,
        library_id,
        normalized_relative_path,
        kind,
        content_length,
        last_write_time_utc,
        scan_id,
        scan_id,
        requested_observed_at,
        requested_observed_at
    FROM asset_identity.scan_observation_stage
    WHERE scan_id = requested_scan_id
      AND library_id = requested_library_id
    ORDER BY normalized_relative_path;

    GET DIAGNOSTICS inserted_count = ROW_COUNT;

    INSERT INTO asset_identity.library_index_snapshot (
        library_id,
        scan_id,
        entry_count,
        observed_at
    ) VALUES (
        requested_library_id,
        requested_scan_id,
        inserted_count,
        requested_observed_at
    );

    DELETE FROM asset_identity.scan_observation_stage
    WHERE scan_id = requested_scan_id;

    RETURN inserted_count;
END
$commit_initial_scan$;

CREATE FUNCTION asset_identity.abort_initial_scan(requested_scan_id uuid)
RETURNS integer
LANGUAGE sql
SET search_path = pg_catalog, asset_identity
AS $abort_initial_scan$
    WITH removed AS (
        DELETE FROM asset_identity.scan_observation_stage
        WHERE scan_id = requested_scan_id
        RETURNING 1
    )
    SELECT count(*)::integer FROM removed
$abort_initial_scan$;
