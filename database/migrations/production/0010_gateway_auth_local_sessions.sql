-- GatewayAuth owns local credential verification state and server-side browser sessions.
ALTER TABLE gateway_auth.authenticated_principal
    ADD COLUMN session_version bigint NOT NULL DEFAULT 1
        CHECK (session_version > 0);

CREATE TABLE gateway_auth.local_account_credential (
    principal_id uuid PRIMARY KEY
        REFERENCES gateway_auth.authenticated_principal(principal_id) ON DELETE CASCADE,
    account_name text NOT NULL UNIQUE
        CHECK (
            length(account_name) BETWEEN 3 AND 64
            AND account_name ~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
        ),
    secret_algorithm text NOT NULL
        CHECK (secret_algorithm = 'pbkdf2-sha256'),
    secret_iterations integer NOT NULL
        CHECK (secret_iterations BETWEEN 600000 AND 2000000),
    secret_salt bytea NOT NULL
        CHECK (octet_length(secret_salt) BETWEEN 16 AND 64),
    secret_digest bytea NOT NULL
        CHECK (octet_length(secret_digest) = 32),
    credential_version bigint NOT NULL DEFAULT 1
        CHECK (credential_version > 0),
    failed_attempt_count integer NOT NULL DEFAULT 0
        CHECK (failed_attempt_count BETWEEN 0 AND 32),
    retry_not_before timestamptz,
    created_at timestamptz NOT NULL,
    changed_at timestamptz NOT NULL,
    disabled_at timestamptz,
    CHECK (changed_at >= created_at),
    CHECK (disabled_at IS NULL OR disabled_at >= created_at)
);

CREATE TABLE gateway_auth.browser_session (
    session_digest bytea PRIMARY KEY
        CHECK (octet_length(session_digest) = 32),
    csrf_digest bytea NOT NULL
        CHECK (octet_length(csrf_digest) = 32),
    principal_id uuid NOT NULL
        REFERENCES gateway_auth.authenticated_principal(principal_id) ON DELETE CASCADE,
    principal_session_version bigint NOT NULL
        CHECK (principal_session_version > 0),
    authentication_method text NOT NULL
        CHECK (authentication_method IN ('local', 'oidc')),
    local_credential_version bigint,
    issued_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,
    idle_expires_at timestamptz NOT NULL,
    absolute_expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    CHECK (last_seen_at >= issued_at),
    CHECK (idle_expires_at >= last_seen_at),
    CHECK (absolute_expires_at >= idle_expires_at),
    CHECK (absolute_expires_at <= issued_at + interval '12 hours'),
    CHECK (idle_expires_at <= last_seen_at + interval '30 minutes'),
    CHECK (revoked_at IS NULL OR revoked_at >= issued_at),
    CHECK (
        (
            authentication_method = 'local'
            AND local_credential_version IS NOT NULL
            AND local_credential_version > 0
        )
        OR (
            authentication_method = 'oidc'
            AND local_credential_version IS NULL
        )
    )
);

CREATE INDEX browser_session_principal_active_index
    ON gateway_auth.browser_session (principal_id, issued_at DESC)
    WHERE revoked_at IS NULL;

REVOKE ALL ON gateway_auth.local_account_credential,
    gateway_auth.browser_session
    FROM assetlibrary_gateway_auth_runtime;

