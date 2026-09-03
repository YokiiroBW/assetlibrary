-- ScanReconciliation owns scan lifecycle and completeness evidence.
CREATE TABLE scan_reconciliation.scan_run (
    scan_id uuid PRIMARY KEY,
    library_id uuid NOT NULL,
    scan_kind text NOT NULL CHECK (scan_kind IN ('initial_read_only')),
    status text NOT NULL CHECK (
        status IN ('running', 'completed', 'discovery_failed', 'cancelled', 'timed_out')
    ),
    observed_entries integer NOT NULL DEFAULT 0 CHECK (observed_entries >= 0),
    committed_entries integer NOT NULL DEFAULT 0 CHECK (committed_entries >= 0),
    failure_code text CHECK (
        failure_code IS NULL OR length(failure_code) BETWEEN 1 AND 100
    ),
    started_at timestamptz NOT NULL,
    finished_at timestamptz,
    CHECK (
        (status = 'running' AND finished_at IS NULL AND failure_code IS NULL)
        OR (status = 'completed' AND finished_at IS NOT NULL AND failure_code IS NULL)
        OR (status IN ('discovery_failed', 'cancelled', 'timed_out')
            AND finished_at IS NOT NULL AND failure_code IS NOT NULL)
    ),
    CHECK (committed_entries <= observed_entries)
);

CREATE INDEX scan_run_library_started_index
    ON scan_reconciliation.scan_run (library_id, started_at DESC, scan_id);

CREATE INDEX scan_run_active_index
    ON scan_reconciliation.scan_run (started_at, scan_id)
    WHERE status = 'running';
