using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PostgresLocalAccountStateReader
{
    public static LocalAccountState? Read(NpgsqlDataReader reader, int startingOrdinal)
    {
        if (reader.IsDBNull(startingOrdinal))
        {
            return null;
        }

        return new LocalAccountState(
            reader.GetGuid(startingOrdinal),
            new LocalAccountName(reader.GetString(startingOrdinal + 1)),
            new LocalAccountDisplayName(reader.GetString(startingOrdinal + 2)),
            reader.GetBoolean(startingOrdinal + 3),
            reader.GetBoolean(startingOrdinal + 4),
            reader.GetInt64(startingOrdinal + 5),
            reader.GetInt64(startingOrdinal + 6));
    }
}
