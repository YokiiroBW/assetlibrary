-- GatewayAuth owns administrator-authorized local account lifecycle and its audit trail.
CREATE TABLE gateway_auth.local_account_lifecycle_operation (
    operation_id uuid PRIMARY KEY
        CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    action text NOT NULL
        CHECK (action IN ('provision', 'replace_credential', 'set_enabled')),
    actor_principal_id uuid NOT NULL
        REFERENCES gateway_auth.authenticated_principal(principal_id),
    target_account_name text NOT NULL
        CHECK (
            length(target_account_name) BETWEEN 3 AND 64
            AND target_account_name ~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
        ),
    requested_display_name text,
    requested_is_system_administrator boolean,
    expected_credential_version bigint,
    expected_session_version bigint,
    requested_enabled boolean,
    outcome text NOT NULL
        CHECK (
            outcome IN (
                'applied',
                'account_conflict',
                'not_found',
                'state_conflict',
                'last_administrator'
            )
        ),
    result_principal_id uuid
        REFERENCES gateway_auth.authenticated_principal(principal_id),
    result_display_name text,
    result_is_system_administrator boolean,
    result_is_enabled boolean,
    result_credential_version bigint,
    result_session_version bigint,
    occurred_at timestamptz NOT NULL,
    CHECK (expected_credential_version IS NULL OR expected_credential_version > 0),
    CHECK (expected_session_version IS NULL OR expected_session_version > 0),
    CHECK (result_credential_version IS NULL OR result_credential_version > 0),
    CHECK (result_session_version IS NULL OR result_session_version > 0),
    CHECK (
        (result_principal_id IS NULL) = (result_display_name IS NULL)
        AND (result_principal_id IS NULL) = (result_is_system_administrator IS NULL)
        AND (result_principal_id IS NULL) = (result_is_enabled IS NULL)
        AND (result_principal_id IS NULL) = (result_credential_version IS NULL)
        AND (result_principal_id IS NULL) = (result_session_version IS NULL)
    )
);

CREATE INDEX local_account_lifecycle_actor_time_index
    ON gateway_auth.local_account_lifecycle_operation (actor_principal_id, occurred_at DESC);

CREATE INDEX local_account_lifecycle_target_time_index
    ON gateway_auth.local_account_lifecycle_operation (target_account_name, occurred_at DESC);

REVOKE ALL ON gateway_auth.local_account_lifecycle_operation
    FROM assetlibrary_gateway_auth_runtime;

CREATE FUNCTION gateway_auth.local_account_actor_is_authorized(
    requested_actor_principal_id uuid,
    observed_actor_session_version bigint
) RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $local_account_actor_is_authorized$
    SELECT EXISTS (
        SELECT 1
        FROM gateway_auth.authenticated_principal AS actor
        WHERE actor.principal_id = requested_actor_principal_id
          AND actor.session_version = observed_actor_session_version
          AND actor.is_system_administrator
          AND actor.disabled_at IS NULL
          AND (
              actor.subject_key !~ '^local:'
              OR EXISTS (
                  SELECT 1
                  FROM gateway_auth.local_account_credential AS actor_credential
                  WHERE actor_credential.principal_id = actor.principal_id
                    AND actor_credential.disabled_at IS NULL
              )
          )
    );
$local_account_actor_is_authorized$;

