-- All read callers share the existing principal/library permission policy.
CREATE FUNCTION gateway_auth.authorized_library_catalog(
    requested_subject_key text, requested_library_id uuid, requested_category text
) RETURNS TABLE (library_id uuid,display_name text,availability text,access_level text,category text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $authorized_library_catalog$
BEGIN
    IF requested_subject_key IS NULL OR length(requested_subject_key) NOT BETWEEN 1 AND 200
       OR requested_subject_key <> btrim(requested_subject_key) OR requested_subject_key ~ '[[:cntrl:]]'
       OR (requested_category IS NOT NULL AND requested_category NOT IN
          ('photos','images','videos','music','projects','documents','characters','general')) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'authorized library request is invalid';
    END IF;
    RETURN QUERY
    SELECT library.library_id,library.display_name,library.availability,
        CASE WHEN principal.is_system_administrator THEN 'library_administrator' ELSE permission.access_level END,
        library.category
    FROM gateway_auth.authenticated_principal AS principal
    CROSS JOIN library_storage.library_catalog_read_projection AS library
    LEFT JOIN library_storage.library_permission_read_projection AS permission
      ON permission.library_id = library.library_id AND permission.principal_id = principal.principal_id
    WHERE principal.subject_key = requested_subject_key AND principal.disabled_at IS NULL
      AND (principal.is_system_administrator OR permission.access_level IS NOT NULL)
      AND (requested_library_id IS NULL OR library.library_id = requested_library_id)
      AND (requested_category IS NULL OR library.category = requested_category);
END
$authorized_library_catalog$;

CREATE FUNCTION gateway_auth.list_authorized_libraries_v2(
    requested_subject_key text,cursor_sort_name text,cursor_library_id uuid,requested_limit integer,requested_category text
) RETURNS TABLE (library_id uuid,display_name text,availability text,access_level text,category text,sort_name text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $list_authorized_libraries_v2$
BEGIN
    IF requested_limit IS NULL OR requested_limit NOT BETWEEN 1 AND 101
       OR (cursor_sort_name IS NULL) <> (cursor_library_id IS NULL)
       OR length(cursor_sort_name) > 4096 OR cursor_sort_name ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'library page is invalid';
    END IF;
    RETURN QUERY
    SELECT library.*,lower(library.display_name)
    FROM gateway_auth.authorized_library_catalog(requested_subject_key,NULL,requested_category) AS library
    WHERE cursor_sort_name IS NULL OR (lower(library.display_name),library.library_id) > (cursor_sort_name,cursor_library_id)
    ORDER BY lower(library.display_name),library.library_id LIMIT requested_limit;
END
$list_authorized_libraries_v2$;

CREATE FUNCTION gateway_auth.find_authorized_library_v2(requested_subject_key text,requested_library_id uuid)
RETURNS TABLE (library_id uuid,display_name text,availability text,access_level text,category text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $find_authorized_library_v2$
BEGIN
    IF requested_library_id IS NULL OR requested_library_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'authorized library request is invalid';
    END IF;
    RETURN QUERY SELECT * FROM gateway_auth.authorized_library_catalog(requested_subject_key,requested_library_id,NULL);
END
$find_authorized_library_v2$;

CREATE FUNCTION gateway_auth.find_authorized_entry(requested_subject_key text,requested_library_id uuid,requested_entry_id uuid)
RETURNS TABLE (library_id uuid,library_display_name text,availability text,access_level text,category text,
    entry_id uuid,relative_path text,kind text,content_length bigint,last_write_time_utc timestamptz)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $find_authorized_entry$
BEGIN
    IF requested_entry_id IS NULL OR requested_entry_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'authorized entry request is invalid';
    END IF;
    RETURN QUERY
    SELECT library.*,entry.* FROM gateway_auth.find_authorized_library_v2(requested_subject_key,requested_library_id) AS library
    CROSS JOIN LATERAL asset_identity.find_read_entry(library.library_id,requested_entry_id) AS entry;
END
$find_authorized_entry$;

CREATE FUNCTION gateway_auth.browse_authorized_entries_v2(
    requested_subject_key text,requested_library_id uuid,requested_parent_relative_path text,
    requested_sort text,requested_direction text,requested_kind text,requested_name_filter text,
    cursor_sort_name text,cursor_entry_id uuid,cursor_modified timestamptz,cursor_size bigint,
    requested_anchor_entry_id uuid,requested_limit integer
) RETURNS TABLE (entry_id uuid,relative_path text,kind text,content_length bigint,last_write_time_utc timestamptz,sort_name text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $browse_authorized_entries_v2$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM gateway_auth.find_authorized_library_v2(requested_subject_key,requested_library_id)) THEN
        RETURN;
    END IF;
    -- Ordinality preserves every approved inner sort, including NULLS LAST in both directions.
    RETURN QUERY SELECT entry.entry_id,entry.relative_path,entry.kind,entry.content_length,entry.last_write_time_utc,entry.sort_name
    FROM asset_identity.browse_read_entries_v2(requested_library_id,requested_parent_relative_path,
        requested_sort,requested_direction,requested_kind,requested_name_filter,cursor_sort_name,cursor_entry_id,
        cursor_modified,cursor_size,requested_anchor_entry_id,requested_limit) WITH ORDINALITY AS entry
    ORDER BY entry.ordinality;
END
$browse_authorized_entries_v2$;

CREATE FUNCTION gateway_auth.search_authorized_entries_v2(
    requested_subject_key text,requested_search_text text,requested_scope text,requested_library_id uuid,
    requested_parent_relative_path text,cursor_sort_name text,cursor_library_id uuid,cursor_entry_id uuid,requested_limit integer
) RETURNS TABLE (library_id uuid,library_display_name text,availability text,access_level text,category text,
    entry_id uuid,relative_path text,kind text,content_length bigint,last_write_time_utc timestamptz,hit_reason text,sort_name text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $search_authorized_entries_v2$
BEGIN
    IF requested_scope IS NULL OR requested_scope NOT IN ('all','library','directory')
       OR (requested_scope = 'all') <> (requested_library_id IS NULL)
       OR (requested_scope = 'directory') <> (requested_parent_relative_path IS NOT NULL) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'search scope is invalid';
    END IF;
    IF requested_scope <> 'all' AND NOT EXISTS (
        SELECT 1 FROM gateway_auth.find_authorized_library_v2(requested_subject_key,requested_library_id)) THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'not_found';
    END IF;
    RETURN QUERY
    WITH authorized_library AS MATERIALIZED (
        SELECT * FROM gateway_auth.authorized_library_catalog(requested_subject_key,requested_library_id,NULL)
    ), matching_entry AS MATERIALIZED (
        SELECT entry.* FROM asset_identity.search_read_entries_v2(
            ARRAY(SELECT authorized_library.library_id FROM authorized_library),requested_search_text,
            requested_parent_relative_path,cursor_sort_name,cursor_library_id,cursor_entry_id,requested_limit) AS entry
    )
    SELECT library.library_id,library.display_name,library.availability,library.access_level,library.category,
        entry.entry_id,entry.relative_path,entry.kind,entry.content_length,entry.last_write_time_utc,entry.hit_reason,entry.sort_name
    FROM authorized_library AS library JOIN matching_entry AS entry ON entry.library_id = library.library_id
    ORDER BY entry.sort_name,entry.library_id,entry.entry_id LIMIT requested_limit;
END
$search_authorized_entries_v2$;

-- Keep the legacy SQL shapes and defaults while using the same authorization policy.
CREATE OR REPLACE FUNCTION gateway_auth.list_authorized_libraries(
    requested_subject_key text,cursor_sort_name text,cursor_library_id uuid,requested_limit integer
) RETURNS TABLE (library_id uuid,display_name text,availability text,access_level text,sort_name text)
LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $legacy_list$
    SELECT library_id,display_name,availability,access_level,sort_name
    FROM gateway_auth.list_authorized_libraries_v2(requested_subject_key,cursor_sort_name,cursor_library_id,requested_limit,NULL)
    ORDER BY sort_name,library_id
$legacy_list$;

CREATE OR REPLACE FUNCTION gateway_auth.find_authorized_library(requested_subject_key text,requested_library_id uuid)
RETURNS TABLE (library_id uuid,display_name text,availability text,access_level text)
LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $legacy_find$
    SELECT library_id,display_name,availability,access_level
    FROM gateway_auth.find_authorized_library_v2(requested_subject_key,requested_library_id)
$legacy_find$;

CREATE OR REPLACE FUNCTION gateway_auth.browse_authorized_entries(
    requested_subject_key text,requested_library_id uuid,requested_parent_relative_path text,
    cursor_sort_name text,cursor_entry_id uuid,requested_limit integer
) RETURNS TABLE (entry_id uuid,relative_path text,kind text,content_length bigint,last_write_time_utc timestamptz,sort_name text)
LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $legacy_browse$
    SELECT * FROM gateway_auth.browse_authorized_entries_v2(requested_subject_key,requested_library_id,requested_parent_relative_path,
        'name','asc','all','',cursor_sort_name,cursor_entry_id,NULL,NULL,NULL,requested_limit) ORDER BY sort_name,entry_id
$legacy_browse$;

CREATE OR REPLACE FUNCTION gateway_auth.search_authorized_entries(
    requested_subject_key text,requested_search_text text,cursor_sort_name text,cursor_library_id uuid,cursor_entry_id uuid,requested_limit integer
) RETURNS TABLE (library_id uuid,library_display_name text,availability text,access_level text,entry_id uuid,relative_path text,
    kind text,content_length bigint,last_write_time_utc timestamptz,hit_reason text,sort_name text)
LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $legacy_search$
    SELECT library_id,library_display_name,availability,access_level,entry_id,relative_path,kind,content_length,last_write_time_utc,hit_reason,sort_name
    FROM gateway_auth.search_authorized_entries_v2(requested_subject_key,requested_search_text,'all',NULL,NULL,
        cursor_sort_name,cursor_library_id,cursor_entry_id,requested_limit) ORDER BY sort_name,library_id,entry_id
$legacy_search$;

REVOKE ALL ON FUNCTION gateway_auth.authorized_library_catalog(text,uuid,text),
    gateway_auth.list_authorized_libraries_v2(text,text,uuid,integer,text),gateway_auth.find_authorized_library_v2(text,uuid),
    gateway_auth.find_authorized_entry(text,uuid,uuid),
    gateway_auth.browse_authorized_entries_v2(text,uuid,text,text,text,text,text,text,uuid,timestamptz,bigint,uuid,integer),
    gateway_auth.search_authorized_entries_v2(text,text,text,uuid,text,text,uuid,uuid,integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION gateway_auth.list_authorized_libraries_v2(text,text,uuid,integer,text),
    gateway_auth.find_authorized_library_v2(text,uuid),gateway_auth.find_authorized_entry(text,uuid,uuid),
    gateway_auth.browse_authorized_entries_v2(text,uuid,text,text,text,text,text,text,uuid,timestamptz,bigint,uuid,integer),
    gateway_auth.search_authorized_entries_v2(text,text,text,uuid,text,text,uuid,uuid,integer) TO assetlibrary_gateway_auth_runtime;
REVOKE ALL ON FUNCTION gateway_auth.authorized_library_catalog(text,uuid,text) FROM assetlibrary_gateway_auth_runtime;
