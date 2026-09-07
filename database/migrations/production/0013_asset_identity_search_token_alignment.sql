-- Match query tokenization to the existing generated path_search_document.
-- Preserve the projection signature, keyset order, owner and existing grants.
CREATE OR REPLACE FUNCTION asset_identity.search_read_entries(
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
          @@ plainto_tsquery(
              'simple'::regconfig, translate(normalized_search, '/._-', '    ')
          )
      AND (
          cursor_sort_name IS NULL
          OR (lower(entry.entry_name), entry.library_id, entry.entry_id)
             > (cursor_sort_name, cursor_library_id, cursor_entry_id)
      )
    ORDER BY lower(entry.entry_name), entry.library_id, entry.entry_id
    LIMIT requested_limit;
END
$search_read_entries$;
