-- Categories are explicit library metadata; they never infer or change physical ownership.
ALTER TABLE library_storage.library_root
    ADD COLUMN category text NOT NULL DEFAULT 'general'
        CHECK (category IN ('photos','images','videos','music','projects','documents','characters','general'));

CREATE INDEX library_root_category_browse_index
    ON library_storage.library_root (category, lower(display_name), library_id);

CREATE OR REPLACE VIEW library_storage.library_catalog_read_projection
WITH (security_barrier = true) AS
SELECT root.library_id, root.display_name,
    CASE WHEN source.availability = 'offline' THEN 'offline' ELSE root.availability END AS availability,
    root.category
FROM library_storage.library_root AS root
JOIN library_storage.storage_source AS source ON source.storage_source_id = root.storage_source_id;

CREATE FUNCTION library_storage.register_trial_library_v2(
    requested_principal uuid, requested_key uuid, requested_body jsonb,
    requested_library_id uuid, requested_source_id uuid, requested_source_name text,
    requested_case_sensitive boolean, requested_name text, requested_root text,
    requested_now timestamptz, requested_category text
) RETURNS uuid
LANGUAGE plpgsql SET search_path = pg_catalog, library_storage
AS $register_trial_library_v2$
DECLARE registered uuid;
BEGIN
    IF requested_category IS NULL OR requested_category NOT IN
        ('photos','images','videos','music','projects','documents','characters','general') THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'registration_category_invalid';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_principal::text || requested_key::text, 109551017));
    registered := library_storage.find_registration_operation(requested_principal, requested_key, requested_body);
    -- A replay must not undo a later, explicit category edit.
    IF registered IS NOT NULL THEN RETURN registered; END IF;
    registered := library_storage.register_trial_library(requested_principal, requested_key, requested_body,
        requested_library_id, requested_source_id, requested_source_name, requested_case_sensitive,
        requested_name, requested_root, requested_now);
    UPDATE library_storage.library_root SET category = requested_category WHERE library_id = registered;
    RETURN registered;
END
$register_trial_library_v2$;

CREATE TABLE library_storage.library_category_operation (
    principal_id uuid NOT NULL CHECK (principal_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    idempotency_key uuid NOT NULL CHECK (idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid),
    library_id uuid NOT NULL REFERENCES library_storage.library_root(library_id),
    expected_category text NOT NULL,
    category text NOT NULL CHECK (category IN ('photos','images','videos','music','projects','documents','characters','general')),
    changed_at timestamptz NOT NULL,
    PRIMARY KEY (principal_id, idempotency_key)
);

CREATE FUNCTION library_storage.update_library_category(
    requested_principal uuid, requested_key uuid, requested_library_id uuid,
    requested_category text, requested_expected_category text, requested_now timestamptz
) RETURNS text
LANGUAGE plpgsql SET search_path = pg_catalog, library_storage
AS $update_library_category$
DECLARE previous library_storage.library_category_operation%ROWTYPE; current_category text;
BEGIN
    IF requested_principal IS NULL OR requested_principal = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_key IS NULL OR requested_key = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_library_id IS NULL OR requested_library_id = '00000000-0000-0000-0000-000000000000'::uuid
       OR requested_now IS NULL
       OR requested_category IS NULL OR requested_category NOT IN
          ('photos','images','videos','music','projects','documents','characters','general')
       OR requested_expected_category IS NULL OR requested_expected_category NOT IN
          ('photos','images','videos','music','projects','documents','characters','general') THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'category_update_invalid';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_principal::text || requested_key::text, 109551026));
    SELECT operation.* INTO previous FROM library_storage.library_category_operation AS operation
    WHERE operation.principal_id = requested_principal AND operation.idempotency_key = requested_key;
    IF FOUND THEN
        IF previous.library_id <> requested_library_id OR previous.category <> requested_category
           OR previous.expected_category <> requested_expected_category THEN
            RAISE EXCEPTION USING ERRCODE = '23505', CONSTRAINT = 'category_idempotency', MESSAGE = 'idempotency_conflict';
        END IF;
        RETURN previous.category;
    END IF;
    SELECT root.category INTO current_category FROM library_storage.library_root AS root
    WHERE root.library_id = requested_library_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'library_not_found';
    END IF;
    IF current_category <> requested_expected_category THEN
        RAISE EXCEPTION USING ERRCODE = '40001', MESSAGE = 'state_conflict';
    END IF;
    UPDATE library_storage.library_root SET category = requested_category WHERE library_id = requested_library_id;
    INSERT INTO library_storage.library_category_operation VALUES
        (requested_principal, requested_key, requested_library_id, requested_expected_category, requested_category, requested_now);
    RETURN requested_category;
END
$update_library_category$;

REVOKE ALL ON FUNCTION library_storage.register_trial_library_v2(uuid,uuid,jsonb,uuid,uuid,text,boolean,text,text,timestamptz,text),
    library_storage.update_library_category(uuid,uuid,uuid,text,text,timestamptz) FROM PUBLIC;