CREATE FUNCTION gateway_auth.read_local_account_for_administrator(
    requested_actor_principal_id uuid,
    observed_actor_session_version bigint,
    requested_account_name text
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
AS $read_local_account_for_administrator$
BEGIN
    IF requested_actor_principal_id IS NULL
       OR requested_actor_principal_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR observed_actor_session_version IS NULL
       OR observed_actor_session_version <= 0
       OR requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'local account read input is invalid';
    END IF;

    IF NOT gateway_auth.local_account_actor_is_authorized(
        requested_actor_principal_id,
        observed_actor_session_version
    ) THEN
        RETURN QUERY SELECT
            'unauthorized'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    RETURN QUERY
    SELECT
        'applied'::text,
        false,
        principal.principal_id,
        credential.account_name,
        principal.display_name,
        principal.is_system_administrator,
        principal.disabled_at IS NULL AND credential.disabled_at IS NULL,
        credential.credential_version,
        principal.session_version
    FROM gateway_auth.local_account_credential AS credential
    JOIN gateway_auth.authenticated_principal AS principal
      ON principal.principal_id = credential.principal_id
    WHERE credential.account_name = requested_account_name;

    IF NOT FOUND THEN
        RETURN QUERY SELECT
            'not_found'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
    END IF;
END
$read_local_account_for_administrator$;

CREATE FUNCTION gateway_auth.provision_local_account(
    requested_actor_principal_id uuid,
    observed_actor_session_version bigint,
    requested_operation_id uuid,
    requested_principal_id uuid,
    requested_account_name text,
    requested_display_name text,
    requested_is_system_administrator boolean,
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
AS $provision_local_account$
DECLARE
    observed_at timestamptz := clock_timestamp();
    previous gateway_auth.local_account_lifecycle_operation%ROWTYPE;
    current_principal_id uuid;
    current_display_name text;
    current_is_administrator boolean;
    current_is_enabled boolean;
    current_credential_version bigint;
    current_session_version bigint;
BEGIN
    IF requested_actor_principal_id IS NULL
       OR requested_actor_principal_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR observed_actor_session_version IS NULL
       OR observed_actor_session_version <= 0
       OR requested_operation_id IS NULL
       OR requested_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_principal_id IS NULL
       OR requested_principal_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
       OR requested_display_name IS NULL
       OR length(requested_display_name) NOT BETWEEN 1 AND 200
       OR requested_display_name <> btrim(requested_display_name)
       OR requested_display_name ~ '[[:cntrl:]]'
       OR requested_is_system_administrator IS NULL
       OR requested_secret_algorithm IS NULL
       OR requested_secret_algorithm <> 'pbkdf2-sha256'
       OR requested_secret_iterations IS NULL
       OR requested_secret_iterations NOT BETWEEN 600000 AND 2000000
       OR requested_secret_salt IS NULL
       OR octet_length(requested_secret_salt) NOT BETWEEN 16 AND 64
       OR requested_secret_digest IS NULL
       OR octet_length(requested_secret_digest) <> 32 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'local account provision input is invalid';
    END IF;

    IF NOT gateway_auth.local_account_actor_is_authorized(
        requested_actor_principal_id,
        observed_actor_session_version
    ) THEN
        RETURN QUERY SELECT
            'unauthorized'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    PERFORM pg_catalog.pg_advisory_xact_lock(109551001012);
    IF NOT gateway_auth.local_account_actor_is_authorized(
        requested_actor_principal_id,
        observed_actor_session_version
    ) THEN
        RETURN QUERY SELECT
            'unauthorized'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    SELECT operation.*
    INTO previous
    FROM gateway_auth.local_account_lifecycle_operation AS operation
    WHERE operation.operation_id = requested_operation_id;
    IF FOUND THEN
        IF previous.action <> 'provision'
           OR previous.actor_principal_id <> requested_actor_principal_id
           OR previous.target_account_name <> requested_account_name
           OR previous.requested_display_name IS DISTINCT FROM requested_display_name
           OR previous.requested_is_system_administrator IS DISTINCT FROM requested_is_system_administrator THEN
            RETURN QUERY SELECT
                'operation_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
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
        principal.is_system_administrator,
        principal.disabled_at IS NULL AND credential.disabled_at IS NULL,
        credential.credential_version,
        principal.session_version
    INTO
        current_principal_id,
        current_display_name,
        current_is_administrator,
        current_is_enabled,
        current_credential_version,
        current_session_version
    FROM gateway_auth.authenticated_principal AS principal
    LEFT JOIN gateway_auth.local_account_credential AS credential
      ON credential.principal_id = principal.principal_id
    WHERE principal.subject_key = 'local:' || requested_account_name
       OR credential.account_name = requested_account_name
    ORDER BY principal.principal_id
    LIMIT 1;

    IF FOUND THEN
        IF current_credential_version IS NULL THEN
            INSERT INTO gateway_auth.local_account_lifecycle_operation (
                operation_id, action, actor_principal_id, target_account_name,
                requested_display_name, requested_is_system_administrator, outcome, occurred_at
            ) VALUES (
                requested_operation_id, 'provision', requested_actor_principal_id,
                requested_account_name, requested_display_name,
                requested_is_system_administrator, 'account_conflict', observed_at
            );
            RETURN QUERY SELECT
                'account_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
                NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        ELSE
            INSERT INTO gateway_auth.local_account_lifecycle_operation (
                operation_id, action, actor_principal_id, target_account_name,
                requested_display_name, requested_is_system_administrator, outcome,
                result_principal_id, result_display_name, result_is_system_administrator,
                result_is_enabled, result_credential_version, result_session_version, occurred_at
            ) VALUES (
                requested_operation_id, 'provision', requested_actor_principal_id,
                requested_account_name, requested_display_name,
                requested_is_system_administrator, 'account_conflict', current_principal_id,
                current_display_name, current_is_administrator, current_is_enabled,
                current_credential_version, current_session_version, observed_at
            );
            RETURN QUERY SELECT
                'account_conflict'::text, false, current_principal_id, requested_account_name,
                current_display_name, current_is_administrator, current_is_enabled,
                current_credential_version, current_session_version;
        END IF;
        RETURN;
    END IF;

    BEGIN
        INSERT INTO gateway_auth.authenticated_principal (
            principal_id, subject_key, display_name, is_system_administrator,
            session_version, created_at, disabled_at
        ) VALUES (
            requested_principal_id, 'local:' || requested_account_name,
            requested_display_name, requested_is_system_administrator,
            1, observed_at, NULL
        );
        INSERT INTO gateway_auth.local_account_credential (
            principal_id, account_name, secret_algorithm, secret_iterations,
            secret_salt, secret_digest, credential_version, failed_attempt_count,
            retry_not_before, created_at, changed_at, disabled_at
        ) VALUES (
            requested_principal_id, requested_account_name, requested_secret_algorithm,
            requested_secret_iterations, requested_secret_salt, requested_secret_digest,
            1, 0, NULL, observed_at, observed_at, NULL
        );
    EXCEPTION WHEN unique_violation THEN
        INSERT INTO gateway_auth.local_account_lifecycle_operation (
            operation_id, action, actor_principal_id, target_account_name,
            requested_display_name, requested_is_system_administrator, outcome, occurred_at
        ) VALUES (
            requested_operation_id, 'provision', requested_actor_principal_id,
            requested_account_name, requested_display_name,
            requested_is_system_administrator, 'account_conflict', observed_at
        );
        RETURN QUERY SELECT
            'account_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END;

    INSERT INTO gateway_auth.local_account_lifecycle_operation (
        operation_id, action, actor_principal_id, target_account_name,
        requested_display_name, requested_is_system_administrator, outcome,
        result_principal_id, result_display_name, result_is_system_administrator,
        result_is_enabled, result_credential_version, result_session_version, occurred_at
    ) VALUES (
        requested_operation_id, 'provision', requested_actor_principal_id,
        requested_account_name, requested_display_name,
        requested_is_system_administrator, 'applied', requested_principal_id,
        requested_display_name, requested_is_system_administrator, true, 1, 1, observed_at
    );
    RETURN QUERY SELECT
        'applied'::text, false, requested_principal_id, requested_account_name,
        requested_display_name, requested_is_system_administrator, true, 1::bigint, 1::bigint;
END
$provision_local_account$;

CREATE FUNCTION gateway_auth.replace_local_account_credential(
    requested_actor_principal_id uuid,
    observed_actor_session_version bigint,
    requested_operation_id uuid,
    requested_account_name text,
    expected_credential_version bigint,
    enable_account boolean,
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
AS $replace_local_account_credential$
DECLARE
    observed_at timestamptz := clock_timestamp();
    previous gateway_auth.local_account_lifecycle_operation%ROWTYPE;
    target_principal_id uuid;
    target_display_name text;
    target_is_administrator boolean;
    target_is_enabled boolean;
    target_credential_version bigint;
    target_session_version bigint;
BEGIN
    IF requested_actor_principal_id IS NULL
       OR requested_actor_principal_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR observed_actor_session_version IS NULL
       OR observed_actor_session_version <= 0
       OR requested_operation_id IS NULL
       OR requested_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
       OR expected_credential_version IS NULL
       OR expected_credential_version <= 0
       OR enable_account IS NULL
       OR requested_secret_algorithm IS NULL
       OR requested_secret_algorithm <> 'pbkdf2-sha256'
       OR requested_secret_iterations IS NULL
       OR requested_secret_iterations NOT BETWEEN 600000 AND 2000000
       OR requested_secret_salt IS NULL
       OR octet_length(requested_secret_salt) NOT BETWEEN 16 AND 64
       OR requested_secret_digest IS NULL
       OR octet_length(requested_secret_digest) <> 32 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'local credential replacement input is invalid';
    END IF;

    IF NOT gateway_auth.local_account_actor_is_authorized(
        requested_actor_principal_id,
        observed_actor_session_version
    ) THEN
        RETURN QUERY SELECT
            'unauthorized'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    PERFORM pg_catalog.pg_advisory_xact_lock(109551001012);
    IF NOT gateway_auth.local_account_actor_is_authorized(
        requested_actor_principal_id,
        observed_actor_session_version
    ) THEN
        RETURN QUERY SELECT
            'unauthorized'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    SELECT operation.*
    INTO previous
    FROM gateway_auth.local_account_lifecycle_operation AS operation
    WHERE operation.operation_id = requested_operation_id;
    IF FOUND THEN
        IF previous.action <> 'replace_credential'
           OR previous.actor_principal_id <> requested_actor_principal_id
           OR previous.target_account_name <> requested_account_name
           OR previous.expected_credential_version IS DISTINCT FROM expected_credential_version
           OR previous.requested_enabled IS DISTINCT FROM enable_account THEN
            RETURN QUERY SELECT
                'operation_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
                NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
            RETURN;
        END IF;

        RETURN QUERY SELECT
            previous.outcome, true, previous.result_principal_id,
            previous.target_account_name, previous.result_display_name,
            previous.result_is_system_administrator, previous.result_is_enabled,
            previous.result_credential_version, previous.result_session_version;
        RETURN;
    END IF;

    SELECT
        principal.principal_id,
        principal.display_name,
        principal.is_system_administrator,
        principal.disabled_at IS NULL AND credential.disabled_at IS NULL,
        credential.credential_version,
        principal.session_version
    INTO
        target_principal_id,
        target_display_name,
        target_is_administrator,
        target_is_enabled,
        target_credential_version,
        target_session_version
    FROM gateway_auth.local_account_credential AS credential
    JOIN gateway_auth.authenticated_principal AS principal
      ON principal.principal_id = credential.principal_id
    WHERE credential.account_name = requested_account_name
    FOR UPDATE OF principal, credential;

    IF NOT FOUND THEN
        INSERT INTO gateway_auth.local_account_lifecycle_operation (
            operation_id, action, actor_principal_id, target_account_name,
            expected_credential_version, requested_enabled, outcome, occurred_at
        ) VALUES (
            requested_operation_id, 'replace_credential', requested_actor_principal_id,
            requested_account_name, expected_credential_version, enable_account,
            'not_found', observed_at
        );
        RETURN QUERY SELECT
            'not_found'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    IF target_credential_version <> expected_credential_version THEN
        INSERT INTO gateway_auth.local_account_lifecycle_operation (
            operation_id, action, actor_principal_id, target_account_name,
            expected_credential_version, requested_enabled, outcome,
            result_principal_id, result_display_name, result_is_system_administrator,
            result_is_enabled, result_credential_version, result_session_version, occurred_at
        ) VALUES (
            requested_operation_id, 'replace_credential', requested_actor_principal_id,
            requested_account_name, expected_credential_version, enable_account,
            'state_conflict', target_principal_id, target_display_name,
            target_is_administrator, target_is_enabled, target_credential_version,
            target_session_version, observed_at
        );
        RETURN QUERY SELECT
            'state_conflict'::text, false, target_principal_id, requested_account_name,
            target_display_name, target_is_administrator, target_is_enabled,
            target_credential_version, target_session_version;
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
        disabled_at = CASE WHEN enable_account THEN NULL ELSE credential.disabled_at END
    WHERE credential.principal_id = target_principal_id;

    UPDATE gateway_auth.authenticated_principal AS principal
    SET
        session_version = principal.session_version + 1,
        disabled_at = CASE WHEN enable_account THEN NULL ELSE principal.disabled_at END
    WHERE principal.principal_id = target_principal_id
    RETURNING principal.session_version
    INTO target_session_version;
    target_credential_version := target_credential_version + 1;
    target_is_enabled := enable_account OR target_is_enabled;

    UPDATE gateway_auth.browser_session AS session
    SET revoked_at = observed_at
    WHERE session.principal_id = target_principal_id
      AND session.revoked_at IS NULL;

    INSERT INTO gateway_auth.local_account_lifecycle_operation (
        operation_id, action, actor_principal_id, target_account_name,
        expected_credential_version, requested_enabled, outcome,
        result_principal_id, result_display_name, result_is_system_administrator,
        result_is_enabled, result_credential_version, result_session_version, occurred_at
    ) VALUES (
        requested_operation_id, 'replace_credential', requested_actor_principal_id,
        requested_account_name, expected_credential_version, enable_account,
        'applied', target_principal_id, target_display_name, target_is_administrator,
        target_is_enabled, target_credential_version, target_session_version, observed_at
    );
    RETURN QUERY SELECT
        'applied'::text, false, target_principal_id, requested_account_name,
        target_display_name, target_is_administrator, target_is_enabled,
        target_credential_version, target_session_version;
END
$replace_local_account_credential$;

CREATE FUNCTION gateway_auth.set_local_account_enabled(
    requested_actor_principal_id uuid,
    observed_actor_session_version bigint,
    requested_operation_id uuid,
    requested_account_name text,
    expected_principal_session_version bigint,
    requested_enabled boolean
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
AS $set_local_account_enabled$
DECLARE
    observed_at timestamptz := clock_timestamp();
    previous gateway_auth.local_account_lifecycle_operation%ROWTYPE;
    target_principal_id uuid;
    target_display_name text;
    target_is_administrator boolean;
    target_is_enabled boolean;
    target_credential_version bigint;
    target_session_version bigint;
BEGIN
    IF requested_actor_principal_id IS NULL
       OR requested_actor_principal_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR observed_actor_session_version IS NULL
       OR observed_actor_session_version <= 0
       OR requested_operation_id IS NULL
       OR requested_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
       OR expected_principal_session_version IS NULL
       OR expected_principal_session_version <= 0
       OR requested_enabled IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'local account enabled-state input is invalid';
    END IF;

    IF NOT gateway_auth.local_account_actor_is_authorized(
        requested_actor_principal_id,
        observed_actor_session_version
    ) THEN
        RETURN QUERY SELECT
            'unauthorized'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    PERFORM pg_catalog.pg_advisory_xact_lock(109551001012);
    IF NOT gateway_auth.local_account_actor_is_authorized(
        requested_actor_principal_id,
        observed_actor_session_version
    ) THEN
        RETURN QUERY SELECT
            'unauthorized'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    SELECT operation.*
    INTO previous
    FROM gateway_auth.local_account_lifecycle_operation AS operation
    WHERE operation.operation_id = requested_operation_id;
    IF FOUND THEN
        IF previous.action <> 'set_enabled'
           OR previous.actor_principal_id <> requested_actor_principal_id
           OR previous.target_account_name <> requested_account_name
           OR previous.expected_session_version IS DISTINCT FROM expected_principal_session_version
           OR previous.requested_enabled IS DISTINCT FROM requested_enabled THEN
            RETURN QUERY SELECT
                'operation_conflict'::text, false, NULL::uuid, NULL::text, NULL::text,
                NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
            RETURN;
        END IF;

        RETURN QUERY SELECT
            previous.outcome, true, previous.result_principal_id,
            previous.target_account_name, previous.result_display_name,
            previous.result_is_system_administrator, previous.result_is_enabled,
            previous.result_credential_version, previous.result_session_version;
        RETURN;
    END IF;

    SELECT
        principal.principal_id,
        principal.display_name,
        principal.is_system_administrator,
        principal.disabled_at IS NULL AND credential.disabled_at IS NULL,
        credential.credential_version,
        principal.session_version
    INTO
        target_principal_id,
        target_display_name,
        target_is_administrator,
        target_is_enabled,
        target_credential_version,
        target_session_version
    FROM gateway_auth.local_account_credential AS credential
    JOIN gateway_auth.authenticated_principal AS principal
      ON principal.principal_id = credential.principal_id
    WHERE credential.account_name = requested_account_name
    FOR UPDATE OF principal, credential;

    IF NOT FOUND THEN
        INSERT INTO gateway_auth.local_account_lifecycle_operation (
            operation_id, action, actor_principal_id, target_account_name,
            expected_session_version, requested_enabled, outcome, occurred_at
        ) VALUES (
            requested_operation_id, 'set_enabled', requested_actor_principal_id,
            requested_account_name, expected_principal_session_version,
            requested_enabled, 'not_found', observed_at
        );
        RETURN QUERY SELECT
            'not_found'::text, false, NULL::uuid, NULL::text, NULL::text,
            NULL::boolean, NULL::boolean, NULL::bigint, NULL::bigint;
        RETURN;
    END IF;

    IF target_session_version <> expected_principal_session_version
       OR target_is_enabled = requested_enabled THEN
        INSERT INTO gateway_auth.local_account_lifecycle_operation (
            operation_id, action, actor_principal_id, target_account_name,
            expected_session_version, requested_enabled, outcome,
            result_principal_id, result_display_name, result_is_system_administrator,
            result_is_enabled, result_credential_version, result_session_version, occurred_at
        ) VALUES (
            requested_operation_id, 'set_enabled', requested_actor_principal_id,
            requested_account_name, expected_principal_session_version,
            requested_enabled, 'state_conflict', target_principal_id,
            target_display_name, target_is_administrator, target_is_enabled,
            target_credential_version, target_session_version, observed_at
        );
        RETURN QUERY SELECT
            'state_conflict'::text, false, target_principal_id, requested_account_name,
            target_display_name, target_is_administrator, target_is_enabled,
            target_credential_version, target_session_version;
        RETURN;
    END IF;

    IF target_is_administrator
       AND target_is_enabled
       AND NOT requested_enabled
       AND NOT EXISTS (
           SELECT 1
           FROM gateway_auth.authenticated_principal AS administrator
           WHERE administrator.is_system_administrator
             AND administrator.disabled_at IS NULL
             AND administrator.principal_id <> target_principal_id
             AND (
                 administrator.subject_key !~ '^local:'
                 OR EXISTS (
                     SELECT 1
                     FROM gateway_auth.local_account_credential AS administrator_credential
                     WHERE administrator_credential.principal_id = administrator.principal_id
                       AND administrator_credential.disabled_at IS NULL
                 )
             )
       ) THEN
        INSERT INTO gateway_auth.local_account_lifecycle_operation (
            operation_id, action, actor_principal_id, target_account_name,
            expected_session_version, requested_enabled, outcome,
            result_principal_id, result_display_name, result_is_system_administrator,
            result_is_enabled, result_credential_version, result_session_version, occurred_at
        ) VALUES (
            requested_operation_id, 'set_enabled', requested_actor_principal_id,
            requested_account_name, expected_principal_session_version,
            requested_enabled, 'last_administrator', target_principal_id,
            target_display_name, target_is_administrator, target_is_enabled,
            target_credential_version, target_session_version, observed_at
        );
        RETURN QUERY SELECT
            'last_administrator'::text, false, target_principal_id, requested_account_name,
            target_display_name, target_is_administrator, target_is_enabled,
            target_credential_version, target_session_version;
        RETURN;
    END IF;

    UPDATE gateway_auth.local_account_credential AS credential
    SET disabled_at = CASE WHEN requested_enabled THEN NULL ELSE observed_at END
    WHERE credential.principal_id = target_principal_id;
    UPDATE gateway_auth.authenticated_principal AS principal
    SET
        disabled_at = CASE WHEN requested_enabled THEN NULL ELSE observed_at END,
        session_version = principal.session_version + 1
    WHERE principal.principal_id = target_principal_id
    RETURNING principal.session_version
    INTO target_session_version;
    target_is_enabled := requested_enabled;

    UPDATE gateway_auth.browser_session AS session
    SET revoked_at = observed_at
    WHERE session.principal_id = target_principal_id
      AND session.revoked_at IS NULL;

    INSERT INTO gateway_auth.local_account_lifecycle_operation (
        operation_id, action, actor_principal_id, target_account_name,
        expected_session_version, requested_enabled, outcome,
        result_principal_id, result_display_name, result_is_system_administrator,
        result_is_enabled, result_credential_version, result_session_version, occurred_at
    ) VALUES (
        requested_operation_id, 'set_enabled', requested_actor_principal_id,
        requested_account_name, expected_principal_session_version,
        requested_enabled, 'applied', target_principal_id, target_display_name,
        target_is_administrator, target_is_enabled, target_credential_version,
        target_session_version, observed_at
    );
    RETURN QUERY SELECT
        'applied'::text, false, target_principal_id, requested_account_name,
        target_display_name, target_is_administrator, target_is_enabled,
        target_credential_version, target_session_version;
END
$set_local_account_enabled$;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA gateway_auth FROM PUBLIC;
REVOKE ALL ON FUNCTION gateway_auth.local_account_actor_is_authorized(uuid, bigint)
    FROM assetlibrary_gateway_auth_runtime;
GRANT EXECUTE ON
    FUNCTION gateway_auth.read_local_account_for_administrator(uuid, bigint, text),
    gateway_auth.provision_local_account(
        uuid, bigint, uuid, uuid, text, text, boolean, text, integer, bytea, bytea
    ),
    gateway_auth.replace_local_account_credential(
        uuid, bigint, uuid, text, bigint, boolean, text, integer, bytea, bytea
    ),
    gateway_auth.set_local_account_enabled(uuid, bigint, uuid, text, bigint, boolean)
    TO assetlibrary_gateway_auth_runtime;
