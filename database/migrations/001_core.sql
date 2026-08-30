-- M0-005 candidate only. Each schema owns its tables; cross-module reads use views.
CREATE SCHEMA IF NOT EXISTS migration;
CREATE TABLE IF NOT EXISTS migration.ledger (
  version integer PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now()
);
CREATE SCHEMA IF NOT EXISTS library;
CREATE TABLE IF NOT EXISTS library.physical_library (
  library_id uuid PRIMARY KEY, display_name text NOT NULL, root_marker text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS library.asset (
  asset_id uuid PRIMARY KEY, library_id uuid NOT NULL REFERENCES library.physical_library(library_id),
  relative_path text NOT NULL, filename text NOT NULL, size_bytes bigint NOT NULL CHECK (size_bytes >= 0),
  sha256 bytea NOT NULL CHECK (octet_length(sha256)=32), fast_fingerprint bytea,
  searchable tsvector GENERATED ALWAYS AS (to_tsvector('simple', coalesce(filename,'') || ' ' || coalesce(relative_path,''))) STORED,
  created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(library_id, relative_path)
);
CREATE INDEX IF NOT EXISTS asset_library_path_keyset ON library.asset(library_id, asset_id);
CREATE INDEX IF NOT EXISTS asset_search_gin ON library.asset USING gin(searchable);
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX IF NOT EXISTS asset_filename_trgm ON library.asset USING gin(filename gin_trgm_ops);
CREATE SCHEMA IF NOT EXISTS tasks;
CREATE TABLE IF NOT EXISTS tasks.durable_task (
  task_id uuid PRIMARY KEY, idempotency_key text NOT NULL UNIQUE, task_type text NOT NULL,
  payload jsonb NOT NULL, state text NOT NULL CHECK(state IN ('queued','leased','succeeded','failed','cancelled')) DEFAULT 'queued',
  attempts integer NOT NULL DEFAULT 0 CHECK(attempts >= 0), max_attempts integer NOT NULL DEFAULT 3 CHECK(max_attempts > 0),
  lease_owner text, lease_until timestamptz, heartbeat_at timestamptz, last_error text,
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS task_claim ON tasks.durable_task(state, lease_until, created_at);
CREATE SCHEMA IF NOT EXISTS events;
CREATE TABLE IF NOT EXISTS events.outbox (
  event_id uuid PRIMARY KEY, aggregate_id uuid NOT NULL, event_type text NOT NULL, payload jsonb NOT NULL,
  occurred_at timestamptz NOT NULL DEFAULT now(), published_at timestamptz, publish_attempts integer NOT NULL DEFAULT 0,
  last_error text
);
CREATE INDEX IF NOT EXISTS outbox_unpublished ON events.outbox(occurred_at) WHERE published_at IS NULL;
CREATE OR REPLACE FUNCTION events.claim_one(publisher text, lease_seconds integer DEFAULT 30)
RETURNS TABLE(event_id uuid, payload jsonb) LANGUAGE sql AS $$
WITH c AS (SELECT event_id FROM events.outbox WHERE published_at IS NULL ORDER BY occurred_at FOR UPDATE SKIP LOCKED LIMIT 1)
UPDATE events.outbox o SET publish_attempts=publish_attempts+1,last_error='claimed by '||publisher
FROM c WHERE o.event_id=c.event_id RETURNING o.event_id,o.payload
$$;
CREATE OR REPLACE FUNCTION events.mark_published(p_event uuid)
RETURNS boolean LANGUAGE sql AS $$ UPDATE events.outbox SET published_at=clock_timestamp(),last_error=NULL WHERE event_id=p_event AND published_at IS NULL RETURNING true $$;
CREATE OR REPLACE FUNCTION tasks.claim_one(worker text, lease_seconds integer DEFAULT 30)
RETURNS TABLE(task_id uuid, payload jsonb, lease_until timestamptz) LANGUAGE plpgsql AS $$
BEGIN
  RETURN QUERY
  WITH candidate AS (
    SELECT t.task_id FROM tasks.durable_task t
    WHERE (t.state='queued' OR (t.state='leased' AND t.lease_until < clock_timestamp()))
      AND t.attempts < t.max_attempts
    ORDER BY t.created_at FOR UPDATE SKIP LOCKED LIMIT 1
  ), claimed AS (
    UPDATE tasks.durable_task t SET state='leased', lease_owner=worker,
      lease_until=clock_timestamp() + make_interval(secs => lease_seconds), heartbeat_at=clock_timestamp(),
      attempts=t.attempts+1, updated_at=clock_timestamp()
    FROM candidate c WHERE t.task_id=c.task_id RETURNING t.task_id,t.payload,t.lease_until
  ) SELECT * FROM claimed;
END $$;
CREATE OR REPLACE FUNCTION tasks.heartbeat(p_task uuid, worker text, lease_seconds integer DEFAULT 30)
RETURNS boolean LANGUAGE sql AS $$
UPDATE tasks.durable_task SET lease_until=clock_timestamp()+make_interval(secs => lease_seconds), heartbeat_at=clock_timestamp(), updated_at=clock_timestamp()
WHERE task_id=p_task AND state='leased' AND lease_owner=worker AND lease_until >= clock_timestamp() RETURNING true
$$;
CREATE OR REPLACE FUNCTION tasks.reclaim_expired()
RETURNS integer LANGUAGE sql AS $$
WITH x AS (UPDATE tasks.durable_task SET state=CASE WHEN attempts >= max_attempts THEN 'failed' ELSE 'queued' END,
 lease_owner=NULL,lease_until=NULL,heartbeat_at=NULL,updated_at=clock_timestamp() WHERE state='leased' AND lease_until < clock_timestamp() RETURNING 1)
SELECT count(*)::integer FROM x
$$;
CREATE OR REPLACE FUNCTION tasks.cancel(p_task uuid)
RETURNS boolean LANGUAGE sql AS $$ UPDATE tasks.durable_task SET state='cancelled',lease_owner=NULL,lease_until=NULL,updated_at=clock_timestamp() WHERE task_id=p_task AND state IN ('queued','leased') RETURNING true $$;
