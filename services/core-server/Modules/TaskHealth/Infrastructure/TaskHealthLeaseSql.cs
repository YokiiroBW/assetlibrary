using AssetLibrary.Infrastructure.Postgres;
using AssetLibrary.Modules.TaskHealth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.TaskHealth.Infrastructure;

internal static class TaskHealthLeaseSql
{
    public static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, DurableTaskHeartbeatRequest request) =>
        ModulePostgresSession.Command(connection, transaction, sql, request.TaskId.Value, request.Identity.Owner.Value,
            request.Identity.Token.Value, request.Identity.Generation, checked((int)request.LeaseDuration.TotalSeconds));
}
