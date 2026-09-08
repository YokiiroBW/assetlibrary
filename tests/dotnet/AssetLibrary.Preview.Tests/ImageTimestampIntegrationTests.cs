using AssetLibrary.ReadCore.Tests;
using Npgsql;
using NpgsqlTypes;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class ImageTimestampIntegrationTests
{
    [TestMethod]
    public async Task RealPostgresRoundTripPreservesUnchangedImageAdmission()
    {
        var connection = Environment.GetEnvironmentVariable("ASSETLIBRARY_TRIAL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Assert.Inconclusive("The existing owned PostgreSQL runner must supply the trial test connection.");
            return;
        }
        using var fixture = new ImageSourceFixture();
        await using var data = NpgsqlDataSource.Create(connection);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var precise in new[]
        {
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero).AddTicks(7),
            new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(7),
        })
        {
            File.SetLastWriteTimeUtc(fixture.Asset, precise.UtcDateTime);
            var actual = File.GetLastWriteTimeUtc(fixture.Asset);
            await using var command = data.CreateCommand("SELECT @instant::timestamptz");
            command.CommandTimeout = 5;
            command.Parameters.AddWithValue("instant", NpgsqlDbType.TimestampTz, actual);
            var stored = (DateTime)(await command.ExecuteScalarAsync(timeout.Token))!;
            using var source = fixture.Open();
            using var output = new MemoryStream();
            _ = source.CopyVerifiedTo(output, fixture.Bytes.Length, stored, 4096, timeout.Token);
            CollectionAssert.AreEqual(fixture.Bytes, output.ToArray());
        }
    }
}
