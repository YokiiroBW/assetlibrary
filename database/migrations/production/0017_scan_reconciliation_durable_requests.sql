CREATE TABLE scan_reconciliation.scan_request (
    task_id uuid PRIMARY KEY,
    library_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    dispatched boolean NOT NULL DEFAULT false,
    terminal boolean NOT NULL DEFAULT false,
    cancellation_requested_at timestamptz,
    recovered_at timestamptz NOT NULL DEFAULT '1970-01-01T00:00:00Z'
);
CREATE UNIQUE INDEX scan_request_active_library_index
    ON scan_reconciliation.scan_request (library_id) WHERE NOT terminal;
CREATE INDEX scan_request_recovery_index
    ON scan_reconciliation.scan_request (recovered_at, task_id) WHERE NOT terminal;

CREATE TABLE scan_reconciliation.scan_operation (
    principal_id uuid NOT NULL,
    operation text NOT NULL CHECK (operation IN ('start', 'cancel')),
    idempotency_key uuid NOT NULL,
    library_id uuid NOT NULL,
    task_id uuid NOT NULL REFERENCES scan_reconciliation.scan_request(task_id),
    PRIMARY KEY (principal_id, operation, idempotency_key)
);

ALTER TABLE scan_reconciliation.scan_run
    ADD COLUMN task_id uuid,
    ADD COLUMN attempt integer;
CREATE UNIQUE INDEX scan_run_task_attempt_index ON scan_reconciliation.scan_run (task_id, attempt)
    WHERE task_id IS NOT NULL;

CREATE FUNCTION scan_reconciliation.accept_initial_scan(
    requested_principal uuid, requested_key uuid, requested_library uuid, requested_task uuid,
    requested_now timestamptz
) RETURNS uuid
LANGUAGE plpgsql SET search_path = pg_catalog, scan_reconciliation
AS $accept_initial_scan$
DECLARE previous scan_reconciliation.scan_operation%ROWTYPE; active_task uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_principal::text || requested_key::text, 109551017));
    SELECT operation.* INTO previous FROM scan_reconciliation.scan_operation AS operation
    WHERE principal_id = requested_principal AND operation.operation = 'start' AND idempotency_key = requested_key;
    IF FOUND THEN
        IF previous.library_id <> requested_library THEN
            RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'idempotency_conflict';
        END IF;
        RETURN previous.task_id;
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_library::text, 109551018));
    SELECT task_id INTO active_task FROM scan_reconciliation.scan_request
    WHERE library_id = requested_library AND NOT terminal;
    IF NOT FOUND THEN
        active_task := requested_task;
        INSERT INTO scan_reconciliation.scan_request(task_id, library_id, created_at)
        VALUES (active_task, requested_library, requested_now);
    END IF;
    INSERT INTO scan_reconciliation.scan_operation VALUES
        (requested_principal, 'start', requested_key, requested_library, active_task);
    RETURN active_task;
END
$accept_initial_scan$;

CREATE FUNCTION scan_reconciliation.request_scan_cancellation(
    requested_principal uuid, requested_key uuid, requested_library uuid, requested_task uuid,
    requested_now timestamptz
) RETURNS boolean
LANGUAGE plpgsql SET search_path = pg_catalog, scan_reconciliation
AS $request_scan_cancellation$
DECLARE previous scan_reconciliation.scan_operation%ROWTYPE;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_principal::text || requested_key::text, 109551019));
    SELECT operation.* INTO previous FROM scan_reconciliation.scan_operation AS operation
    WHERE principal_id = requested_principal AND operation.operation = 'cancel' AND idempotency_key = requested_key;
    IF FOUND THEN
        IF previous.library_id <> requested_library OR previous.task_id <> requested_task THEN
            RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'idempotency_conflict';
        END IF;
        RETURN true;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM scan_reconciliation.scan_request
        WHERE task_id = requested_task AND library_id = requested_library) THEN RETURN false; END IF;
    INSERT INTO scan_reconciliation.scan_operation VALUES
        (requested_principal, 'cancel', requested_key, requested_library, requested_task);
    UPDATE scan_reconciliation.scan_request
    SET cancellation_requested_at = COALESCE(cancellation_requested_at, requested_now)
    WHERE task_id = requested_task AND NOT terminal;
    RETURN true;
END
$request_scan_cancellation$;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA scan_reconciliation FROM PUBLIC;
