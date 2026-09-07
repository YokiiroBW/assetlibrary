-- Preparation follows verified local operator authorization; final recovery still performs CAS.
CREATE FUNCTION gateway_auth.prepare_local_administrator_recovery(
    requested_authorization_id uuid,
    requested_operation_id uuid,
    requested_account_name text,
    requested_authorization_expires_at timestamptz
) RETURNS TABLE(outcome text, expected_credential_version bigint)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $prepare_local_administrator_recovery$
DECLARE
    previous gateway_auth.administrator_bootstrap_recovery_operation%ROWTYPE;
    observed_version bigint;
BEGIN
    IF requested_authorization_id IS NULL
       OR requested_authorization_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_operation_id IS NULL
       OR requested_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
       OR requested_authorization_expires_at IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'administrator recovery preparation input is invalid';
    END IF;

    PERFORM pg_catalog.pg_advisory_xact_lock(109551001012);
    IF requested_authorization_expires_at <= clock_timestamp() THEN
        RETURN QUERY SELECT 'authorization_rejected'::text, NULL::bigint;
        RETURN;
    END IF;

    SELECT operation.* INTO previous
    FROM gateway_auth.administrator_bootstrap_recovery_operation AS operation
    WHERE operation.authorization_id = requested_authorization_id
       OR operation.operation_id = requested_operation_id
    ORDER BY (operation.authorization_id = requested_authorization_id) DESC
    LIMIT 1;
    IF FOUND THEN
        IF NOT gateway_auth.administrator_bootstrap_recovery_request_matches(
            previous, requested_authorization_id, requested_operation_id,
            'recover_administrator', requested_account_name,
            requested_authorization_expires_at, NULL::text, previous.expected_credential_version
        ) THEN
            RETURN QUERY SELECT 'request_conflict'::text, NULL::bigint;
        ELSE
            RETURN QUERY SELECT 'ready'::text, previous.expected_credential_version;
        END IF;
        RETURN;
    END IF;

    SELECT credential.credential_version INTO observed_version
    FROM gateway_auth.local_account_credential AS credential
    JOIN gateway_auth.authenticated_principal AS principal
      ON principal.principal_id = credential.principal_id
    WHERE credential.account_name = requested_account_name
      AND principal.subject_key = 'local:' || requested_account_name
      AND principal.is_system_administrator;
    IF NOT FOUND THEN
        RETURN QUERY SELECT 'state_conflict'::text, NULL::bigint;
    ELSE
        RETURN QUERY SELECT 'ready'::text, observed_version;
    END IF;
END
$prepare_local_administrator_recovery$;

REVOKE ALL ON FUNCTION gateway_auth.prepare_local_administrator_recovery(uuid, uuid, text, timestamptz)
    FROM PUBLIC;
GRANT EXECUTE ON FUNCTION gateway_auth.prepare_local_administrator_recovery(uuid, uuid, text, timestamptz)
    TO assetlibrary_gateway_auth_runtime;
