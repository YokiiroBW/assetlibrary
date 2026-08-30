#!/usr/bin/env python3
"""Small, transaction-scoped migration runner used by the PostgreSQL spike."""
import argparse, hashlib, pathlib, subprocess, sys

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--socket', required=True); ap.add_argument('--database', required=True)
    ap.add_argument('--migration-dir', default=None)
    ns = ap.parse_args()
    root = pathlib.Path(__file__).resolve().parents[3]
    directory = pathlib.Path(ns.migration_dir) if ns.migration_dir else root/'database/migrations'
    base = ['psql','-h',ns.socket,'-d',ns.database,'-X','-v','ON_ERROR_STOP=1','-q','-At']
    for path in sorted(directory.glob('*.sql')):
        version = int(path.name[:3]); body = path.read_text(); checksum = hashlib.sha256(body.encode()).hexdigest()
        # Lock, ledger validation, migration body and ledger write are one transaction.
        sql = ("BEGIN; SELECT pg_advisory_xact_lock(778005); "
               f"CREATE SCHEMA IF NOT EXISTS migration; "
               f"CREATE TABLE IF NOT EXISTS migration.ledger (version integer PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now()); "
               f"DO $$ BEGIN IF EXISTS (SELECT 1 FROM migration.ledger WHERE version={version} AND checksum <> '{checksum}') THEN RAISE EXCEPTION 'migration checksum drift: {version}'; END IF; END $$; "
               f"{body}\nINSERT INTO migration.ledger(version,checksum) VALUES ({version},'{checksum}') ON CONFLICT (version) DO NOTHING; COMMIT;")
        subprocess.run(base, input=sql, text=True, check=True)
    return 0

if __name__ == '__main__':
    try: raise SystemExit(main())
    except subprocess.CalledProcessError as exc: print(exc, file=sys.stderr); raise
