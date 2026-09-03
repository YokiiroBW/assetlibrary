-- Read-only catalog projection for database-boundary auditing, not product authorization.
CREATE VIEW migration.module_privilege_projection AS
SELECT
    ownership.module_name,
    ownership.schema_name,
    ownership.owner_role AS expected_owner_role,
    pg_get_userbyid(namespace.nspowner)::name AS actual_owner_role,
    ownership.runtime_role,
    pg_get_userbyid(namespace.nspowner)::name = ownership.owner_role AS owner_matches,
    has_schema_privilege(
        ownership.runtime_role::text,
        namespace.oid,
        'USAGE'
    ) AS runtime_has_usage,
    has_schema_privilege(
        ownership.runtime_role::text,
        namespace.oid,
        'CREATE'
    ) AS runtime_has_create,
    pg_has_role(
        ownership.runtime_role::text,
        ownership.owner_role::text,
        'MEMBER'
    ) AS runtime_is_owner_member,
    NOT owner_attributes.rolcanlogin AS owner_is_nologin,
    NOT runtime_attributes.rolcanlogin AS runtime_is_nologin,
    NOT EXISTS (
        SELECT 1
        FROM aclexplode(
            COALESCE(namespace.nspacl, acldefault('n', namespace.nspowner))
        ) AS access
        WHERE access.grantee = 0
          AND access.privilege_type IN ('USAGE', 'CREATE')
    ) AS public_is_denied,
    (
        pg_get_userbyid(namespace.nspowner)::name = ownership.owner_role
        AND has_schema_privilege(
            ownership.runtime_role::text,
            namespace.oid,
            'USAGE'
        )
        AND NOT has_schema_privilege(
            ownership.runtime_role::text,
            namespace.oid,
            'CREATE'
        )
        AND NOT pg_has_role(
            ownership.runtime_role::text,
            ownership.owner_role::text,
            'MEMBER'
        )
        AND NOT owner_attributes.rolcanlogin
        AND NOT runtime_attributes.rolcanlogin
        AND NOT EXISTS (
            SELECT 1
            FROM aclexplode(
                COALESCE(namespace.nspacl, acldefault('n', namespace.nspowner))
            ) AS access
            WHERE access.grantee = 0
              AND access.privilege_type IN ('USAGE', 'CREATE')
        )
    ) AS is_clean
FROM migration.module_ownership AS ownership
JOIN pg_namespace AS namespace
  ON namespace.nspname = ownership.schema_name
JOIN pg_roles AS owner_attributes
  ON owner_attributes.rolname = ownership.owner_role
JOIN pg_roles AS runtime_attributes
  ON runtime_attributes.rolname = ownership.runtime_role;

ALTER VIEW migration.module_privilege_projection
    OWNER TO assetlibrary_migration_owner;
REVOKE ALL ON migration.module_privilege_projection FROM PUBLIC;
GRANT SELECT ON migration.module_privilege_projection
    TO assetlibrary_database_auditor;
