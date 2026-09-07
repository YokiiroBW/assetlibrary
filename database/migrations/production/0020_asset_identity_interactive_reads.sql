-- Extend only the present-fact read projection; discovery and asset writes are unchanged.
CREATE INDEX filesystem_entry_browse_modified_index
    ON asset_identity.filesystem_entry (library_id,parent_relative_path,last_write_time_utc,lower(entry_name),entry_id)
    WHERE state = 'present';
CREATE INDEX filesystem_entry_browse_size_asc_index
    ON asset_identity.filesystem_entry (library_id,parent_relative_path,content_length ASC NULLS LAST,lower(entry_name),entry_id)
    WHERE state = 'present';
CREATE INDEX filesystem_entry_browse_size_desc_index
    ON asset_identity.filesystem_entry (library_id,parent_relative_path,content_length DESC NULLS LAST,lower(entry_name) DESC,entry_id DESC)
    WHERE state = 'present';

CREATE FUNCTION asset_identity.find_read_entry(requested_library_id uuid, requested_entry_id uuid)
RETURNS TABLE (entry_id uuid,relative_path text,kind text,content_length bigint,last_write_time_utc timestamptz)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, asset_identity
AS $find_read_entry$
    SELECT entry.entry_id,entry.normalized_relative_path,entry.kind::text,entry.content_length,entry.last_write_time_utc
    FROM asset_identity.filesystem_entry AS entry
    WHERE entry.library_id = requested_library_id AND entry.entry_id = requested_entry_id AND entry.state = 'present'
$find_read_entry$;

