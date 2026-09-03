-- AssetIdentity publishes present physical facts without hashes or scan internals.
ALTER TABLE asset_identity.filesystem_entry
    ADD COLUMN entry_name text
        GENERATED ALWAYS AS (
            reverse(split_part(reverse(normalized_relative_path), '/', 1))
        ) STORED,
    ADD COLUMN parent_relative_path text
        GENERATED ALWAYS AS (
            CASE
                WHEN strpos(normalized_relative_path, '/') = 0 THEN ''
                ELSE left(
                    normalized_relative_path,
                    length(normalized_relative_path)
                        - strpos(reverse(normalized_relative_path), '/')
                )
            END
        ) STORED,
    ADD COLUMN path_search_document tsvector
        GENERATED ALWAYS AS (
            to_tsvector(
                'simple'::regconfig,
                translate(normalized_relative_path, '/._-', '    ')
            )
        ) STORED;

CREATE INDEX filesystem_entry_browse_index
    ON asset_identity.filesystem_entry (
        library_id,
        parent_relative_path,
        (lower(entry_name)),
        entry_id
    )
    WHERE state = 'present';

CREATE INDEX filesystem_entry_path_search_index
    ON asset_identity.filesystem_entry
    USING gin (path_search_document)
    WHERE state = 'present';

CREATE VIEW asset_identity.entry_read_projection
WITH (security_barrier = true)
AS
SELECT
    entry.entry_id,
    entry.library_id,
    entry.normalized_relative_path,
    entry.entry_name,
    entry.parent_relative_path,
    entry.kind::text AS kind,
    entry.content_length,
    entry.last_write_time_utc,
    entry.path_search_document
FROM asset_identity.filesystem_entry AS entry
WHERE entry.state = 'present';

CREATE FUNCTION asset_identity.browse_read_entries(
    requested_library_id uuid,
    requested_parent_relative_path text,
    cursor_sort_name text,
    cursor_entry_id uuid,
    requested_limit integer
) RETURNS TABLE (
    entry_id uuid,
    relative_path text,
    kind text,
    content_length bigint,
    last_write_time_utc timestamptz,
    sort_name text
)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, asset_identity
AS $browse_read_entries$
BEGIN
    IF requested_library_id IS NULL
       OR requested_library_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_parent_relative_path IS NULL
       OR length(requested_parent_relative_path) > 4096
       OR requested_parent_relative_path LIKE '/%'
       OR requested_parent_relative_path ~ '^[A-Za-z]:'
       OR strpos(requested_parent_relative_path, chr(92)) > 0
       OR strpos(requested_parent_relative_path, '//') > 0
       OR requested_parent_relative_path ~ '(^|/)\.{1,2}($|/)'
       OR requested_limit IS NULL
       OR requested_limit NOT BETWEEN 1 AND 101 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry browse projection request is invalid';
    END IF;
    IF (cursor_sort_name IS NULL) <> (cursor_entry_id IS NULL)
       OR length(cursor_sort_name) > 4096
       OR cursor_sort_name ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry browse projection cursor is invalid';
    END IF;

    RETURN QUERY
    SELECT
        entry.entry_id,
        entry.normalized_relative_path,
        entry.kind::text,
        entry.content_length,
        entry.last_write_time_utc,
        lower(entry.entry_name)
    FROM asset_identity.filesystem_entry AS entry
    WHERE entry.state = 'present'
      AND entry.library_id = requested_library_id
      AND entry.parent_relative_path = requested_parent_relative_path
      AND (
          cursor_sort_name IS NULL
          OR (lower(entry.entry_name), entry.entry_id) > (cursor_sort_name, cursor_entry_id)
      )
    ORDER BY lower(entry.entry_name), entry.entry_id
    LIMIT requested_limit;
END
$browse_read_entries$;

CREATE FUNCTION asset_identity.search_read_entries(
    requested_library_ids uuid[],
    requested_search_text text,
    cursor_sort_name text,
    cursor_library_id uuid,
    cursor_entry_id uuid,
    requested_limit integer
) RETURNS TABLE (
    library_id uuid,
    entry_id uuid,
    relative_path text,
    kind text,
    content_length bigint,
    last_write_time_utc timestamptz,
    hit_reason text,
    sort_name text
)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, asset_identity
AS $search_read_entries$
DECLARE
    normalized_search text;
BEGIN
    normalized_search := btrim(regexp_replace(requested_search_text, '\s+', ' ', 'g'));
    IF requested_library_ids IS NULL
       OR cardinality(requested_library_ids) > 10000
       OR array_position(requested_library_ids, NULL) IS NOT NULL
       OR requested_search_text IS NULL
       OR length(normalized_search) NOT BETWEEN 2 AND 200
       OR normalized_search ~ '[[:cntrl:]]'
       OR requested_limit IS NULL
       OR requested_limit NOT BETWEEN 1 AND 101 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry search projection request is invalid';
    END IF;
    IF (cursor_sort_name IS NULL) <> (cursor_library_id IS NULL)
       OR (cursor_sort_name IS NULL) <> (cursor_entry_id IS NULL)
       OR length(cursor_sort_name) > 4096
       OR cursor_sort_name ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry search projection cursor is invalid';
    END IF;

    RETURN QUERY
    SELECT
        entry.library_id,
        entry.entry_id,
        entry.normalized_relative_path,
        entry.kind::text,
        entry.content_length,
        entry.last_write_time_utc,
        CASE
            WHEN position(lower(normalized_search) IN lower(entry.entry_name)) > 0 THEN 'name'
            ELSE 'path'
        END,
        lower(entry.entry_name)
    FROM asset_identity.filesystem_entry AS entry
    WHERE entry.state = 'present'
      AND entry.library_id = ANY(requested_library_ids)
      AND entry.path_search_document
          @@ plainto_tsquery('simple'::regconfig, normalized_search)
      AND (
          cursor_sort_name IS NULL
          OR (lower(entry.entry_name), entry.library_id, entry.entry_id)
             > (cursor_sort_name, cursor_library_id, cursor_entry_id)
      )
    ORDER BY lower(entry.entry_name), entry.library_id, entry.entry_id
    LIMIT requested_limit;
END
$search_read_entries$;

GRANT USAGE ON SCHEMA asset_identity
    TO assetlibrary_gateway_auth_owner;
REVOKE ALL ON
    FUNCTION asset_identity.browse_read_entries(uuid, text, text, uuid, integer),
    asset_identity.search_read_entries(uuid[], text, text, uuid, uuid, integer)
    FROM PUBLIC;
GRANT EXECUTE ON
    FUNCTION asset_identity.browse_read_entries(uuid, text, text, uuid, integer),
    asset_identity.search_read_entries(uuid[], text, text, uuid, uuid, integer)
    TO assetlibrary_gateway_auth_owner;
