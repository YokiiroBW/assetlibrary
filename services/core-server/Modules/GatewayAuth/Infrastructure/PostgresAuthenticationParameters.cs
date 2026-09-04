using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PostgresAuthenticationParameters
{
    public static void AddUuid(NpgsqlCommand command, Guid value) =>
        Add(command, NpgsqlDbType.Uuid, value);

    public static void AddText(NpgsqlCommand command, string value) =>
        Add(command, NpgsqlDbType.Text, value);

    public static void AddBoolean(NpgsqlCommand command, bool value) =>
        Add(command, NpgsqlDbType.Boolean, value);

    public static void AddInteger(NpgsqlCommand command, int value) =>
        Add(command, NpgsqlDbType.Integer, value);

    public static void AddBigint(NpgsqlCommand command, long value) =>
        Add(command, NpgsqlDbType.Bigint, value);

    public static void AddBytes(NpgsqlCommand command, byte[] value) =>
        Add(command, NpgsqlDbType.Bytea, value);

    public static void AddTimestampWithTimeZone(
        NpgsqlCommand command,
        DateTimeOffset value) =>
        Add(command, NpgsqlDbType.TimestampTz, value);

    private static void Add(NpgsqlCommand command, NpgsqlDbType type, object value) =>
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value });
}
