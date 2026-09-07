-- Per-library availability must not turn an unrelated library on the same source offline.
ALTER TABLE library_storage.library_root
    ADD COLUMN availability text NOT NULL DEFAULT 'online' CHECK (availability IN ('online', 'offline')),
    ADD COLUMN availability_observed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    ADD COLUMN availability_reason text;

CREATE INDEX library_root_probe_index ON library_storage.library_root (availability_observed_at, library_id);

CREATE TABLE library_storage.registration_operation (
    principal_id uuid NOT NULL,
    idempotency_key uuid NOT NULL,
    request_body jsonb NOT NULL CHECK (jsonb_typeof(request_body) = 'object'),
    library_id uuid NOT NULL REFERENCES library_storage.library_root(library_id),
    PRIMARY KEY (principal_id, idempotency_key)
);

CREATE OR REPLACE VIEW library_storage.library_catalog_read_projection
WITH (security_barrier = true) AS
SELECT root.library_id, root.display_name,
    CASE WHEN source.availability = 'offline' THEN 'offline' ELSE root.availability END AS availability
FROM library_storage.library_root AS root
JOIN library_storage.storage_source AS source ON source.storage_source_id = root.storage_source_id;

CREATE FUNCTION library_storage.find_registration_operation(
    requested_principal uuid, requested_key uuid, requested_body jsonb
) RETURNS uuid
LANGUAGE plpgsql SET search_path = pg_catalog, library_storage
AS $find_registration_operation$
DECLARE previous library_storage.registration_operation%ROWTYPE;
BEGIN
    SELECT operation.* INTO previous FROM library_storage.registration_operation AS operation
    WHERE operation.principal_id = requested_principal AND operation.idempotency_key = requested_key;
    IF NOT FOUND THEN RETURN NULL; END IF;
    IF previous.request_body <> requested_body THEN
        RAISE EXCEPTION USING ERRCODE = '23505', CONSTRAINT = 'registration_idempotency', MESSAGE = 'idempotency_conflict';
    END IF;
    RETURN previous.library_id;
END
$find_registration_operation$;

CREATE FUNCTION library_storage.register_trial_library(
    requested_principal uuid, requested_key uuid, requested_body jsonb,
    requested_library_id uuid, requested_source_id uuid, requested_source_name text,
    requested_case_sensitive boolean, requested_name text, requested_root text,
    requested_now timestamptz
) RETURNS uuid
LANGUAGE plpgsql SET search_path = pg_catalog, library_storage
AS $register_trial_library$
DECLARE previous uuid;
BEGIN
    IF requested_principal IS NULL OR requested_principal = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_key IS NULL OR requested_key = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_body IS NULL OR jsonb_typeof(requested_body) <> 'object'
       OR requested_root IS NULL OR length(requested_root) NOT BETWEEN 1 AND 4096 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'registration_invalid';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_principal::text || requested_key::text, 109551017));
    previous := library_storage.find_registration_operation(requested_principal, requested_key, requested_body);
    IF previous IS NOT NULL THEN RETURN previous; END IF;
    INSERT INTO library_storage.storage_source
        (storage_source_id, display_name, availability, root_case_sensitive, availability_observed_at)
    VALUES (requested_source_id, requested_source_name, 'online', requested_case_sensitive, requested_now)
    ON CONFLICT (storage_source_id) DO NOTHING;
    IF NOT EXISTS (SELECT 1 FROM library_storage.storage_source
        WHERE storage_source_id = requested_source_id AND root_case_sensitive = requested_case_sensitive) THEN
        RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'storage_configuration_conflict';
    END IF;
    PERFORM library_storage.register_library_root(requested_library_id, requested_source_id,
        requested_name, requested_root, requested_now);
    INSERT INTO library_storage.registration_operation VALUES
        (requested_principal, requested_key, requested_body, requested_library_id);
    RETURN requested_library_id;
END
$register_trial_library$;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA library_storage FROM PUBLIC;
