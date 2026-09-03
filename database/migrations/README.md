# Production database migrations

`production/manifest.json` is the only production migration entry point. The two SQL files directly under this directory are retained M0-005 Spike evidence and are deliberately not listed by the production manifest.

The production foundation targets PostgreSQL 16.x and is verified against 16.15. It creates no domain tables. It reserves one schema, one NOLOGIN owner role and one NOLOGIN runtime role for each frozen server module, plus a migration ledger and a read-only catalog projection that audits those boundaries.

## Safety model

- `migration_tool.py validate` is the default, connection-free operation.
- Role provisioning is explicit and intended for a fresh AssetLibrary database/isolated test cluster. It never creates a login or stores a password.
- `apply` requires the target database name twice and a backup directory. When work is pending, a verified custom-format dump and its metadata are published together by one same-filesystem directory rename before any DDL.
- `restore` requires the target database name twice and refuses a target containing any user schema or object. It never drops or creates a database.
- A successful restore is followed by a read-back comparison of the restored migration ledger against the SHA-256-referenced backup metadata.
- Connection passwords are accepted only through libpq mechanisms such as `PGPASSWORD` or a password file. They are never command-line arguments or backup metadata.
- PostgreSQL client and server major versions must both equal the manifest major. Commands have bounded connection, lock, statement and process timeouts.

Examples (placeholders only):

```text
python -B database/migrations/production/migration_tool.py validate
python -B database/migrations/production/migration_tool.py status --host 127.0.0.1 --port 5432 --username deploy_login --database assetlibrary --confirm-database assetlibrary --postgres-bin /path/to/postgresql/bin
python -B database/migrations/production/migration_tool.py apply --host 127.0.0.1 --port 5432 --username deploy_login --database assetlibrary --confirm-database assetlibrary --backup-dir /isolated/backup/path --postgres-bin /path/to/postgresql/bin
```

`provision-roles` needs an administrative deployment connection because roles are cluster-global. The deployment login is separately granted direct `CONNECT` on the target database and `assetlibrary_migration_executor WITH INHERIT FALSE, SET TRUE`. Runtime logins are separately granted direct `CONNECT` and exactly the module runtime roles they need. Those login identities and credentials are deployment configuration, not repository data.

All fixed roles are `NOLOGIN NOINHERIT`. One membership is intentionally narrower than that role-level default: `assetlibrary_migration_owner` receives the 11 module-owner roles with `INHERIT TRUE, SET TRUE, ADMIN FALSE` so it can establish their default privileges in the foundation migration. The deployment login still reaches that role only through non-inheriting executor membership, and runtime roles never inherit or set an owner role. Provisioning fails if any fixed role has an unexpected outgoing membership or membership options.

Cross-cluster recovery first applies the same role provisioning to the empty destination database, then restores the trusted archive. A database dump does not include cluster-global roles; the committed, checksum-pinned NOLOGIN role definition is therefore part of the recovery contract. Product users and library permissions are future domain data and will be included in software backups; database login secrets are not.

## Ownership rules for future migrations

- Add a new contiguous manifest entry and never edit an applied file.
- Set `module` and `owner_role` to the single owning module. The runner selects that role; migration SQL must not issue `SET ROLE` or transaction commands.
- A module may create/write only objects in its own schema. Cross-module reads require a separately reviewed, explicitly granted read-only projection owned by the source module.
- `migration`, its ledger and the audit projection remain exclusive to `database-migration-owner`.
- Business tables for Library/Asset/Scan and TaskHealth/Outbox belong to V01-004 and V01-005. The M0-005 `library`, `tasks` and `events` objects are not production tables.

Official references: PostgreSQL [versioning policy](https://www.postgresql.org/support/versioning/), [role membership](https://www.postgresql.org/docs/16/role-membership.html), [`GRANT`](https://www.postgresql.org/docs/16/sql-grant.html), [`pg_dump`](https://www.postgresql.org/docs/16/app-pgdump.html), and [`pg_restore`](https://www.postgresql.org/docs/16/app-pgrestore.html). PostgreSQL 16.15 is supported through 2028-11-09; the Windows task-local evidence uses the EDB binary archive linked from the PostgreSQL Windows download page.
