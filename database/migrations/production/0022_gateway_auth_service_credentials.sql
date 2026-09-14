-- Independent service identities never receive local login or browser sessions.
CREATE TABLE gateway_auth.service_principal (
    principal_id uuid PRIMARY KEY REFERENCES gateway_auth.authenticated_principal(principal_id)
);
CREATE TABLE gateway_auth.service_read_credential (
    credential_id uuid PRIMARY KEY CHECK (credential_id <> '00000000-0000-0000-0000-000000000000'),
    principal_id uuid NOT NULL REFERENCES gateway_auth.service_principal(principal_id),
    secret_digest bytea NOT NULL UNIQUE CHECK (octet_length(secret_digest) = 32),
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    CHECK (expires_at > created_at AND expires_at <= created_at + interval '365 days'),
    CHECK (revoked_at IS NULL OR revoked_at >= created_at)
);
CREATE INDEX service_read_credential_principal_index ON gateway_auth.service_read_credential(principal_id);
CREATE TABLE gateway_auth.service_read_operation (
    operation_id uuid NOT NULL CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'),
    action text NOT NULL CHECK (action IN ('create', 'issue', 'rotate', 'revoke', 'disable')),
    operator_id text NOT NULL CHECK (length(btrim(operator_id)) BETWEEN 1 AND 200 AND operator_id !~ '[[:cntrl:]]'),
    principal_id uuid NOT NULL REFERENCES gateway_auth.service_principal(principal_id),
    credential_id uuid,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY(operation_id, action)
);
REVOKE ALL ON gateway_auth.service_principal, gateway_auth.service_read_credential,
    gateway_auth.service_read_operation FROM assetlibrary_gateway_auth_runtime;

CREATE FUNCTION gateway_auth.lock_service_read_principal(requested_principal uuid, require_enabled boolean)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $lock_service_read_principal$
BEGIN
    PERFORM 1 FROM gateway_auth.authenticated_principal AS p
    JOIN gateway_auth.service_principal AS s USING(principal_id)
    WHERE p.principal_id = requested_principal AND NOT p.is_system_administrator
      AND (require_enabled IS FALSE OR p.disabled_at IS NULL)
      AND NOT EXISTS (SELECT 1 FROM gateway_auth.local_account_credential AS l WHERE l.principal_id = p.principal_id)
    FOR UPDATE OF p;
    RETURN FOUND;
END
$lock_service_read_principal$;

CREATE FUNCTION gateway_auth.create_service_read_principal(requested_principal uuid, requested_name text,
    requested_operator text, requested_operation uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $create_service_read_principal$
BEGIN
    IF requested_name IS NULL OR length(btrim(requested_name)) NOT BETWEEN 1 AND 200
       OR requested_name ~ '[[:cntrl:]]' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'service name is invalid';
    END IF;
    -- Serialize retries on the chosen principal, including its first insertion.
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_principal::text, 6322));
    IF EXISTS (SELECT 1 FROM gateway_auth.authenticated_principal WHERE principal_id = requested_principal) THEN
        RETURN gateway_auth.lock_service_read_principal(requested_principal, true)
            AND EXISTS (SELECT 1 FROM gateway_auth.service_read_operation
                WHERE operation_id = requested_operation AND action = 'create'
                  AND principal_id = requested_principal AND operator_id = requested_operator)
            AND EXISTS (SELECT 1 FROM gateway_auth.authenticated_principal
                WHERE principal_id = requested_principal AND display_name = requested_name);
    END IF;
    INSERT INTO gateway_auth.authenticated_principal(principal_id, subject_key, display_name, created_at)
    VALUES(requested_principal, 'service:' || requested_principal::text, requested_name, clock_timestamp());
    INSERT INTO gateway_auth.service_principal VALUES(requested_principal);
    INSERT INTO gateway_auth.service_read_operation(operation_id, action, operator_id, principal_id)
    VALUES(requested_operation, 'create', requested_operator, requested_principal);
    RETURN true;
END
$create_service_read_principal$;

