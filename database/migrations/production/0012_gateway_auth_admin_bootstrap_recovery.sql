-- GatewayAuth owns one-time administrator bootstrap and out-of-band recovery state.
CREATE TABLE gateway_auth.administrator_bootstrap_recovery_operation (
    authorization_id uuid PRIMARY KEY
        CHECK (authorization_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    operation_id uuid NOT NULL UNIQUE
        CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    action text NOT NULL
        CHECK (action IN ('bootstrap_first_administrator', 'recover_administrator')),
    target_account_name text NOT NULL
        CHECK (
            length(target_account_name) BETWEEN 3 AND 64
            AND target_account_name ~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
        ),
    authorization_expires_at timestamptz NOT NULL,
    requested_display_name text,
    expected_credential_version bigint,
    outcome text NOT NULL
        CHECK (outcome IN ('applied', 'state_conflict')),
    result_principal_id uuid
        REFERENCES gateway_auth.authenticated_principal(principal_id),
    result_display_name text,
    result_is_system_administrator boolean,
    result_is_enabled boolean,
    result_credential_version bigint,
    result_session_version bigint,
    occurred_at timestamptz NOT NULL,
    CHECK (authorization_expires_at > occurred_at),
    CHECK (
        (
            action = 'bootstrap_first_administrator'
            AND requested_display_name IS NOT NULL
            AND expected_credential_version IS NULL
        )
        OR (
            action = 'recover_administrator'
            AND requested_display_name IS NULL
            AND expected_credential_version > 0
        )
    ),
    CHECK (result_credential_version IS NULL OR result_credential_version > 0),
    CHECK (result_session_version IS NULL OR result_session_version > 0),
    CHECK (
        (result_principal_id IS NULL) = (result_display_name IS NULL)
        AND (result_principal_id IS NULL) = (result_is_system_administrator IS NULL)
        AND (result_principal_id IS NULL) = (result_is_enabled IS NULL)
        AND (result_principal_id IS NULL) = (result_credential_version IS NULL)
        AND (result_principal_id IS NULL) = (result_session_version IS NULL)
    ),
    CHECK (
        (outcome = 'applied' AND result_principal_id IS NOT NULL)
        OR (outcome = 'state_conflict' AND result_principal_id IS NULL)
    )
);

CREATE INDEX administrator_bootstrap_recovery_target_time_index
    ON gateway_auth.administrator_bootstrap_recovery_operation (
        target_account_name,
        occurred_at DESC
    );

REVOKE ALL ON gateway_auth.administrator_bootstrap_recovery_operation
    FROM assetlibrary_gateway_auth_runtime;

CREATE FUNCTION gateway_auth.administrator_bootstrap_recovery_request_matches(
    recorded gateway_auth.administrator_bootstrap_recovery_operation,
    requested_authorization_id uuid,
    requested_operation_id uuid,
    requested_action text,
    requested_account_name text,
    requested_authorization_expires_at timestamptz,
    requested_display_name text,
    requested_expected_credential_version bigint
) RETURNS boolean
LANGUAGE sql
IMMUTABLE
SET search_path = pg_catalog, gateway_auth
AS $administrator_bootstrap_recovery_request_matches$
    SELECT
        ($1).authorization_id = $2
        AND ($1).operation_id = $3
        AND ($1).action = $4
        AND ($1).target_account_name = $5
        AND ($1).authorization_expires_at = $6
        AND ($1).requested_display_name IS NOT DISTINCT FROM $7
        AND ($1).expected_credential_version IS NOT DISTINCT FROM $8;
$administrator_bootstrap_recovery_request_matches$;

CREATE FUNCTION gateway_auth.bootstrap_first_local_administrator(
    requested_authorization_id uuid,
    requested_operation_id uuid,
    requested_account_name text,
    requested_authorization_expires_at timestamptz,
    requested_principal_id uuid,
    requested_display_name text,
    requested_secret_algorithm text,
    requested_secret_iterations integer,
    requested_secret_salt bytea,
    requested_secret_digest bytea
) RETURNS TABLE (
    outcome text,
    was_replayed boolean,
    principal_id uuid,
    account_name text,
    display_name text,
    is_system_administrator boolean,
    is_enabled boolean,
    credential_version bigint,
    principal_session_version bigint
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $bootstrap_first_local_administrator$
DECLARE
    observed_at timestamptz;
    previous gateway_auth.administrator_bootstrap_recovery_operation%ROWTYPE;
BEGIN
    IF requested_authorization_id IS NULL
       OR requested_authorization_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_operation_id IS NULL
       OR requested_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
       OR requested_authorization_expires_at IS NULL
       OR requested_principal_id IS NULL
       OR requested_principal_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_display_name IS NULL
       OR length(requested_display_name) NOT BETWEEN 1 AND 200
       OR requested_display_name <> btrim(requested_display_name)
       OR requested_display_name ~ '[[:cntrl:]]'
       OR requested_secret_algorithm IS NULL
       OR requested_secret_algorithm <> 'pbkdf2-sha256'
       OR requested_secret_iterations IS NULL
       OR requested_secret_iterations NOT BETWEEN 600000 AND 2000000
       OR requested_secret_salt IS NULL
       OR octet_length(requested_secret_salt) <> 16
       OR requested_secret_digest IS NULL
       OR octet_length(requested_secret_digest) <> 32 THEN
        RAISE EXCEPTION USING
            ERRCODE = '22023',
            MESSAGE = 'administrator bootstrap input is invalid';
    END IF;

    PERFORM pg_catalog.pg_advisory_xact_lock(109551001012);
    observed_at := clock_timestamp();
    IF requested_authorization_expires_at <= observed_at THEN
        RETURN QUERY SELECT
            'authorization_rejected'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    SELECT operation.*
    INTO previous
    FROM gateway_auth.administrator_bootstrap_recovery_operation AS operation
    WHERE operation.authorization_id = requested_authorization_id
       OR operation.operation_id = requested_operation_id
    ORDER BY (operation.authorization_id = requested_authorization_id) DESC
    LIMIT 1;
    IF FOUND THEN
        IF NOT gateway_auth.administrator_bootstrap_recovery_request_matches(
            previous,
            requested_authorization_id,
            requested_operation_id,
            'bootstrap_first_administrator',
            requested_account_name,
            requested_authorization_expires_at,
            requested_display_name,
            NULL::bigint
        ) THEN
            RETURN QUERY SELECT
                'request_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
                NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
            RETURN;
        END IF;

        RETURN QUERY SELECT
            previous.outcome,
            true,
            previous.result_principal_id,
            previous.target_account_name,
            previous.result_display_name,
            previous.result_is_system_administrator,
            previous.result_is_enabled,
            previous.result_credential_version,
            previous.result_session_version;
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM gateway_auth.authenticated_principal AS principal
        WHERE principal.is_system_administrator
          AND principal.subject_key ~ '^local:'
    ) OR EXISTS (
        SELECT 1
        FROM gateway_auth.authenticated_principal AS principal
        LEFT JOIN gateway_auth.local_account_credential AS credential
          ON credential.principal_id = principal.principal_id
        WHERE principal.principal_id = requested_principal_id
           OR principal.subject_key = 'local:' || requested_account_name
           OR credential.account_name = requested_account_name
    ) THEN
        INSERT INTO gateway_auth.administrator_bootstrap_recovery_operation (
            authorization_id,
            operation_id,
            action,
            target_account_name,
            authorization_expires_at,
            requested_display_name,
            outcome,
            occurred_at
        ) VALUES (
            requested_authorization_id,
            requested_operation_id,
            'bootstrap_first_administrator',
            requested_account_name,
            requested_authorization_expires_at,
            requested_display_name,
            'state_conflict',
            observed_at
        );
        RETURN QUERY SELECT
            'state_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    BEGIN
        INSERT INTO gateway_auth.authenticated_principal (
            principal_id,
            subject_key,
            display_name,
            is_system_administrator,
            session_version,
            created_at,
            disabled_at
        ) VALUES (
            requested_principal_id,
            'local:' || requested_account_name,
            requested_display_name,
            true,
            1,
            observed_at,
            NULL
        );
        INSERT INTO gateway_auth.local_account_credential (
            principal_id,
            account_name,
            secret_algorithm,
            secret_iterations,
            secret_salt,
            secret_digest,
            credential_version,
            failed_attempt_count,
            retry_not_before,
            created_at,
            changed_at,
            disabled_at
        ) VALUES (
            requested_principal_id,
            requested_account_name,
            requested_secret_algorithm,
            requested_secret_iterations,
            requested_secret_salt,
            requested_secret_digest,
            1,
            0,
            NULL,
            observed_at,
            observed_at,
            NULL
        );
    EXCEPTION WHEN unique_violation THEN
        INSERT INTO gateway_auth.administrator_bootstrap_recovery_operation (
            authorization_id,
            operation_id,
            action,
            target_account_name,
            authorization_expires_at,
            requested_display_name,
            outcome,
            occurred_at
        ) VALUES (
            requested_authorization_id,
            requested_operation_id,
            'bootstrap_first_administrator',
            requested_account_name,
            requested_authorization_expires_at,
            requested_display_name,
            'state_conflict',
            observed_at
        );
        RETURN QUERY SELECT
            'state_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END;

    INSERT INTO gateway_auth.administrator_bootstrap_recovery_operation (
        authorization_id,
        operation_id,
        action,
        target_account_name,
        authorization_expires_at,
        requested_display_name,
        outcome,
        result_principal_id,
        result_display_name,
        result_is_system_administrator,
        result_is_enabled,
        result_credential_version,
        result_session_version,
        occurred_at
    ) VALUES (
        requested_authorization_id,
        requested_operation_id,
        'bootstrap_first_administrator',
        requested_account_name,
        requested_authorization_expires_at,
        requested_display_name,
        'applied',
        requested_principal_id,
        requested_display_name,
        true,
        true,
        1,
        1,
        observed_at
    );
    RETURN QUERY SELECT
        'applied'::text,
        false,
        requested_principal_id,
        requested_account_name,
        requested_display_name,
        true,
        true,
        1::bigint,
        1::bigint;
END
$bootstrap_first_local_administrator$;

CREATE FUNCTION gateway_auth.recover_local_administrator(
    requested_authorization_id uuid,
    requested_operation_id uuid,
    requested_account_name text,
    requested_authorization_expires_at timestamptz,
    expected_credential_version bigint,
    requested_secret_algorithm text,
    requested_secret_iterations integer,
    requested_secret_salt bytea,
    requested_secret_digest bytea
) RETURNS TABLE (
    outcome text,
    was_replayed boolean,
    principal_id uuid,
    account_name text,
    display_name text,
    is_system_administrator boolean,
    is_enabled boolean,
    credential_version bigint,
    principal_session_version bigint
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $recover_local_administrator$
DECLARE
    observed_at timestamptz;
    previous gateway_auth.administrator_bootstrap_recovery_operation%ROWTYPE;
    target_principal_id uuid;
    target_display_name text;
    target_credential_version bigint;
    target_session_version bigint;
BEGIN
    IF requested_authorization_id IS NULL
       OR requested_authorization_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_operation_id IS NULL
       OR requested_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
       OR requested_authorization_expires_at IS NULL
       OR expected_credential_version IS NULL
       OR expected_credential_version <= 0
       OR requested_secret_algorithm IS NULL
       OR requested_secret_algorithm <> 'pbkdf2-sha256'
       OR requested_secret_iterations IS NULL
       OR requested_secret_iterations NOT BETWEEN 600000 AND 2000000
       OR requested_secret_salt IS NULL
       OR octet_length(requested_secret_salt) <> 16
       OR requested_secret_digest IS NULL
       OR octet_length(requested_secret_digest) <> 32 THEN
        RAISE EXCEPTION USING
            ERRCODE = '22023',
            MESSAGE = 'administrator recovery input is invalid';
    END IF;

    PERFORM pg_catalog.pg_advisory_xact_lock(109551001012);
    observed_at := clock_timestamp();
    IF requested_authorization_expires_at <= observed_at THEN
        RETURN QUERY SELECT
            'authorization_rejected'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    SELECT operation.*
    INTO previous
    FROM gateway_auth.administrator_bootstrap_recovery_operation AS operation
    WHERE operation.authorization_id = requested_authorization_id
       OR operation.operation_id = requested_operation_id
    ORDER BY (operation.authorization_id = requested_authorization_id) DESC
    LIMIT 1;
    IF FOUND THEN
        IF NOT gateway_auth.administrator_bootstrap_recovery_request_matches(
            previous,
            requested_authorization_id,
            requested_operation_id,
            'recover_administrator',
            requested_account_name,
            requested_authorization_expires_at,
            NULL::text,
            expected_credential_version
        ) THEN
            RETURN QUERY SELECT
                'request_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
                NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
            RETURN;
        END IF;

        RETURN QUERY SELECT
            previous.outcome,
            true,
            previous.result_principal_id,
            previous.target_account_name,
            previous.result_display_name,
            previous.result_is_system_administrator,
            previous.result_is_enabled,
            previous.result_credential_version,
            previous.result_session_version;
        RETURN;
    END IF;

    SELECT
        principal.principal_id,
        principal.display_name,
        credential.credential_version,
        principal.session_version
    INTO
        target_principal_id,
        target_display_name,
        target_credential_version,
        target_session_version
    FROM gateway_auth.local_account_credential AS credential
    JOIN gateway_auth.authenticated_principal AS principal
      ON principal.principal_id = credential.principal_id
    WHERE credential.account_name = requested_account_name
      AND principal.subject_key = 'local:' || requested_account_name
      AND principal.is_system_administrator
    FOR UPDATE OF principal, credential;

    IF NOT FOUND OR target_credential_version <> expected_credential_version THEN
        INSERT INTO gateway_auth.administrator_bootstrap_recovery_operation (
            authorization_id,
            operation_id,
            action,
            target_account_name,
            authorization_expires_at,
            expected_credential_version,
            outcome,
            occurred_at
        ) VALUES (
            requested_authorization_id,
            requested_operation_id,
            'recover_administrator',
            requested_account_name,
            requested_authorization_expires_at,
            expected_credential_version,
            'state_conflict',
            observed_at
        );
        RETURN QUERY SELECT
            'state_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    UPDATE gateway_auth.local_account_credential AS credential
    SET
        secret_algorithm = requested_secret_algorithm,
        secret_iterations = requested_secret_iterations,
        secret_salt = requested_secret_salt,
        secret_digest = requested_secret_digest,
        credential_version = credential.credential_version + 1,
        failed_attempt_count = 0,
        retry_not_before = NULL,
        changed_at = observed_at,
        disabled_at = NULL
    WHERE credential.principal_id = target_principal_id;

    UPDATE gateway_auth.authenticated_principal AS principal
    SET
        session_version = principal.session_version + 1,
        disabled_at = NULL
    WHERE principal.principal_id = target_principal_id
    RETURNING principal.session_version
    INTO target_session_version;
    target_credential_version := target_credential_version + 1;

    UPDATE gateway_auth.browser_session AS session
    SET revoked_at = observed_at
    WHERE session.principal_id = target_principal_id
      AND session.revoked_at IS NULL;

    INSERT INTO gateway_auth.administrator_bootstrap_recovery_operation (
        authorization_id,
        operation_id,
        action,
        target_account_name,
        authorization_expires_at,
        expected_credential_version,
        outcome,
        result_principal_id,
        result_display_name,
        result_is_system_administrator,
        result_is_enabled,
        result_credential_version,
        result_session_version,
        occurred_at
    ) VALUES (
        requested_authorization_id,
        requested_operation_id,
        'recover_administrator',
        requested_account_name,
        requested_authorization_expires_at,
        expected_credential_version,
        'applied',
        target_principal_id,
        target_display_name,
        true,
        true,
        target_credential_version,
        target_session_version,
        observed_at
    );
    RETURN QUERY SELECT
        'applied'::text,
        false,
        target_principal_id,
        requested_account_name,
        target_display_name,
        true,
        true,
        target_credential_version,
        target_session_version;
END
$recover_local_administrator$;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA gateway_auth FROM PUBLIC;
REVOKE ALL ON FUNCTION
    gateway_auth.administrator_bootstrap_recovery_request_matches(
        gateway_auth.administrator_bootstrap_recovery_operation,
        uuid,
        uuid,
        text,
        text,
        timestamptz,
        text,
        bigint
    ) FROM assetlibrary_gateway_auth_runtime;
GRANT EXECUTE ON
    FUNCTION gateway_auth.bootstrap_first_local_administrator(
        uuid, uuid, text, timestamptz, uuid, text, text, integer, bytea, bytea
    ),
    gateway_auth.recover_local_administrator(
        uuid, uuid, text, timestamptz, bigint, text, integer, bytea, bytea
    )
    TO assetlibrary_gateway_auth_runtime;
