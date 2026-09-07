-- The transaction retaining this row lock fences the separate module's final commit.
CREATE FUNCTION task_health.lock_durable_task_commit(
    requested_task_id uuid, requested_worker text, requested_token uuid, requested_generation bigint,
    requested_lease_seconds integer
) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, task_health
AS $lock_durable_task_commit$
DECLARE task task_health.durable_task%ROWTYPE;
BEGIN
    IF requested_lease_seconds IS NULL OR requested_lease_seconds NOT BETWEEN 1 AND 3600 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid commit lease duration';
    END IF;
    SELECT current_task.* INTO task FROM task_health.durable_task AS current_task
    WHERE current_task.task_id = requested_task_id FOR UPDATE;
    IF NOT FOUND OR task.state <> 'leased' OR task.lease_owner IS DISTINCT FROM requested_worker
       OR task.lease_token IS DISTINCT FROM requested_token OR task.lease_generation IS DISTINCT FROM requested_generation
       OR task.lease_until < clock_timestamp() THEN RETURN 'not_current'; END IF;
    IF task.cancellation_requested_at IS NOT NULL THEN RETURN 'cancelled'; END IF;
    UPDATE task_health.durable_task SET lease_until = clock_timestamp() + make_interval(secs => requested_lease_seconds)
    WHERE task_id = requested_task_id;
    RETURN 'accepted';
END
$lock_durable_task_commit$;

-- Only the trusted coordinator calls this after matching the immutable snapshot to a task's scan run.
CREATE FUNCTION task_health.reconcile_committed_task(requested_task_id uuid)
RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, task_health
AS $reconcile_committed_task$
BEGIN
    UPDATE task_health.durable_task
    SET state = 'succeeded', last_failure_code = NULL,
        lease_owner = NULL, lease_token = NULL, lease_until = NULL, heartbeat_at = NULL,
        updated_at = clock_timestamp()
    WHERE task_id = requested_task_id AND task_type = 'scan.initial_read_only'
      AND (state <> 'leased' OR lease_until < clock_timestamp());
    RETURN FOUND;
END
$reconcile_committed_task$;

CREATE FUNCTION task_health.claim_durable_tasks_of_type(
    requested_worker text, requested_batch_size integer, requested_lease_seconds integer, requested_task_type text
) RETURNS TABLE(task_id uuid, task_type text, payload jsonb, priority smallint, attempt integer,
    max_attempts integer, lease_token uuid, lease_generation bigint, lease_until timestamptz, cancellation_requested boolean)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, task_health
AS $claim_durable_tasks_of_type$
BEGIN
    IF requested_worker IS NULL OR requested_worker !~ '^[a-z][a-z0-9._-]{0,199}$'
       OR requested_batch_size IS NULL OR requested_batch_size NOT BETWEEN 1 AND 256
       OR requested_lease_seconds IS NULL OR requested_lease_seconds NOT BETWEEN 1 AND 3600
       OR requested_task_type IS NULL OR requested_task_type !~ '^[a-z][a-z0-9._-]{0,99}$' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'invalid task claim request';
    END IF;
    RETURN QUERY WITH candidates AS (
        SELECT task.task_id FROM task_health.durable_task AS task
        WHERE task.state = 'queued' AND task.task_type = requested_task_type
          AND task.cancellation_requested_at IS NULL AND task.available_at <= clock_timestamp()
          AND task.attempts < task.max_attempts
        ORDER BY task.priority, task.available_at, task.created_at, task.task_id
        FOR UPDATE SKIP LOCKED LIMIT requested_batch_size
    ) UPDATE task_health.durable_task AS claimed
    SET state = 'leased', attempts = claimed.attempts + 1, lease_owner = requested_worker,
        lease_token = gen_random_uuid(), lease_generation = claimed.lease_generation + 1,
        lease_until = clock_timestamp() + make_interval(secs => requested_lease_seconds),
        heartbeat_at = clock_timestamp(), updated_at = clock_timestamp()
    FROM candidates WHERE claimed.task_id = candidates.task_id
    RETURNING claimed.task_id, claimed.task_type, claimed.payload, claimed.priority, claimed.attempts,
        claimed.max_attempts, claimed.lease_token, claimed.lease_generation, claimed.lease_until,
        claimed.cancellation_requested_at IS NOT NULL;
END
$claim_durable_tasks_of_type$;

REVOKE ALL ON FUNCTION task_health.lock_durable_task_commit(uuid, text, uuid, bigint, integer),
    task_health.reconcile_committed_task(uuid), task_health.claim_durable_tasks_of_type(text, integer, integer, text)
    FROM PUBLIC;
GRANT EXECUTE ON FUNCTION task_health.lock_durable_task_commit(uuid, text, uuid, bigint, integer),
    task_health.reconcile_committed_task(uuid), task_health.claim_durable_tasks_of_type(text, integer, integer, text)
    TO assetlibrary_task_health_runtime;
