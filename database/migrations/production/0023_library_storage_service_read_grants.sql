-- LibraryStorage is the sole writer of the existing library ACL. No credential ACL copy.
CREATE TABLE library_storage.service_read_grant_operation (
    operator_id text NOT NULL,
    correlation_id uuid NOT NULL,
    principal_id uuid NOT NULL,
    library_ids uuid[] NOT NULL,
    granted boolean NOT NULL,
    occurred_at timestamptz NOT NULL,
    PRIMARY KEY (operator_id, correlation_id)
);

REVOKE ALL ON library_storage.service_read_grant_operation
    FROM assetlibrary_library_storage_runtime;

CREATE FUNCTION library_storage.set_service_read_grants(
    requested_principal uuid, requested_libraries uuid[], requested_granted boolean,
    requested_operator text, requested_correlation uuid, requested_now timestamptz
) RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, library_storage
AS $set_service_read_grants$
DECLARE
    normalized_libraries uuid[];
    previous library_storage.service_read_grant_operation%ROWTYPE;
BEGIN
    IF requested_principal IS NULL OR requested_principal = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_libraries IS NULL OR cardinality(requested_libraries) NOT BETWEEN 1 AND 100
       OR array_ndims(requested_libraries) <> 1
       OR array_position(requested_libraries, NULL) IS NOT NULL
       OR '00000000-0000-0000-0000-000000000000'::uuid = ANY(requested_libraries)
       OR requested_granted IS NULL OR requested_now IS NULL
       OR requested_operator IS NULL OR length(requested_operator) NOT BETWEEN 1 AND 200
       OR requested_operator <> btrim(requested_operator) OR requested_operator ~ '[[:cntrl:]]'
       OR requested_correlation IS NULL OR requested_correlation = '00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'service_grant_invalid';
    END IF;
    SELECT array_agg(id ORDER BY id) INTO normalized_libraries
    FROM (SELECT DISTINCT unnest(requested_libraries) AS id) AS ids;
    IF cardinality(normalized_libraries) <> cardinality(requested_libraries) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'service_grant_invalid';
    END IF;

    PERFORM pg_advisory_xact_lock(hashtextextended(requested_operator || requested_correlation::text, 109551023));
    -- The owning module holds the principal row lock until this transaction commits,
    -- serializing grants with disable without granting LibraryStorage cross-table writes.
    IF NOT gateway_auth.lock_service_read_principal(requested_principal, requested_granted) THEN
        RAISE EXCEPTION USING ERRCODE = '42501', MESSAGE = 'service_principal_unavailable';
    END IF;
    IF EXISTS (SELECT 1 FROM unnest(normalized_libraries) AS requested(id)
        WHERE NOT EXISTS (SELECT 1 FROM library_storage.library_root AS root WHERE root.library_id = requested.id)) THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'library_not_found';
    END IF;

    SELECT operation.* INTO previous FROM library_storage.service_read_grant_operation AS operation
    WHERE operation.operator_id = requested_operator AND operation.correlation_id = requested_correlation;
    IF FOUND THEN
        IF previous.principal_id <> requested_principal OR previous.library_ids <> normalized_libraries
           OR previous.granted <> requested_granted THEN
            RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'idempotency_conflict';
        END IF;
        -- A replay must not resurrect an ACL revoked by a later operation or return stale success.
        IF EXISTS (SELECT 1 FROM unnest(normalized_libraries) AS requested(id)
            LEFT JOIN library_storage.library_permission AS permission
              ON permission.library_id = requested.id AND permission.principal_id = requested_principal
            WHERE (requested_granted AND permission.access_level IS DISTINCT FROM 'read_only'::library_storage.library_access_level)
               OR (NOT requested_granted AND permission.principal_id IS NOT NULL)) THEN
            RAISE EXCEPTION USING ERRCODE = '40001', MESSAGE = 'state_conflict';
        END IF;
        RETURN;
    END IF;

    IF requested_granted THEN
        INSERT INTO library_storage.library_permission(library_id, principal_id, access_level, granted_at, updated_at)
        SELECT id, requested_principal, 'read_only', requested_now, requested_now
        FROM unnest(normalized_libraries) AS requested(id)
        ON CONFLICT (library_id, principal_id) DO UPDATE
        SET access_level = 'read_only', updated_at = greatest(library_permission.granted_at, EXCLUDED.updated_at);
    ELSE
        DELETE FROM library_storage.library_permission
        WHERE principal_id = requested_principal AND library_id = ANY(normalized_libraries);
    END IF;
    INSERT INTO library_storage.service_read_grant_operation
        (operator_id, correlation_id, principal_id, library_ids, granted, occurred_at)
    VALUES (requested_operator, requested_correlation, requested_principal, normalized_libraries,
        requested_granted, requested_now);
END
$set_service_read_grants$;

REVOKE ALL ON FUNCTION library_storage.set_service_read_grants(uuid, uuid[], boolean, text, uuid, timestamptz)
    FROM PUBLIC;
GRANT EXECUTE ON FUNCTION library_storage.set_service_read_grants(uuid, uuid[], boolean, text, uuid, timestamptz)
    TO assetlibrary_library_storage_runtime;
