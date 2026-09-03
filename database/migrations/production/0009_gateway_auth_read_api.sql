-- GatewayAuth maps trusted authentication subjects and owns permission-filtered read entry points.
CREATE TABLE gateway_auth.authenticated_principal (
    principal_id uuid PRIMARY KEY
        CHECK (principal_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    subject_key text NOT NULL UNIQUE
        CHECK (
            length(subject_key) BETWEEN 1 AND 200
            AND subject_key = btrim(subject_key)
            AND subject_key !~ '[[:cntrl:]]'
        ),
    display_name text NOT NULL
        CHECK (length(btrim(display_name)) BETWEEN 1 AND 200),
    is_system_administrator boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL,
    disabled_at timestamptz,
    CHECK (disabled_at IS NULL OR disabled_at >= created_at)
);

REVOKE ALL ON gateway_auth.authenticated_principal
    FROM assetlibrary_gateway_auth_runtime;

CREATE FUNCTION gateway_auth.list_authorized_libraries(
    requested_subject_key text,
    cursor_sort_name text,
    cursor_library_id uuid,
    requested_limit integer
) RETURNS TABLE (
    library_id uuid,
    display_name text,
    availability text,
    access_level text,
    sort_name text
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $list_authorized_libraries$
BEGIN
    IF requested_subject_key IS NULL
       OR length(requested_subject_key) NOT BETWEEN 1 AND 200
       OR requested_subject_key <> btrim(requested_subject_key)
       OR requested_subject_key ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'authenticated subject is invalid';
    END IF;
    IF requested_limit IS NULL OR requested_limit NOT BETWEEN 1 AND 101 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'read limit is invalid';
    END IF;
    IF (cursor_sort_name IS NULL) <> (cursor_library_id IS NULL)
       OR length(cursor_sort_name) > 4096
       OR cursor_sort_name ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'library cursor is incomplete';
    END IF;

    RETURN QUERY
    WITH caller AS MATERIALIZED (
        SELECT principal.principal_id, principal.is_system_administrator
        FROM gateway_auth.authenticated_principal AS principal
        WHERE principal.subject_key = requested_subject_key
          AND principal.disabled_at IS NULL
    ), authorized AS MATERIALIZED (
        SELECT
            library.library_id,
            library.display_name,
            library.availability,
            CASE
                WHEN caller.is_system_administrator THEN 'library_administrator'
                ELSE permission.access_level
            END AS access_level,
            lower(library.display_name) AS sort_name
        FROM caller
        CROSS JOIN library_storage.library_catalog_read_projection AS library
        LEFT JOIN library_storage.library_permission_read_projection AS permission
          ON permission.library_id = library.library_id
         AND permission.principal_id = caller.principal_id
        WHERE caller.is_system_administrator OR permission.access_level IS NOT NULL
    )
    SELECT
        authorized.library_id,
        authorized.display_name,
        authorized.availability,
        authorized.access_level,
        authorized.sort_name
    FROM authorized
    WHERE cursor_sort_name IS NULL
       OR (authorized.sort_name, authorized.library_id) > (cursor_sort_name, cursor_library_id)
    ORDER BY authorized.sort_name, authorized.library_id
    LIMIT requested_limit;
END
$list_authorized_libraries$;

CREATE FUNCTION gateway_auth.find_authorized_library(
    requested_subject_key text,
    requested_library_id uuid
) RETURNS TABLE (
    library_id uuid,
    display_name text,
    availability text,
    access_level text
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $find_authorized_library$
BEGIN
    IF requested_subject_key IS NULL
       OR length(requested_subject_key) NOT BETWEEN 1 AND 200
       OR requested_subject_key <> btrim(requested_subject_key)
       OR requested_subject_key ~ '[[:cntrl:]]'
       OR requested_library_id IS NULL
       OR requested_library_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'authorized library request is invalid';
    END IF;

    RETURN QUERY
    WITH caller AS MATERIALIZED (
        SELECT principal.principal_id, principal.is_system_administrator
        FROM gateway_auth.authenticated_principal AS principal
        WHERE principal.subject_key = requested_subject_key
          AND principal.disabled_at IS NULL
    )
    SELECT
        library.library_id,
        library.display_name,
        library.availability,
        CASE
            WHEN caller.is_system_administrator THEN 'library_administrator'
            ELSE permission.access_level
        END AS access_level
    FROM caller
    JOIN library_storage.library_catalog_read_projection AS library
      ON library.library_id = requested_library_id
    LEFT JOIN library_storage.library_permission_read_projection AS permission
      ON permission.library_id = library.library_id
     AND permission.principal_id = caller.principal_id
    WHERE caller.is_system_administrator OR permission.access_level IS NOT NULL;
END
$find_authorized_library$;

CREATE FUNCTION gateway_auth.browse_authorized_entries(
    requested_subject_key text,
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
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $browse_authorized_entries$
BEGIN
    IF requested_subject_key IS NULL
       OR length(requested_subject_key) NOT BETWEEN 1 AND 200
       OR requested_subject_key <> btrim(requested_subject_key)
       OR requested_subject_key ~ '[[:cntrl:]]'
       OR requested_library_id IS NULL
       OR requested_library_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_parent_relative_path IS NULL
       OR length(requested_parent_relative_path) > 4096 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'browse request is invalid';
    END IF;
    IF requested_limit IS NULL OR requested_limit NOT BETWEEN 1 AND 101 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'read limit is invalid';
    END IF;
    IF (cursor_sort_name IS NULL) <> (cursor_entry_id IS NULL) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'entry cursor is incomplete';
    END IF;

    RETURN QUERY
    WITH caller AS MATERIALIZED (
        SELECT principal.principal_id, principal.is_system_administrator
        FROM gateway_auth.authenticated_principal AS principal
        WHERE principal.subject_key = requested_subject_key
          AND principal.disabled_at IS NULL
    ), authorized_library AS MATERIALIZED (
        SELECT library.library_id
        FROM caller
        JOIN library_storage.library_catalog_read_projection AS library
          ON library.library_id = requested_library_id
        LEFT JOIN library_storage.library_permission_read_projection AS permission
          ON permission.library_id = library.library_id
         AND permission.principal_id = caller.principal_id
        WHERE caller.is_system_administrator OR permission.access_level IS NOT NULL
    )
    SELECT entry.*
    FROM authorized_library
    CROSS JOIN LATERAL asset_identity.browse_read_entries(
        authorized_library.library_id,
        requested_parent_relative_path,
        cursor_sort_name,
        cursor_entry_id,
        requested_limit
    ) AS entry
    ORDER BY entry.sort_name, entry.entry_id
    LIMIT requested_limit;
END
$browse_authorized_entries$;

CREATE FUNCTION gateway_auth.search_authorized_entries(
    requested_subject_key text,
    requested_search_text text,
    cursor_sort_name text,
    cursor_library_id uuid,
    cursor_entry_id uuid,
    requested_limit integer
) RETURNS TABLE (
    library_id uuid,
    library_display_name text,
    availability text,
    access_level text,
    entry_id uuid,
    relative_path text,
    kind text,
    content_length bigint,
    last_write_time_utc timestamptz,
    hit_reason text,
    sort_name text
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $search_authorized_entries$
DECLARE
    normalized_search text;
BEGIN
    normalized_search := btrim(regexp_replace(requested_search_text, '\s+', ' ', 'g'));
    IF requested_subject_key IS NULL
       OR length(requested_subject_key) NOT BETWEEN 1 AND 200
       OR requested_subject_key <> btrim(requested_subject_key)
       OR requested_subject_key ~ '[[:cntrl:]]'
       OR requested_search_text IS NULL
       OR length(normalized_search) NOT BETWEEN 2 AND 200
       OR normalized_search ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'search request is invalid';
    END IF;
    IF requested_limit IS NULL OR requested_limit NOT BETWEEN 1 AND 101 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'read limit is invalid';
    END IF;
    IF (cursor_sort_name IS NULL) <> (cursor_library_id IS NULL)
       OR (cursor_sort_name IS NULL) <> (cursor_entry_id IS NULL) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'search cursor is incomplete';
    END IF;

    RETURN QUERY
    WITH caller AS MATERIALIZED (
        SELECT principal.principal_id, principal.is_system_administrator
        FROM gateway_auth.authenticated_principal AS principal
        WHERE principal.subject_key = requested_subject_key
          AND principal.disabled_at IS NULL
    ), authorized_library AS MATERIALIZED (
        SELECT
            library.library_id,
            library.display_name,
            library.availability,
            CASE
                WHEN caller.is_system_administrator THEN 'library_administrator'
                ELSE permission.access_level
            END AS access_level
        FROM caller
        CROSS JOIN library_storage.library_catalog_read_projection AS library
        LEFT JOIN library_storage.library_permission_read_projection AS permission
          ON permission.library_id = library.library_id
         AND permission.principal_id = caller.principal_id
        WHERE caller.is_system_administrator OR permission.access_level IS NOT NULL
    ), matching_entry AS MATERIALIZED (
        SELECT entry.*
        FROM asset_identity.search_read_entries(
            ARRAY(SELECT authorized_library.library_id FROM authorized_library),
            normalized_search,
            cursor_sort_name,
            cursor_library_id,
            cursor_entry_id,
            requested_limit
        ) AS entry
    )
    SELECT
        authorized_library.library_id,
        authorized_library.display_name,
        authorized_library.availability,
        authorized_library.access_level,
        entry.entry_id,
        entry.relative_path,
        entry.kind,
        entry.content_length,
        entry.last_write_time_utc,
        entry.hit_reason,
        entry.sort_name
    FROM authorized_library
    JOIN matching_entry AS entry
      ON entry.library_id = authorized_library.library_id
    ORDER BY entry.sort_name, entry.library_id, entry.entry_id
    LIMIT requested_limit;
END
$search_authorized_entries$;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA gateway_auth FROM PUBLIC;
GRANT EXECUTE ON
    FUNCTION gateway_auth.list_authorized_libraries(text, text, uuid, integer),
    gateway_auth.find_authorized_library(text, uuid),
    gateway_auth.browse_authorized_entries(text, uuid, text, text, uuid, integer),
    gateway_auth.search_authorized_entries(text, text, text, uuid, uuid, integer)
    TO assetlibrary_gateway_auth_runtime;