CREATE FUNCTION gateway_auth.issue_service_read_credential(requested_principal uuid, requested_credential uuid,
    replaced_credential uuid, requested_digest bytea, requested_expiry timestamptz,
    requested_operator text, requested_operation uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $issue_service_read_credential$
DECLARE issued_at timestamptz; requested_action text;
BEGIN
    IF NOT gateway_auth.lock_service_read_principal(requested_principal, true) THEN RETURN false; END IF;
    issued_at := clock_timestamp();
    requested_action := CASE WHEN replaced_credential IS NULL THEN 'issue' ELSE 'rotate' END;
    -- An uncertain/replayed issue never returns another secret. Operator must explicitly rotate.
    IF EXISTS (SELECT 1 FROM gateway_auth.service_read_operation
        WHERE operation_id = requested_operation AND action = requested_action) THEN RETURN false; END IF;
    IF replaced_credential IS NOT NULL THEN
        UPDATE gateway_auth.service_read_credential SET revoked_at = issued_at
        WHERE credential_id = replaced_credential AND principal_id = requested_principal AND revoked_at IS NULL;
        IF NOT FOUND THEN RETURN false; END IF;
    END IF;
    INSERT INTO gateway_auth.service_read_credential
    VALUES(requested_credential, requested_principal, requested_digest, issued_at, requested_expiry, NULL);
    INSERT INTO gateway_auth.service_read_operation(operation_id, action, operator_id, principal_id, credential_id)
    VALUES(requested_operation, requested_action, requested_operator, requested_principal, requested_credential);
    RETURN true;
END
$issue_service_read_credential$;

CREATE FUNCTION gateway_auth.authenticate_service_read_credential(requested_digest bytea)
RETURNS TABLE(principal_id uuid, subject_key text)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $authenticate_service_read_credential$
    SELECT p.principal_id, p.subject_key FROM gateway_auth.service_read_credential AS c
    JOIN gateway_auth.service_principal AS s ON s.principal_id = c.principal_id
    JOIN gateway_auth.authenticated_principal AS p ON p.principal_id = s.principal_id
    WHERE c.secret_digest = requested_digest AND c.revoked_at IS NULL
      AND c.expires_at > statement_timestamp() AND p.disabled_at IS NULL AND NOT p.is_system_administrator
      AND NOT EXISTS (SELECT 1 FROM gateway_auth.local_account_credential AS l WHERE l.principal_id = p.principal_id);
$authenticate_service_read_credential$;

CREATE FUNCTION gateway_auth.revoke_service_read_credential(requested_principal uuid, requested_credential uuid,
    requested_operator text, requested_operation uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $revoke_service_read_credential$
BEGIN
    IF NOT gateway_auth.lock_service_read_principal(requested_principal, false) THEN RETURN false; END IF;
    IF EXISTS (SELECT 1 FROM gateway_auth.service_read_operation WHERE operation_id = requested_operation AND action = 'revoke') THEN
        RETURN EXISTS (SELECT 1 FROM gateway_auth.service_read_operation WHERE operation_id = requested_operation AND action = 'revoke'
            AND operator_id = requested_operator AND principal_id = requested_principal AND credential_id = requested_credential);
    END IF;
    UPDATE gateway_auth.service_read_credential SET revoked_at = coalesce(revoked_at, clock_timestamp())
    WHERE credential_id = requested_credential AND principal_id = requested_principal;
    IF NOT FOUND THEN RETURN false; END IF;
    INSERT INTO gateway_auth.service_read_operation(operation_id, action, operator_id, principal_id, credential_id)
    VALUES(requested_operation, 'revoke', requested_operator, requested_principal, requested_credential);
    RETURN true;
END
$revoke_service_read_credential$;

CREATE FUNCTION gateway_auth.disable_service_read_principal(requested_principal uuid,
    requested_operator text, requested_operation uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, gateway_auth
AS $disable_service_read_principal$
BEGIN
    IF NOT gateway_auth.lock_service_read_principal(requested_principal, false) THEN RETURN false; END IF;
    IF EXISTS (SELECT 1 FROM gateway_auth.service_read_operation WHERE operation_id = requested_operation AND action = 'disable') THEN
        RETURN EXISTS (SELECT 1 FROM gateway_auth.service_read_operation WHERE operation_id = requested_operation AND action = 'disable'
            AND operator_id = requested_operator AND principal_id = requested_principal);
    END IF;
    UPDATE gateway_auth.authenticated_principal SET disabled_at = coalesce(disabled_at, clock_timestamp()) WHERE principal_id = requested_principal;
    UPDATE gateway_auth.service_read_credential SET revoked_at = coalesce(revoked_at, clock_timestamp()) WHERE principal_id = requested_principal;
    INSERT INTO gateway_auth.service_read_operation(operation_id, action, operator_id, principal_id)
    VALUES(requested_operation, 'disable', requested_operator, requested_principal);
    RETURN true;
END
$disable_service_read_principal$;

REVOKE ALL ON FUNCTION gateway_auth.lock_service_read_principal(uuid, boolean),
    gateway_auth.create_service_read_principal(uuid, text, text, uuid),
    gateway_auth.issue_service_read_credential(uuid, uuid, uuid, bytea, timestamptz, text, uuid),
    gateway_auth.authenticate_service_read_credential(bytea),
    gateway_auth.revoke_service_read_credential(uuid, uuid, text, uuid),
    gateway_auth.disable_service_read_principal(uuid, text, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION gateway_auth.create_service_read_principal(uuid, text, text, uuid),
    gateway_auth.issue_service_read_credential(uuid, uuid, uuid, bytea, timestamptz, text, uuid),
    gateway_auth.authenticate_service_read_credential(bytea),
    gateway_auth.revoke_service_read_credential(uuid, uuid, text, uuid),
    gateway_auth.disable_service_read_principal(uuid, text, uuid) TO assetlibrary_gateway_auth_runtime;
GRANT USAGE ON SCHEMA gateway_auth TO assetlibrary_library_storage_owner;
GRANT EXECUTE ON FUNCTION gateway_auth.lock_service_read_principal(uuid, boolean) TO assetlibrary_library_storage_owner;
