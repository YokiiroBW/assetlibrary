using System.Runtime.CompilerServices;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Modules.LibraryStorage.Infrastructure;

public sealed class PostgresLibraryStore(NpgsqlDataSource dataSource) : ILibraryManagementStore
{
    private readonly LibraryStorageDatabase database = new(dataSource);

    private readonly PostgresLibraryRegistration registration = new(dataSource);
    public ValueTask<LibraryId?> FindRegistrationAsync(LibraryRegistrationRequest request, ManagementOperation operation, CancellationToken token) =>
        registration.FindRegistrationAsync(request, operation, token);
    public ValueTask<LibraryId> RegisterAsync(ConfiguredStorageSource source, LibraryRegistrationRequest request,
        CanonicalLibraryRoot root, ManagementOperation operation, DateTimeOffset now, CancellationToken token) =>
        registration.RegisterAsync(source, request, root, operation, now, token);

    public ValueTask<LibraryScanTarget?> FindAsync(LibraryId libraryId, CancellationToken cancellationToken) =>
        database.RunAsync<LibraryScanTarget?>(async (connection, transaction, token) =>
        {
            await using var command = LibraryStorageDatabase.Command(connection, transaction,
                "SELECT root.storage_source_id,root.canonical_root,source.root_case_sensitive," +
                "CASE WHEN source.availability='offline' THEN 'offline' ELSE root.availability END " +
                "FROM library_storage.library_root root JOIN library_storage.storage_source source USING(storage_source_id) WHERE library_id=$1");
            command.Parameters.Add(new NpgsqlParameter { Value = libraryId.Value });
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? new LibraryScanTarget(libraryId, new StorageSourceId(reader.GetGuid(0)),
                    new CanonicalLibraryRoot(reader.GetString(1), reader.GetBoolean(2) ? RootPathComparison.CaseSensitive : RootPathComparison.CaseInsensitive),
                    reader.GetString(3) == "online" ? StorageAvailability.Online : StorageAvailability.Offline) : null;
        }, cancellationToken);

    public async IAsyncEnumerable<RegisteredLibraryRoot> ListAsync(StorageSourceId storageSourceId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid? cursor = null;
        do
        {
            var batch = await ReadRootsAsync(storageSourceId, cursor, cancellationToken).ConfigureAwait(false);
            foreach (var entry in batch)
            {
                cursor = entry.LibraryId.Value;
                yield return entry;
            }

            if (batch.Count < 100)
            {
                break;
            }
        } while (true);
    }

    private ValueTask<List<RegisteredLibraryRoot>> ReadRootsAsync(StorageSourceId sourceId, Guid? cursor, CancellationToken cancellationToken) =>
        database.RunAsync(async (connection, transaction, token) =>
        {
            await using var command = LibraryStorageDatabase.Command(connection, transaction,
                "SELECT root.library_id,root.canonical_root,source.root_case_sensitive FROM library_storage.library_root root " +
                "JOIN library_storage.storage_source source USING(storage_source_id) WHERE root.storage_source_id=$1 " +
                "AND ($2::uuid IS NULL OR root.library_id>$2) ORDER BY root.library_id LIMIT 100");
            command.Parameters.Add(new NpgsqlParameter { Value = sourceId.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = cursor is null ? DBNull.Value : cursor.Value });
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var result = new List<RegisteredLibraryRoot>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new RegisteredLibraryRoot(new LibraryId(reader.GetGuid(0)), sourceId,
                    new CanonicalLibraryRoot(reader.GetString(1), reader.GetBoolean(2) ? RootPathComparison.CaseSensitive : RootPathComparison.CaseInsensitive)));
            }

            return result;
        }, cancellationToken);

    public async ValueTask SetAvailabilityAsync(LibraryId libraryId, StorageAvailability availability, string? reason, DateTimeOffset now, CancellationToken token)
    {
        _ = await database.RunAsync(async (connection, transaction, ct) =>
        {
            await using var command = LibraryStorageDatabase.Command(connection, transaction,
                "UPDATE library_storage.library_root SET availability=$2,availability_reason=$3,availability_observed_at=$4 WHERE library_id=$1");
            command.Parameters.Add(new NpgsqlParameter { Value = libraryId.Value });
            command.Parameters.Add(new NpgsqlParameter { Value = availability == StorageAvailability.Online ? "online" : "offline" });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = reason is null ? DBNull.Value : reason });
            command.Parameters.Add(new NpgsqlParameter { Value = now });
            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<LibraryId>> NextProbesAsync(DateTimeOffset before, int limit, CancellationToken cancellationToken) =>
        database.RunAsync<IReadOnlyList<LibraryId>>(async (connection, transaction, token) =>
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
            await using var command = LibraryStorageDatabase.Command(connection, transaction,
                "SELECT library_id FROM library_storage.library_root WHERE availability_observed_at<$1 ORDER BY availability_observed_at,library_id LIMIT $2");
            command.Parameters.Add(new NpgsqlParameter { Value = before });
            command.Parameters.Add(new NpgsqlParameter { Value = limit });
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var result = new List<LibraryId>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new LibraryId(reader.GetGuid(0)));
            }

            return result;
        }, cancellationToken);

}
