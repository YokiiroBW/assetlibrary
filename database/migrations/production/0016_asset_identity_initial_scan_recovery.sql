-- Published snapshots remain immutable in the initial-only trial.
CREATE FUNCTION asset_identity.find_initial_snapshot(requested_library_id uuid)
RETURNS TABLE(scan_id uuid, entry_count integer, observed_at timestamptz)
LANGUAGE sql STABLE SET search_path = pg_catalog, asset_identity
AS $find_initial_snapshot$
    SELECT snapshot.scan_id, snapshot.entry_count, snapshot.observed_at
    FROM asset_identity.library_index_snapshot AS snapshot
    WHERE snapshot.library_id = requested_library_id
$find_initial_snapshot$;

CREATE FUNCTION asset_identity.lock_initial_scan(requested_library_id uuid)
RETURNS boolean LANGUAGE sql SET search_path = pg_catalog, asset_identity
AS $lock_initial_scan$
    SELECT pg_try_advisory_lock(hashtextextended(requested_library_id::text, 109551016))
$lock_initial_scan$;

CREATE FUNCTION asset_identity.unlock_initial_scan(requested_library_id uuid)
RETURNS boolean LANGUAGE sql SET search_path = pg_catalog, asset_identity
AS $unlock_initial_scan$
    SELECT pg_advisory_unlock(hashtextextended(requested_library_id::text, 109551016))
$unlock_initial_scan$;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA asset_identity FROM PUBLIC;
