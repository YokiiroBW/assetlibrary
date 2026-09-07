using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PostgresReadCommand
{
    public static NpgsqlCommand Create(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string commandText,
        TimeSpan timeout) =>
        new(commandText, connection, transaction)
        {
            CommandTimeout = Math.Max(1, checked((int)Math.Ceiling(timeout.TotalSeconds))),
        };

    public static AuthorizedLibrary ReadLibrary(NpgsqlDataReader reader, int offset) =>
        new(
            new LibraryId(reader.GetGuid(offset)),
            reader.GetString(offset + 1),
            Availability(reader.GetString(offset + 2)),
            AccessLevel(reader.GetString(offset + 3)),
            LibraryCategories.Parse(reader.GetString(offset + 4)));

    public static ReadOnlyEntry ReadEntry(
        NpgsqlDataReader reader,
        LibraryId libraryId,
        int offset) =>
        new(
            new StableEntryId(reader.GetGuid(offset)),
            libraryId,
            new RelativeAssetPath(reader.GetString(offset + 1)),
            EntryKind(reader.GetString(offset + 2)),
            reader.IsDBNull(offset + 3) ? null : reader.GetInt64(offset + 3),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(offset + 4), DateTimeKind.Utc)));

    public static AuthorizedSearchHit ReadSearchHit(NpgsqlDataReader reader)
    {
        var library = ReadLibrary(reader, 0);
        var entry = ReadEntry(reader, library.LibraryId, 5);
        var reason = reader.GetString(10) switch
        {
            "name" => SearchHitReason.Name,
            "path" => SearchHitReason.Path,
            _ => throw new InvalidOperationException("The read projection returned an unknown hit reason."),
        };
        return new AuthorizedSearchHit(library, entry, reason);
    }

    public static void Text(NpgsqlCommand command, string value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value,
        });

    public static void NullableText(NpgsqlCommand command, string? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value is null ? DBNull.Value : value,
        });

    public static void Uuid(NpgsqlCommand command, Guid value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = value,
        });

    public static void NullableUuid(NpgsqlCommand command, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = value.HasValue ? value.Value : DBNull.Value,
        });

    public static void Integer(NpgsqlCommand command, int value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Integer,
            Value = value,
        });

    public static void NullableTimestamp(NpgsqlCommand command, DateTimeOffset? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = value is null ? DBNull.Value : value.Value,
        });

    public static void NullableBigint(NpgsqlCommand command, long? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Bigint,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static StorageAvailability Availability(string value) => value switch
    {
        "online" => StorageAvailability.Online,
        "offline" => StorageAvailability.Offline,
        _ => throw new InvalidOperationException("The read projection returned an unknown availability."),
    };

    private static LibraryAccessLevel AccessLevel(string value) => value switch
    {
        "read_only" => LibraryAccessLevel.ReadOnly,
        "read_write" => LibraryAccessLevel.ReadWrite,
        "organize" => LibraryAccessLevel.Organize,
        "library_administrator" => LibraryAccessLevel.LibraryAdministrator,
        _ => throw new InvalidOperationException("The read projection returned an unknown access level."),
    };

    private static AssetEntryKind EntryKind(string value) => value switch
    {
        "file" => AssetEntryKind.File,
        "directory" => AssetEntryKind.Directory,
        "reparse_file" => AssetEntryKind.ReparseFile,
        "reparse_directory" => AssetEntryKind.ReparseDirectory,
        _ => throw new InvalidOperationException("The read projection returned an unknown entry kind."),
    };
}
