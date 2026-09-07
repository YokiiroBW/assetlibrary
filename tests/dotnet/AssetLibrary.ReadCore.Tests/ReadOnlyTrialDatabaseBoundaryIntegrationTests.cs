using AssetLibrary.Infrastructure.Postgres;
using Npgsql;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialDatabaseBoundaryIntegrationTests
{
    [TestMethod]
    [DataRow(ModuleDatabaseRole.LibraryStorage, "assetlibrary_library_storage_runtime", "DELETE FROM task_health.durable_task WHERE false")]
    [DataRow(ModuleDatabaseRole.ScanReconciliation, "assetlibrary_scan_reconciliation_runtime", "DELETE FROM asset_identity.filesystem_entry WHERE false")]
    [DataRow(ModuleDatabaseRole.TaskHealth, "assetlibrary_task_health_runtime", "DELETE FROM library_storage.library_root WHERE false")]
    public async Task SharedConnectionMechanicsPreserveEveryModuleRoleBoundary(ModuleDatabaseRole role, string expectedRole, string forbiddenSql)
    {
        await using var dataSource = NpgsqlDataSource.Create(ReadOnlyTrialFixture.RequiredEnvironment("ASSETLIBRARY_TRIAL_TEST_CONNECTION"));
        var session = new ModulePostgresSession(dataSource, role);
        var current = await session.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = ModulePostgresSession.Command(connection, transaction, "SELECT current_user::text");
            return await command.ExecuteScalarAsync(token);
        }, CancellationToken.None);
        Assert.AreEqual(expectedRole, current);
        var denied = await Assert.ThrowsExactlyAsync<PostgresException>(async () => await session.RunAsync(
            async (connection, transaction, token) =>
            {
                await using var command = ModulePostgresSession.Command(connection, transaction, forbiddenSql);
                return await command.ExecuteNonQueryAsync(token);
            }, CancellationToken.None));
        Assert.AreEqual(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        await using var clean = dataSource.CreateCommand("SELECT current_user::text");
        Assert.AreNotEqual(expectedRole, await clean.ExecuteScalarAsync());
    }
}
