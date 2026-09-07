using Npgsql;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialDatabaseAccess
{
    private const string LoginBoundaryQuery = """
        SELECT COALESCE((
            SELECT r.rolcanlogin AND NOT r.rolsuper AND NOT r.rolinherit
                AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolreplication AND NOT r.rolbypassrls
                AND NOT EXISTS (SELECT 1 FROM pg_database d WHERE d.datname=current_database() AND d.datdba=r.oid)
                AND (SELECT count(*) FROM pg_auth_members m WHERE m.member=r.oid)=1
                AND EXISTS (
                    SELECT 1 FROM pg_auth_members m JOIN pg_roles expected ON expected.oid=m.roleid
                    WHERE m.member=r.oid AND expected.rolname=$1
                        AND m.set_option AND NOT m.inherit_option AND NOT m.admin_option)
            FROM pg_roles r WHERE r.rolname=session_user
        ),false)
        """;

    public static async ValueTask<bool> CheckAsync(TrialDatabaseConnections connections, CancellationToken cancellationToken)
    {
        var modules = new (NpgsqlDataSource Source, string Role)[]
        {
            (connections.Gateway, "assetlibrary_gateway_auth_runtime"),
            (connections.Library, "assetlibrary_library_storage_runtime"),
            (connections.Asset, "assetlibrary_asset_identity_runtime"),
            (connections.Scan, "assetlibrary_scan_reconciliation_runtime"),
            (connections.Task, "assetlibrary_task_health_runtime"),
        };
        foreach (var module in modules)
        {
            await using var connection = await module.Source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(LoginBoundaryQuery, connection) { CommandTimeout = 5 };
            command.Parameters.Add(new NpgsqlParameter { Value = module.Role });
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class TrialRuntimeReadinessProbe(IDatabaseReadinessProbe migrations, TrialDatabaseConnections connections) : IDatabaseReadinessProbe
{
    public async ValueTask<DatabaseReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var result = await migrations.CheckAsync(deadline.Token).ConfigureAwait(false);
            if (!result.IsReady)
            {
                return result;
            }

            return await TrialDatabaseAccess.CheckAsync(connections, deadline.Token).ConfigureAwait(false)
                ? result : DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.PermissionDenied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.Unavailable);
        }
    }
}
