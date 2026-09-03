-- TaskHealth owns durable tasks, health observations and outbox delivery state.
CREATE TYPE task_health.task_state AS ENUM (
    'queued',
    'leased',
    'succeeded',
    'failed',
    'cancelled'
);

CREATE TYPE task_health.outbox_state AS ENUM (
    'pending',
    'leased',
    'published',
    'dead_lettered'
);

CREATE TYPE task_health.health_scope_kind AS ENUM (
    'system',
    'library',
    'asset'
);

CREATE TYPE task_health.health_state AS ENUM (
    'normal',
    'degraded',
    'warning',
    'offline',
    'maintenance',
    'initializing'
);

CREATE TABLE task_health.durable_task (
    task_id uuid PRIMARY KEY
        CHECK (task_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    idempotency_key text NOT NULL UNIQUE
        CHECK (length(idempotency_key) BETWEEN 1 AND 200 AND idempotency_key = btrim(idempotency_key)),
    task_type text NOT NULL
        CHECK (task_type ~ '^[a-z][a-z0-9._-]{0,99}$'),
    payload jsonb NOT NULL
        CHECK (jsonb_typeof(payload) = 'object' AND octet_length(payload::text) <= 262144),
    priority smallint NOT NULL CHECK (priority BETWEEN 0 AND 5),
    state task_health.task_state NOT NULL DEFAULT 'queued',
    available_at timestamptz NOT NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    max_attempts integer NOT NULL CHECK (max_attempts BETWEEN 1 AND 100),
    cancellation_requested_at timestamptz,
    lease_owner text CHECK (
        lease_owner IS NULL OR lease_owner ~ '^[a-z][a-z0-9._-]{0,199}$'
    ),
    lease_token uuid CHECK (
        lease_token IS NULL OR lease_token <> '00000000-0000-0000-0000-000000000000'::uuid
    ),
    lease_generation bigint NOT NULL DEFAULT 0 CHECK (lease_generation >= 0),
    lease_until timestamptz,
    heartbeat_at timestamptz,
    last_failure_code text CHECK (
        last_failure_code IS NULL OR last_failure_code ~ '^[a-z][a-z0-9._-]{0,99}$'
    ),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CHECK (attempts <= max_attempts),
    CHECK (
        (state = 'leased'
            AND lease_owner IS NOT NULL
            AND lease_token IS NOT NULL
            AND lease_generation > 0
            AND lease_until IS NOT NULL
            AND heartbeat_at IS NOT NULL)
        OR (state <> 'leased'
            AND lease_owner IS NULL
            AND lease_token IS NULL
            AND lease_until IS NULL
            AND heartbeat_at IS NULL)
    ),
    CHECK (state <> 'failed' OR last_failure_code IS NOT NULL),
    CHECK (state <> 'cancelled' OR cancellation_requested_at IS NOT NULL)
);

CREATE INDEX durable_task_claim_index
    ON task_health.durable_task (priority, available_at, created_at, task_id)
    WHERE state = 'queued' AND cancellation_requested_at IS NULL;

CREATE INDEX durable_task_expired_lease_index
    ON task_health.durable_task (lease_until, task_id)
    WHERE state = 'leased';

CREATE INDEX durable_task_state_updated_index
    ON task_health.durable_task (state, updated_at DESC, task_id);

CREATE TABLE task_health.outbox_event (
    event_id uuid PRIMARY KEY
        CHECK (event_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    source_module text NOT NULL
        CHECK (source_module ~ '^[A-Z][A-Za-z0-9]{0,99}$'),
    event_type text NOT NULL
        CHECK (event_type ~ '^[a-z][a-z0-9._-]{0,149}$'),
    aggregate_id uuid CHECK (
        aggregate_id IS NULL OR aggregate_id <> '00000000-0000-0000-0000-000000000000'::uuid
    ),
    schema_version integer NOT NULL CHECK (schema_version BETWEEN 1 AND 1000),
    payload jsonb NOT NULL
        CHECK (jsonb_typeof(payload) = 'object' AND octet_length(payload::text) <= 262144),
    state task_health.outbox_state NOT NULL DEFAULT 'pending',
    available_at timestamptz NOT NULL,
    publish_attempts integer NOT NULL DEFAULT 0 CHECK (publish_attempts >= 0),
    max_publish_attempts integer NOT NULL CHECK (max_publish_attempts BETWEEN 1 AND 100),
    lease_owner text CHECK (
        lease_owner IS NULL OR lease_owner ~ '^[a-z][a-z0-9._-]{0,199}$'
    ),
    lease_token uuid CHECK (
        lease_token IS NULL OR lease_token <> '00000000-0000-0000-0000-000000000000'::uuid
    ),
    lease_generation bigint NOT NULL DEFAULT 0 CHECK (lease_generation >= 0),
    lease_until timestamptz,
    last_failure_code text CHECK (
        last_failure_code IS NULL OR last_failure_code ~ '^[a-z][a-z0-9._-]{0,99}$'
    ),
    occurred_at timestamptz NOT NULL,
    published_at timestamptz,
    updated_at timestamptz NOT NULL,
    CHECK (publish_attempts <= max_publish_attempts),
    CHECK (
        (state = 'leased'
            AND lease_owner IS NOT NULL
            AND lease_token IS NOT NULL
            AND lease_generation > 0
            AND lease_until IS NOT NULL)
        OR (state <> 'leased'
            AND lease_owner IS NULL
            AND lease_token IS NULL
            AND lease_until IS NULL)
    ),
    CHECK ((state = 'published') = (published_at IS NOT NULL)),
    CHECK (state <> 'dead_lettered' OR last_failure_code IS NOT NULL)
);

CREATE INDEX outbox_event_claim_index
    ON task_health.outbox_event (available_at, occurred_at, event_id)
    WHERE state = 'pending';

CREATE INDEX outbox_event_expired_lease_index
    ON task_health.outbox_event (lease_until, event_id)
    WHERE state = 'leased';

CREATE INDEX outbox_event_state_updated_index
    ON task_health.outbox_event (state, updated_at DESC, event_id);

CREATE TABLE task_health.health_status (
    scope_kind task_health.health_scope_kind NOT NULL,
    scope_id uuid,
    scope_identity uuid GENERATED ALWAYS AS (
        COALESCE(scope_id, '00000000-0000-0000-0000-000000000000'::uuid)
    ) STORED,
    component text NOT NULL
        CHECK (component ~ '^[a-z][a-z0-9._-]{0,99}$'),
    state task_health.health_state NOT NULL,
    reason_code text CHECK (
        reason_code IS NULL OR reason_code ~ '^[a-z][a-z0-9._-]{0,99}$'
    ),
    observed_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (scope_kind, scope_identity, component),
    CHECK (
        (scope_kind = 'system' AND scope_id IS NULL)
        OR (scope_kind IN ('library', 'asset')
            AND scope_id IS NOT NULL
            AND scope_id <> '00000000-0000-0000-0000-000000000000'::uuid)
    ),
    CHECK (
        (state = 'normal' AND reason_code IS NULL)
        OR (state <> 'normal' AND reason_code IS NOT NULL)
    )
);

CREATE INDEX health_status_scope_index
    ON task_health.health_status (scope_kind, scope_id, component);

CREATE FUNCTION task_health.enqueue_durable_task(
    requested_task_id uuid,
    requested_idempotency_key text,
    requested_task_type text,
    requested_payload jsonb,
    requested_priority smallint,
    requested_max_attempts integer,
    requested_available_at timestamptz,
    requested_created_at timestamptz
) RETURNS TABLE(task_id uuid, created boolean)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $enqueue_durable_task$
DECLARE
    existing task_health.durable_task%ROWTYPE;
BEGIN
    INSERT INTO task_health.durable_task AS inserted (
        task_id,
        idempotency_key,
        task_type,
        payload,
        priority,
        available_at,
        max_attempts,
        created_at,
        updated_at
    ) VALUES (
        requested_task_id,
        requested_idempotency_key,
        requested_task_type,
        requested_payload,
        requested_priority,
        requested_available_at,
        requested_max_attempts,
        requested_created_at,
        requested_created_at
    )
    ON CONFLICT (idempotency_key) DO NOTHING
    RETURNING inserted.task_id INTO task_id;

    IF FOUND THEN
        created := true;
        RETURN NEXT;
        RETURN;
    END IF;

    SELECT candidate.*
    INTO existing
    FROM task_health.durable_task AS candidate
    WHERE candidate.idempotency_key = requested_idempotency_key;

    IF NOT FOUND THEN
        RAISE EXCEPTION USING
            ERRCODE = '40001',
            MESSAGE = 'concurrent idempotent task is not visible; retry the transaction';
    END IF;

    -- A retry cannot reschedule existing work; requested IDs and scheduling timestamps are not identity fields.
    IF existing.task_type <> requested_task_type
        OR existing.payload <> requested_payload
        OR existing.priority <> requested_priority
        OR existing.max_attempts <> requested_max_attempts
    THEN
        RAISE EXCEPTION USING
            ERRCODE = '23505',
            MESSAGE = 'idempotency key is already bound to a different task request';
    END IF;

    task_id := existing.task_id;
    created := false;
    RETURN NEXT;
END
$enqueue_durable_task$;

CREATE FUNCTION task_health.claim_durable_tasks(
    requested_worker text,
    requested_batch_size integer,
    requested_lease_seconds integer
) RETURNS TABLE(
    task_id uuid,
    task_type text,
    payload jsonb,
    priority smallint,
    attempt integer,
    max_attempts integer,
    lease_token uuid,
    lease_generation bigint,
    lease_until timestamptz,
    cancellation_requested boolean
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $claim_durable_tasks$
BEGIN
    IF requested_worker IS NULL
        OR requested_worker !~ '^[a-z][a-z0-9._-]{0,199}$'
        OR requested_batch_size IS NULL
        OR requested_batch_size NOT BETWEEN 1 AND 256
        OR requested_lease_seconds IS NULL
        OR requested_lease_seconds NOT BETWEEN 1 AND 3600
    THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid task claim request';
    END IF;

    RETURN QUERY
    WITH candidates AS (
        SELECT candidate.task_id
        FROM task_health.durable_task AS candidate
        WHERE candidate.state = 'queued'
          AND candidate.cancellation_requested_at IS NULL
          AND candidate.available_at <= clock_timestamp()
          AND candidate.attempts < candidate.max_attempts
        ORDER BY candidate.priority, candidate.available_at, candidate.created_at, candidate.task_id
        FOR UPDATE SKIP LOCKED
        LIMIT requested_batch_size
    )
    UPDATE task_health.durable_task AS claimed
    SET state = 'leased',
        attempts = claimed.attempts + 1,
        lease_owner = requested_worker,
        lease_token = gen_random_uuid(),
        lease_generation = claimed.lease_generation + 1,
        lease_until = clock_timestamp() + make_interval(secs => requested_lease_seconds),
        heartbeat_at = clock_timestamp(),
        updated_at = clock_timestamp()
    FROM candidates
    WHERE claimed.task_id = candidates.task_id
    RETURNING
        claimed.task_id,
        claimed.task_type,
        claimed.payload,
        claimed.priority,
        claimed.attempts,
        claimed.max_attempts,
        claimed.lease_token,
        claimed.lease_generation,
        claimed.lease_until,
        claimed.cancellation_requested_at IS NOT NULL;
END
$claim_durable_tasks$;

CREATE FUNCTION task_health.heartbeat_durable_task(
    requested_task_id uuid,
    requested_worker text,
    requested_lease_token uuid,
    requested_lease_generation bigint,
    requested_lease_seconds integer
) RETURNS TABLE(cancellation_requested boolean, lease_until timestamptz)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $heartbeat_durable_task$
BEGIN
    IF requested_task_id IS NULL
        OR requested_worker IS NULL
        OR requested_worker !~ '^[a-z][a-z0-9._-]{0,199}$'
        OR requested_lease_token IS NULL
        OR requested_lease_generation IS NULL
        OR requested_lease_generation <= 0
        OR requested_lease_seconds IS NULL
        OR requested_lease_seconds NOT BETWEEN 1 AND 3600
    THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid task heartbeat request';
    END IF;

    RETURN QUERY
    UPDATE task_health.durable_task AS task
    SET lease_until = clock_timestamp() + make_interval(secs => requested_lease_seconds),
        heartbeat_at = clock_timestamp(),
        updated_at = clock_timestamp()
    WHERE task.task_id = requested_task_id
      AND task.state = 'leased'
      AND task.lease_owner = requested_worker
      AND task.lease_token = requested_lease_token
      AND task.lease_generation = requested_lease_generation
      AND task.lease_until >= clock_timestamp()
    RETURNING task.cancellation_requested_at IS NOT NULL, task.lease_until;
END
$heartbeat_durable_task$;

CREATE FUNCTION task_health.finish_durable_task(
    requested_task_id uuid,
    requested_worker text,
    requested_lease_token uuid,
    requested_lease_generation bigint,
    requested_outcome text,
    requested_failure_code text DEFAULT NULL,
    requested_retry_delay_seconds integer DEFAULT NULL
) RETURNS task_health.task_state
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $finish_durable_task$
DECLARE
    resulting_state task_health.task_state;
BEGIN
    IF requested_task_id IS NULL
        OR requested_worker IS NULL
        OR requested_worker !~ '^[a-z][a-z0-9._-]{0,199}$'
        OR requested_lease_token IS NULL
        OR requested_lease_generation IS NULL
        OR requested_lease_generation <= 0
        OR requested_outcome IS NULL
        OR requested_outcome NOT IN ('succeeded', 'retryable_failure', 'permanent_failure', 'cancelled')
        OR (
            requested_outcome IN ('retryable_failure', 'permanent_failure')
            AND (requested_failure_code IS NULL
                OR requested_failure_code !~ '^[a-z][a-z0-9._-]{0,99}$')
        )
        OR (
            requested_outcome NOT IN ('retryable_failure', 'permanent_failure')
            AND requested_failure_code IS NOT NULL
        )
        OR (
            requested_outcome = 'retryable_failure'
            AND (requested_retry_delay_seconds IS NULL
                OR requested_retry_delay_seconds NOT BETWEEN 0 AND 86400)
        )
        OR (
            requested_outcome <> 'retryable_failure'
            AND requested_retry_delay_seconds IS NOT NULL
        )
    THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid task finish request';
    END IF;

    UPDATE task_health.durable_task AS task
    SET state = CASE
            WHEN requested_outcome = 'succeeded' THEN 'succeeded'::task_health.task_state
            WHEN task.cancellation_requested_at IS NOT NULL THEN 'cancelled'::task_health.task_state
            WHEN requested_outcome = 'cancelled' THEN 'cancelled'::task_health.task_state
            WHEN requested_outcome = 'retryable_failure' AND task.attempts < task.max_attempts
                THEN 'queued'::task_health.task_state
            ELSE 'failed'::task_health.task_state
        END,
        available_at = CASE
            WHEN requested_outcome = 'retryable_failure'
                AND task.cancellation_requested_at IS NULL
                AND task.attempts < task.max_attempts
                THEN clock_timestamp() + make_interval(secs => requested_retry_delay_seconds)
            ELSE task.available_at
        END,
        last_failure_code = CASE
            WHEN requested_outcome IN ('retryable_failure', 'permanent_failure')
                AND task.cancellation_requested_at IS NULL
                THEN requested_failure_code
            ELSE NULL
        END,
        cancellation_requested_at = CASE
            WHEN requested_outcome = 'cancelled' THEN COALESCE(task.cancellation_requested_at, clock_timestamp())
            ELSE task.cancellation_requested_at
        END,
        lease_owner = NULL,
        lease_token = NULL,
        lease_until = NULL,
        heartbeat_at = NULL,
        updated_at = clock_timestamp()
    WHERE task.task_id = requested_task_id
      AND task.state = 'leased'
      AND task.lease_owner = requested_worker
      AND task.lease_token = requested_lease_token
      AND task.lease_generation = requested_lease_generation
      AND task.lease_until >= clock_timestamp()
      AND (requested_outcome <> 'cancelled' OR task.cancellation_requested_at IS NOT NULL)
    RETURNING task.state INTO resulting_state;

    RETURN resulting_state;
END
$finish_durable_task$;

CREATE FUNCTION task_health.request_durable_task_cancellation(requested_task_id uuid)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $request_durable_task_cancellation$
DECLARE
    current_state task_health.task_state;
BEGIN
    SELECT task.state
    INTO current_state
    FROM task_health.durable_task AS task
    WHERE task.task_id = requested_task_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN 'not_found';
    END IF;

    IF current_state = 'queued' THEN
        UPDATE task_health.durable_task AS task
        SET state = 'cancelled',
            cancellation_requested_at = COALESCE(task.cancellation_requested_at, clock_timestamp()),
            last_failure_code = NULL,
            updated_at = clock_timestamp()
        WHERE task.task_id = requested_task_id;
        RETURN 'cancelled';
    END IF;

    IF current_state = 'leased' THEN
        UPDATE task_health.durable_task AS task
        SET cancellation_requested_at = COALESCE(task.cancellation_requested_at, clock_timestamp()),
            updated_at = clock_timestamp()
        WHERE task.task_id = requested_task_id;
        RETURN 'requested';
    END IF;

    IF current_state = 'cancelled' THEN
        RETURN 'already_cancelled';
    END IF;

    RETURN 'already_terminal';
END
$request_durable_task_cancellation$;

CREATE FUNCTION task_health.reclaim_expired_durable_tasks(requested_batch_size integer)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $reclaim_expired_durable_tasks$
DECLARE
    reclaimed_count integer;
BEGIN
    IF requested_batch_size IS NULL OR requested_batch_size NOT BETWEEN 1 AND 1024 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid task reclaim batch size';
    END IF;

    WITH candidates AS (
        SELECT candidate.task_id
        FROM task_health.durable_task AS candidate
        WHERE candidate.state = 'leased'
          AND candidate.lease_until < clock_timestamp()
        ORDER BY candidate.lease_until, candidate.task_id
        FOR UPDATE SKIP LOCKED
        LIMIT requested_batch_size
    ), reclaimed AS (
        UPDATE task_health.durable_task AS task
        SET state = CASE
                WHEN task.cancellation_requested_at IS NOT NULL THEN 'cancelled'::task_health.task_state
                WHEN task.attempts >= task.max_attempts THEN 'failed'::task_health.task_state
                ELSE 'queued'::task_health.task_state
            END,
            available_at = CASE
                WHEN task.cancellation_requested_at IS NULL AND task.attempts < task.max_attempts
                    THEN clock_timestamp()
                ELSE task.available_at
            END,
            last_failure_code = CASE
                WHEN task.cancellation_requested_at IS NULL THEN 'lease_expired'
                ELSE NULL
            END,
            lease_owner = NULL,
            lease_token = NULL,
            lease_until = NULL,
            heartbeat_at = NULL,
            updated_at = clock_timestamp()
        FROM candidates
        WHERE task.task_id = candidates.task_id
        RETURNING 1
    )
    SELECT count(*)::integer INTO reclaimed_count FROM reclaimed;

    RETURN reclaimed_count;
END
$reclaim_expired_durable_tasks$;

CREATE FUNCTION task_health.enqueue_outbox_event(
    requested_event_id uuid,
    requested_source_module text,
    requested_event_type text,
    requested_aggregate_id uuid,
    requested_schema_version integer,
    requested_payload jsonb,
    requested_occurred_at timestamptz,
    requested_max_publish_attempts integer,
    requested_available_at timestamptz,
    requested_created_at timestamptz
) RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $enqueue_outbox_event$
DECLARE
    existing task_health.outbox_event%ROWTYPE;
BEGIN
    INSERT INTO task_health.outbox_event (
        event_id,
        source_module,
        event_type,
        aggregate_id,
        schema_version,
        payload,
        available_at,
        max_publish_attempts,
        occurred_at,
        updated_at
    ) VALUES (
        requested_event_id,
        requested_source_module,
        requested_event_type,
        requested_aggregate_id,
        requested_schema_version,
        requested_payload,
        requested_available_at,
        requested_max_publish_attempts,
        requested_occurred_at,
        requested_created_at
    )
    ON CONFLICT (event_id) DO NOTHING;

    IF FOUND THEN
        RETURN true;
    END IF;

    SELECT candidate.*
    INTO existing
    FROM task_health.outbox_event AS candidate
    WHERE candidate.event_id = requested_event_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION USING
            ERRCODE = '40001',
            MESSAGE = 'concurrent outbox event is not visible; retry the transaction';
    END IF;

    -- A retry cannot reschedule an existing event; availability and write timestamps are not identity fields.
    IF existing.source_module <> requested_source_module
        OR existing.event_type <> requested_event_type
        OR existing.aggregate_id IS DISTINCT FROM requested_aggregate_id
        OR existing.schema_version <> requested_schema_version
        OR existing.payload <> requested_payload
        OR existing.occurred_at <> requested_occurred_at
        OR existing.max_publish_attempts <> requested_max_publish_attempts
    THEN
        RAISE EXCEPTION USING
            ERRCODE = '23505',
            MESSAGE = 'event ID is already bound to a different outbox event';
    END IF;

    RETURN false;
END
$enqueue_outbox_event$;

CREATE FUNCTION task_health.claim_outbox_events(
    requested_publisher text,
    requested_batch_size integer,
    requested_lease_seconds integer
) RETURNS TABLE(
    event_id uuid,
    source_module text,
    event_type text,
    aggregate_id uuid,
    schema_version integer,
    payload jsonb,
    occurred_at timestamptz,
    publish_attempt integer,
    max_publish_attempts integer,
    lease_token uuid,
    lease_generation bigint,
    lease_until timestamptz
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $claim_outbox_events$
BEGIN
    IF requested_publisher IS NULL
        OR requested_publisher !~ '^[a-z][a-z0-9._-]{0,199}$'
        OR requested_batch_size IS NULL
        OR requested_batch_size NOT BETWEEN 1 AND 256
        OR requested_lease_seconds IS NULL
        OR requested_lease_seconds NOT BETWEEN 1 AND 3600
    THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid outbox claim request';
    END IF;

    RETURN QUERY
    WITH candidates AS (
        SELECT candidate.event_id
        FROM task_health.outbox_event AS candidate
        WHERE candidate.state = 'pending'
          AND candidate.available_at <= clock_timestamp()
          AND candidate.publish_attempts < candidate.max_publish_attempts
        ORDER BY candidate.available_at, candidate.occurred_at, candidate.event_id
        FOR UPDATE SKIP LOCKED
        LIMIT requested_batch_size
    )
    UPDATE task_health.outbox_event AS claimed
    SET state = 'leased',
        publish_attempts = claimed.publish_attempts + 1,
        lease_owner = requested_publisher,
        lease_token = gen_random_uuid(),
        lease_generation = claimed.lease_generation + 1,
        lease_until = clock_timestamp() + make_interval(secs => requested_lease_seconds),
        updated_at = clock_timestamp()
    FROM candidates
    WHERE claimed.event_id = candidates.event_id
    RETURNING
        claimed.event_id,
        claimed.source_module,
        claimed.event_type,
        claimed.aggregate_id,
        claimed.schema_version,
        claimed.payload,
        claimed.occurred_at,
        claimed.publish_attempts,
        claimed.max_publish_attempts,
        claimed.lease_token,
        claimed.lease_generation,
        claimed.lease_until;
END
$claim_outbox_events$;

CREATE FUNCTION task_health.mark_outbox_event_published(
    requested_event_id uuid,
    requested_publisher text,
    requested_lease_token uuid,
    requested_lease_generation bigint
) RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $mark_outbox_event_published$
BEGIN
    IF requested_event_id IS NULL
        OR requested_publisher IS NULL
        OR requested_publisher !~ '^[a-z][a-z0-9._-]{0,199}$'
        OR requested_lease_token IS NULL
        OR requested_lease_generation IS NULL
        OR requested_lease_generation <= 0
    THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid outbox publish request';
    END IF;

    UPDATE task_health.outbox_event AS event
    SET state = 'published',
        published_at = clock_timestamp(),
        last_failure_code = NULL,
        lease_owner = NULL,
        lease_token = NULL,
        lease_until = NULL,
        updated_at = clock_timestamp()
    WHERE event.event_id = requested_event_id
      AND event.state = 'leased'
      AND event.lease_owner = requested_publisher
      AND event.lease_token = requested_lease_token
      AND event.lease_generation = requested_lease_generation
      AND event.lease_until >= clock_timestamp();

    RETURN FOUND;
END
$mark_outbox_event_published$;

CREATE FUNCTION task_health.release_outbox_event(
    requested_event_id uuid,
    requested_publisher text,
    requested_lease_token uuid,
    requested_lease_generation bigint,
    requested_failure_code text,
    requested_retry_delay_seconds integer
) RETURNS task_health.outbox_state
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $release_outbox_event$
DECLARE
    resulting_state task_health.outbox_state;
BEGIN
    IF requested_event_id IS NULL
        OR requested_publisher IS NULL
        OR requested_publisher !~ '^[a-z][a-z0-9._-]{0,199}$'
        OR requested_lease_token IS NULL
        OR requested_lease_generation IS NULL
        OR requested_lease_generation <= 0
        OR requested_failure_code IS NULL
        OR requested_failure_code !~ '^[a-z][a-z0-9._-]{0,99}$'
        OR requested_retry_delay_seconds IS NULL
        OR requested_retry_delay_seconds NOT BETWEEN 0 AND 86400
    THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid outbox release request';
    END IF;

    UPDATE task_health.outbox_event AS event
    SET state = CASE
            WHEN event.publish_attempts >= event.max_publish_attempts
                THEN 'dead_lettered'::task_health.outbox_state
            ELSE 'pending'::task_health.outbox_state
        END,
        available_at = CASE
            WHEN event.publish_attempts < event.max_publish_attempts
                THEN clock_timestamp() + make_interval(secs => requested_retry_delay_seconds)
            ELSE event.available_at
        END,
        last_failure_code = requested_failure_code,
        lease_owner = NULL,
        lease_token = NULL,
        lease_until = NULL,
        updated_at = clock_timestamp()
    WHERE event.event_id = requested_event_id
      AND event.state = 'leased'
      AND event.lease_owner = requested_publisher
      AND event.lease_token = requested_lease_token
      AND event.lease_generation = requested_lease_generation
      AND event.lease_until >= clock_timestamp()
    RETURNING event.state INTO resulting_state;

    RETURN resulting_state;
END
$release_outbox_event$;

CREATE FUNCTION task_health.reclaim_expired_outbox_events(requested_batch_size integer)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $reclaim_expired_outbox_events$
DECLARE
    reclaimed_count integer;
BEGIN
    IF requested_batch_size IS NULL OR requested_batch_size NOT BETWEEN 1 AND 1024 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid outbox reclaim batch size';
    END IF;

    WITH candidates AS (
        SELECT candidate.event_id
        FROM task_health.outbox_event AS candidate
        WHERE candidate.state = 'leased'
          AND candidate.lease_until < clock_timestamp()
        ORDER BY candidate.lease_until, candidate.event_id
        FOR UPDATE SKIP LOCKED
        LIMIT requested_batch_size
    ), reclaimed AS (
        UPDATE task_health.outbox_event AS event
        SET state = CASE
                WHEN event.publish_attempts >= event.max_publish_attempts
                    THEN 'dead_lettered'::task_health.outbox_state
                ELSE 'pending'::task_health.outbox_state
            END,
            available_at = CASE
                WHEN event.publish_attempts < event.max_publish_attempts THEN clock_timestamp()
                ELSE event.available_at
            END,
            last_failure_code = 'lease_expired',
            lease_owner = NULL,
            lease_token = NULL,
            lease_until = NULL,
            updated_at = clock_timestamp()
        FROM candidates
        WHERE event.event_id = candidates.event_id
        RETURNING 1
    )
    SELECT count(*)::integer INTO reclaimed_count FROM reclaimed;

    RETURN reclaimed_count;
END
$reclaim_expired_outbox_events$;

CREATE FUNCTION task_health.write_health_status(
    requested_scope_kind task_health.health_scope_kind,
    requested_scope_id uuid,
    requested_component text,
    requested_state task_health.health_state,
    requested_reason_code text,
    requested_observed_at timestamptz,
    requested_written_at timestamptz
) RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, task_health
AS $write_health_status$
BEGIN
    IF requested_observed_at IS NULL OR requested_written_at IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid health timestamp';
    END IF;

    INSERT INTO task_health.health_status (
        scope_kind,
        scope_id,
        component,
        state,
        reason_code,
        observed_at,
        updated_at
    ) VALUES (
        requested_scope_kind,
        requested_scope_id,
        requested_component,
        requested_state,
        requested_reason_code,
        requested_observed_at,
        GREATEST(requested_written_at, requested_observed_at)
    )
    ON CONFLICT (scope_kind, scope_identity, component) DO UPDATE
    SET state = EXCLUDED.state,
        reason_code = EXCLUDED.reason_code,
        observed_at = EXCLUDED.observed_at,
        updated_at = GREATEST(health_status.updated_at, EXCLUDED.updated_at)
    WHERE EXCLUDED.observed_at >= health_status.observed_at;

    RETURN FOUND;
END
$write_health_status$;

-- Runtime callers can observe state and invoke bounded commands, but cannot bypass lease fences.
REVOKE ALL ON ALL TABLES IN SCHEMA task_health FROM assetlibrary_task_health_runtime;
GRANT SELECT ON ALL TABLES IN SCHEMA task_health TO assetlibrary_task_health_runtime;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA task_health FROM PUBLIC;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA task_health TO assetlibrary_task_health_runtime;