CREATE FUNCTION gateway_auth.read_local_sign_in_material(
    requested_account_name text
) RETURNS TABLE (
    principal_id uuid,
    subject_key text,
    display_name text,
    is_system_administrator boolean,
    principal_session_version bigint,
    secret_algorithm text,
    secret_iterations integer,
    secret_salt bytea,
    secret_digest bytea,
    credential_version bigint,
    retry_not_before timestamptz,
    can_attempt boolean
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $read_local_sign_in_material$
BEGIN
    IF requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'local account name is invalid';
    END IF;

    RETURN QUERY
    SELECT
        principal.principal_id,
        principal.subject_key,
        principal.display_name,
        principal.is_system_administrator,
        principal.session_version,
        credential.secret_algorithm,
        credential.secret_iterations,
        credential.secret_salt,
        credential.secret_digest,
        credential.credential_version,
        credential.retry_not_before,
        credential.retry_not_before IS NULL OR credential.retry_not_before <= clock_timestamp()
    FROM gateway_auth.local_account_credential AS credential
    JOIN gateway_auth.authenticated_principal AS principal
      ON principal.principal_id = credential.principal_id
    WHERE credential.account_name = requested_account_name
      AND credential.disabled_at IS NULL
      AND principal.disabled_at IS NULL;
END
$read_local_sign_in_material$;

CREATE FUNCTION gateway_auth.record_local_sign_in_failure(
    requested_account_name text,
    observed_credential_version bigint
) RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $record_local_sign_in_failure$
DECLARE
    observed_at timestamptz := clock_timestamp();
    affected_rows integer;
BEGIN
    IF requested_account_name IS NULL
       OR length(requested_account_name) NOT BETWEEN 3 AND 64
       OR requested_account_name !~ '^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$'
       OR observed_credential_version IS NULL
       OR observed_credential_version <= 0 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'local sign-in failure input is invalid';
    END IF;

    UPDATE gateway_auth.local_account_credential AS credential
    SET
        failed_attempt_count = least(32, credential.failed_attempt_count + 1),
        retry_not_before = observed_at + make_interval(
            secs => least(
                300,
                power(2, least(9, credential.failed_attempt_count))::integer
            )
        )
    FROM gateway_auth.authenticated_principal AS principal
    WHERE credential.account_name = requested_account_name
      AND credential.credential_version = observed_credential_version
      AND credential.disabled_at IS NULL
      AND principal.principal_id = credential.principal_id
      AND principal.disabled_at IS NULL
      AND (credential.retry_not_before IS NULL OR credential.retry_not_before <= observed_at);
    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    RETURN affected_rows = 1;
END
$record_local_sign_in_failure$;

CREATE FUNCTION gateway_auth.create_browser_session(
    requested_principal_id uuid,
    observed_principal_session_version bigint,
    requested_authentication_method text,
    observed_local_credential_version bigint,
    requested_session_digest bytea,
    requested_csrf_digest bytea
) RETURNS TABLE (
    outcome text,
    issued_at timestamptz,
    idle_expires_at timestamptz,
    absolute_expires_at timestamptz
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $create_browser_session$
DECLARE
    observed_at timestamptz := clock_timestamp();
    current_session_version bigint;
    current_credential_version bigint;
    current_retry_not_before timestamptz;
    affected_rows integer;
BEGIN
    IF requested_principal_id IS NULL
       OR requested_principal_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR observed_principal_session_version IS NULL
       OR observed_principal_session_version <= 0
       OR requested_authentication_method IS NULL
       OR requested_authentication_method NOT IN ('local', 'oidc')
       OR requested_session_digest IS NULL
       OR octet_length(requested_session_digest) <> 32
       OR requested_csrf_digest IS NULL
       OR octet_length(requested_csrf_digest) <> 32
       OR (requested_authentication_method = 'local') <> (observed_local_credential_version IS NOT NULL)
       OR (
           observed_local_credential_version IS NOT NULL
           AND observed_local_credential_version <= 0
       ) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'browser session input is invalid';
    END IF;

    SELECT principal.session_version
    INTO current_session_version
    FROM gateway_auth.authenticated_principal AS principal
    WHERE principal.principal_id = requested_principal_id
      AND principal.disabled_at IS NULL
    FOR UPDATE;

    IF current_session_version IS NULL
       OR current_session_version <> observed_principal_session_version THEN
        RETURN QUERY SELECT 'identity_stale'::text, NULL::timestamptz, NULL::timestamptz, NULL::timestamptz;
        RETURN;
    END IF;

    IF requested_authentication_method = 'local' THEN
        SELECT credential.credential_version, credential.retry_not_before
        INTO current_credential_version, current_retry_not_before
        FROM gateway_auth.local_account_credential AS credential
        WHERE credential.principal_id = requested_principal_id
          AND credential.disabled_at IS NULL
        FOR UPDATE;

        IF current_credential_version IS NULL
           OR current_credential_version <> observed_local_credential_version
           OR current_retry_not_before > observed_at THEN
            RETURN QUERY SELECT 'identity_stale'::text, NULL::timestamptz, NULL::timestamptz, NULL::timestamptz;
            RETURN;
        END IF;
    END IF;

    DELETE FROM gateway_auth.browser_session AS session
    WHERE session.principal_id = requested_principal_id
      AND (
          session.revoked_at IS NOT NULL
          OR session.absolute_expires_at <= observed_at
          OR session.idle_expires_at <= observed_at
      );

    DELETE FROM gateway_auth.browser_session AS session
    WHERE session.session_digest IN (
        SELECT retained.session_digest
        FROM gateway_auth.browser_session AS retained
        WHERE retained.principal_id = requested_principal_id
        ORDER BY retained.issued_at DESC, retained.session_digest DESC
        OFFSET 19
    );

    INSERT INTO gateway_auth.browser_session (
        session_digest,
        csrf_digest,
        principal_id,
        principal_session_version,
        authentication_method,
        local_credential_version,
        issued_at,
        last_seen_at,
        idle_expires_at,
        absolute_expires_at,
        revoked_at
    ) VALUES (
        requested_session_digest,
        requested_csrf_digest,
        requested_principal_id,
        observed_principal_session_version,
        requested_authentication_method,
        observed_local_credential_version,
        observed_at,
        observed_at,
        observed_at + interval '30 minutes',
        observed_at + interval '12 hours',
        NULL
    ) ON CONFLICT DO NOTHING;
    GET DIAGNOSTICS affected_rows = ROW_COUNT;

    IF affected_rows <> 1 THEN
        RETURN QUERY SELECT 'token_conflict'::text, NULL::timestamptz, NULL::timestamptz, NULL::timestamptz;
        RETURN;
    END IF;

    IF requested_authentication_method = 'local' THEN
        UPDATE gateway_auth.local_account_credential AS credential
        SET failed_attempt_count = 0, retry_not_before = NULL
        WHERE credential.principal_id = requested_principal_id;
    END IF;

    RETURN QUERY SELECT
        'created'::text,
        observed_at,
        observed_at + interval '30 minutes',
        observed_at + interval '12 hours';
END
$create_browser_session$;

CREATE FUNCTION gateway_auth.authenticate_browser_session(
    requested_session_digest bytea,
    requested_csrf_digest bytea,
    require_csrf boolean
) RETURNS TABLE (
    principal_id uuid,
    subject_key text,
    display_name text,
    is_system_administrator boolean,
    principal_session_version bigint,
    authentication_method text,
    idle_expires_at timestamptz,
    absolute_expires_at timestamptz
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $authenticate_browser_session$
DECLARE
    observed_at timestamptz := clock_timestamp();
BEGIN
    IF requested_session_digest IS NULL
       OR octet_length(requested_session_digest) <> 32
       OR require_csrf IS NULL
       OR (require_csrf AND (
           requested_csrf_digest IS NULL
           OR octet_length(requested_csrf_digest) <> 32
       ))
       OR (NOT require_csrf AND requested_csrf_digest IS NOT NULL) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'browser session authentication input is invalid';
    END IF;

    RETURN QUERY
    WITH touched AS (
        UPDATE gateway_auth.browser_session AS session
        SET
            last_seen_at = observed_at,
            idle_expires_at = least(
                observed_at + interval '30 minutes',
                session.absolute_expires_at
            )
        FROM gateway_auth.authenticated_principal AS principal
        LEFT JOIN gateway_auth.local_account_credential AS credential
          ON credential.principal_id = principal.principal_id
        WHERE session.session_digest = requested_session_digest
          AND session.principal_id = principal.principal_id
          AND session.principal_session_version = principal.session_version
          AND session.revoked_at IS NULL
          AND session.idle_expires_at > observed_at
          AND session.absolute_expires_at > observed_at
          AND principal.disabled_at IS NULL
          AND (
              session.authentication_method = 'oidc'
              OR (
                  credential.disabled_at IS NULL
                  AND session.local_credential_version = credential.credential_version
              )
          )
          AND (NOT require_csrf OR session.csrf_digest = requested_csrf_digest)
        RETURNING
            principal.principal_id,
            principal.subject_key,
            principal.display_name,
            principal.is_system_administrator,
            principal.session_version,
            session.authentication_method,
            session.idle_expires_at,
            session.absolute_expires_at
    )
    SELECT touched.* FROM touched;
END
$authenticate_browser_session$;

CREATE FUNCTION gateway_auth.revoke_browser_session(
    requested_session_digest bytea,
    requested_csrf_digest bytea
) RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, gateway_auth
AS $revoke_browser_session$
DECLARE
    observed_at timestamptz := clock_timestamp();
    affected_rows integer;
BEGIN
    IF requested_session_digest IS NULL
       OR octet_length(requested_session_digest) <> 32
       OR requested_csrf_digest IS NULL
       OR octet_length(requested_csrf_digest) <> 32 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'browser session revocation input is invalid';
    END IF;

    UPDATE gateway_auth.browser_session AS session
    SET revoked_at = observed_at
    WHERE session.session_digest = requested_session_digest
      AND session.csrf_digest = requested_csrf_digest
      AND session.revoked_at IS NULL;
    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    RETURN affected_rows = 1;
END
$revoke_browser_session$;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA gateway_auth FROM PUBLIC;
GRANT EXECUTE ON
    FUNCTION gateway_auth.read_local_sign_in_material(text),
    gateway_auth.record_local_sign_in_failure(text, bigint),
    gateway_auth.create_browser_session(uuid, bigint, text, bigint, bytea, bytea),
    gateway_auth.authenticate_browser_session(bytea, bytea, boolean),
    gateway_auth.revoke_browser_session(bytea, bytea)
    TO assetlibrary_gateway_auth_runtime;