CREATE FUNCTION asset_identity.browse_read_entries_v2(
    requested_library_id uuid, requested_parent_relative_path text,
    requested_sort text, requested_direction text, requested_kind text, requested_name_filter text,
    cursor_sort_name text, cursor_entry_id uuid, cursor_modified timestamptz, cursor_size bigint,
    requested_anchor_entry_id uuid, requested_limit integer
) RETURNS TABLE (entry_id uuid,relative_path text,kind text,content_length bigint,last_write_time_utc timestamptz,sort_name text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, asset_identity
AS $browse_read_entries_v2$
DECLARE ordering text; comparison text; after_cursor text; kind_clause text; selection text; null_after text;
BEGIN
    IF requested_library_id IS NULL OR requested_library_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_parent_relative_path IS NULL OR length(requested_parent_relative_path) > 4096
       OR requested_parent_relative_path LIKE '/%' OR requested_parent_relative_path ~ '^[A-Za-z]:'
       OR strpos(requested_parent_relative_path, chr(92)) > 0 OR strpos(requested_parent_relative_path, '//') > 0
       OR requested_parent_relative_path ~ '(^|/)\.{1,2}($|/)' OR requested_parent_relative_path ~ '[[:cntrl:]]'
       OR requested_sort IS NULL OR requested_sort NOT IN ('name','modified','size')
       OR requested_direction IS NULL OR requested_direction NOT IN ('asc','desc')
       OR requested_kind IS NULL OR requested_kind NOT IN ('all','files','directories')
       OR requested_name_filter IS NULL OR length(requested_name_filter) > 200 OR requested_name_filter ~ '[[:cntrl:]]'
       OR requested_limit IS NULL OR requested_limit NOT BETWEEN 1 AND 101 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry browse projection request is invalid';
    END IF;
    IF (cursor_sort_name IS NULL) <> (cursor_entry_id IS NULL)
       OR length(cursor_sort_name) > 4096 OR cursor_sort_name ~ '[[:cntrl:]]'
       OR (requested_anchor_entry_id IS NOT NULL AND cursor_entry_id IS NOT NULL)
       OR (cursor_entry_id IS NOT NULL AND requested_sort = 'modified' AND cursor_modified IS NULL)
       OR cursor_size < 0 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry browse projection cursor is invalid';
    END IF;
    IF requested_anchor_entry_id IS NOT NULL THEN
        SELECT lower(entry.entry_name),entry.entry_id,entry.last_write_time_utc,entry.content_length
        INTO cursor_sort_name,cursor_entry_id,cursor_modified,cursor_size
        FROM asset_identity.filesystem_entry AS entry
        WHERE entry.library_id = requested_library_id AND entry.entry_id = requested_anchor_entry_id
          AND entry.state = 'present' AND entry.parent_relative_path = requested_parent_relative_path
          AND (requested_kind = 'all' OR (requested_kind = 'files' AND entry.kind IN ('file','reparse_file'))
               OR (requested_kind = 'directories' AND entry.kind IN ('directory','reparse_directory')))
          AND strpos(lower(entry.entry_name),lower(requested_name_filter)) > 0;
        IF NOT FOUND THEN RETURN; END IF;
    END IF;

    -- Only validated, fixed SQL tokens are composed. Paths, names and typed anchors stay bound parameters.
    comparison := CASE WHEN requested_direction = 'asc' THEN '>' ELSE '<' END;
    IF requested_anchor_entry_id IS NOT NULL THEN comparison := comparison || '='; END IF;
    kind_clause := CASE requested_kind
        WHEN 'files' THEN ' AND entry.kind IN (''file'',''reparse_file'')'
        WHEN 'directories' THEN ' AND entry.kind IN (''directory'',''reparse_directory'')'
        ELSE '' END;
    selection := 'SELECT entry.entry_id,entry.normalized_relative_path,entry.kind::text,entry.content_length,'
        || 'entry.last_write_time_utc,lower(entry.entry_name) AS sort_name FROM asset_identity.filesystem_entry AS entry '
        || 'WHERE entry.state = ''present'' AND entry.library_id = $1 AND entry.parent_relative_path = $2'
        || kind_clause || ' AND strpos(lower(entry.entry_name),lower($3)) > 0';
    ordering := 'lower(entry.entry_name) ' || requested_direction || ',entry.entry_id ' || requested_direction;
    IF requested_sort = 'modified' THEN
        ordering := 'entry.last_write_time_utc ' || requested_direction || ',' || ordering;
        after_cursor := '(entry.last_write_time_utc,lower(entry.entry_name),entry.entry_id) ' || comparison || ' ($6,$4,$5)';
    ELSIF requested_sort = 'size' THEN
        ordering := 'entry.content_length ' || requested_direction || ' NULLS LAST,' || ordering;
        null_after := 'true';
        IF cursor_entry_id IS NULL THEN
            after_cursor := 'true';
        ELSIF cursor_size IS NULL THEN
            after_cursor := 'false';
            null_after := '(lower(entry.entry_name),entry.entry_id) ' || comparison || ' ($4,$5)';
        ELSE
            after_cursor := '(entry.content_length,lower(entry.entry_name),entry.entry_id) ' || comparison || ' ($7,$4,$5)';
        END IF;
        -- Separate range seeks avoid scanning from the index head for a late cursor with an OR-NULL filter.
        -- The final merge sorts at most twice the requested page bound, never the entire directory.
        RETURN QUERY EXECUTE 'SELECT page.* FROM (('
            || selection || ' AND entry.content_length IS NOT NULL AND (' || after_cursor || ') ORDER BY ' || ordering || ' LIMIT $8) UNION ALL ('
            || selection || ' AND entry.content_length IS NULL AND (' || null_after || ') ORDER BY ' || ordering || ' LIMIT $8)) AS page'
            || ' ORDER BY page.content_length ' || requested_direction || ' NULLS LAST,page.sort_name ' || requested_direction
            || ',page.entry_id ' || requested_direction || ' LIMIT $8'
        USING requested_library_id,requested_parent_relative_path,requested_name_filter,cursor_sort_name,cursor_entry_id,
            cursor_modified,cursor_size,requested_limit;
        RETURN;
    ELSE
        after_cursor := '(lower(entry.entry_name),entry.entry_id) ' || comparison || ' ($4,$5)';
    END IF;
    IF cursor_entry_id IS NULL THEN after_cursor := 'true'; END IF;

    RETURN QUERY EXECUTE
        selection || ' AND (' || after_cursor || ')'
        || ' ORDER BY ' || ordering || ' LIMIT $8'
    USING requested_library_id,requested_parent_relative_path,requested_name_filter,cursor_sort_name,cursor_entry_id,
        cursor_modified,cursor_size,requested_limit;
END
$browse_read_entries_v2$;

CREATE FUNCTION asset_identity.search_read_entries_v2(
    requested_library_ids uuid[],requested_search_text text,requested_parent_relative_path text,
    cursor_sort_name text,cursor_library_id uuid,cursor_entry_id uuid,requested_limit integer
) RETURNS TABLE (library_id uuid,entry_id uuid,relative_path text,kind text,content_length bigint,
    last_write_time_utc timestamptz,hit_reason text,sort_name text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, asset_identity
AS $search_read_entries_v2$
DECLARE normalized_search text;
BEGIN
    normalized_search := btrim(regexp_replace(requested_search_text, '\s+', ' ', 'g'));
    IF requested_library_ids IS NULL OR cardinality(requested_library_ids) > 10000
       OR array_position(requested_library_ids, NULL) IS NOT NULL
       OR requested_search_text IS NULL OR length(normalized_search) NOT BETWEEN 2 AND 200
       OR normalized_search ~ '[[:cntrl:]]' OR requested_limit IS NULL OR requested_limit NOT BETWEEN 1 AND 101
       OR (requested_parent_relative_path IS NOT NULL AND (length(requested_parent_relative_path) > 4096
           OR requested_parent_relative_path LIKE '/%' OR requested_parent_relative_path ~ '^[A-Za-z]:'
           OR strpos(requested_parent_relative_path, chr(92)) > 0 OR strpos(requested_parent_relative_path, '//') > 0
           OR requested_parent_relative_path ~ '(^|/)\.{1,2}($|/)' OR requested_parent_relative_path ~ '[[:cntrl:]]')) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry search projection request is invalid';
    END IF;
    IF (cursor_sort_name IS NULL) <> (cursor_library_id IS NULL)
       OR (cursor_sort_name IS NULL) <> (cursor_entry_id IS NULL)
       OR length(cursor_sort_name) > 4096 OR cursor_sort_name ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry search projection cursor is invalid';
    END IF;
    RETURN QUERY
    SELECT entry.library_id,entry.entry_id,entry.normalized_relative_path,entry.kind::text,entry.content_length,
        entry.last_write_time_utc,
        CASE WHEN position(lower(normalized_search) IN lower(entry.entry_name)) > 0 THEN 'name' ELSE 'path' END,
        lower(entry.entry_name)
    FROM asset_identity.filesystem_entry AS entry
    WHERE entry.state = 'present' AND entry.library_id = ANY(requested_library_ids)
      AND (requested_parent_relative_path IS NULL OR requested_parent_relative_path = ''
           OR starts_with(entry.normalized_relative_path, requested_parent_relative_path || '/'))
      AND entry.path_search_document @@ plainto_tsquery('simple'::regconfig,translate(normalized_search,'/._-','    '))
      AND (cursor_sort_name IS NULL OR (lower(entry.entry_name),entry.library_id,entry.entry_id)
           > (cursor_sort_name,cursor_library_id,cursor_entry_id))
    ORDER BY lower(entry.entry_name),entry.library_id,entry.entry_id LIMIT requested_limit;
END
$search_read_entries_v2$;

REVOKE ALL ON FUNCTION asset_identity.find_read_entry(uuid,uuid),
    asset_identity.browse_read_entries_v2(uuid,text,text,text,text,text,text,uuid,timestamptz,bigint,uuid,integer),
    asset_identity.search_read_entries_v2(uuid[],text,text,text,uuid,uuid,integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION asset_identity.find_read_entry(uuid,uuid),
    asset_identity.browse_read_entries_v2(uuid,text,text,text,text,text,text,uuid,timestamptz,bigint,uuid,integer),
    asset_identity.search_read_entries_v2(uuid[],text,text,text,uuid,uuid,integer) TO assetlibrary_gateway_auth_owner;
