using System.Text;
using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class DatabaseReadinessTests
{
    [TestMethod]
    public void EmbeddedManifestMatchesCurrentProductionContract()
    {
        var contract = DatabaseMigrationContract.LoadCurrent();

        Assert.AreEqual(16, contract.PostgreSqlMajor);
        Assert.AreEqual("assetlibrary_database_auditor", contract.AuditorRole);
        Assert.AreEqual(18, contract.LatestVersion);
        Assert.HasCount(18, contract.Migrations);
        CollectionAssert.AreEqual(
            Enumerable.Range(1, 18).ToArray(),
            contract.Migrations.Select(migration => migration.Version).ToArray());
    }

    [TestMethod]
    public void ManifestParserRejectsInvalidSequenceAndChecksum()
    {
        var invalidSequence = ManifestJson();
        invalidSequence["migrations"]![1]!["version"] = 7;
        var invalidChecksum = ManifestJson();
        invalidChecksum["bootstrap"]!["sha256"] = "not-a-checksum";

        Assert.ThrowsExactly<InvalidDataException>(() => Parse(invalidSequence));
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(invalidChecksum));
    }

    [TestMethod]
    public void StateValidatorAcceptsOnlyAnExactCompleteLedger()
    {
        var contract = DatabaseMigrationContract.LoadCurrent();
        var exact = Snapshot(contract);

        var ready = DatabaseMigrationStateValidator.Validate(contract, exact);
        var pending = DatabaseMigrationStateValidator.Validate(
            contract,
            exact with { Migrations = exact.Migrations.Take(9).ToArray() });
        var extra = DatabaseMigrationStateValidator.Validate(
            contract,
            exact with
            {
                Migrations = [
                    .. exact.Migrations,
                    new AppliedDatabaseMigration(contract.LatestVersion + 1, "unexpected", "unexpected", "assetlibrary_bad_owner", new('a', 64)),
                ],
            });
        var drift = DatabaseMigrationStateValidator.Validate(
            contract,
            exact with
            {
                Migrations = exact.Migrations.Select((migration, index) =>
                    index == 4 ? migration with { Name = "renamed" } : migration).ToArray(),
            });

        Assert.IsTrue(ready.IsReady);
        Assert.AreEqual(contract.LatestVersion, ready.SchemaVersion);
        Assert.AreEqual(DatabaseReadinessStatus.MigrationNotCurrent, pending.Status);
        Assert.AreEqual(DatabaseReadinessStatus.MigrationNotCurrent, extra.Status);
        Assert.AreEqual(DatabaseReadinessStatus.MigrationDrift, drift.Status);
    }

    [TestMethod]
    public void StateValidatorRejectsWrongServerAndBootstrap()
    {
        var contract = DatabaseMigrationContract.LoadCurrent();
        var exact = Snapshot(contract);

        var server = DatabaseMigrationStateValidator.Validate(
            contract,
            exact with { ServerVersionNumber = 170000 });
        var format = DatabaseMigrationStateValidator.Validate(
            contract,
            exact with { BootstrapFormatVersion = 2 });
        var checksum = DatabaseMigrationStateValidator.Validate(
            contract,
            exact with { BootstrapChecksum = new string('f', 64) });

        Assert.AreEqual(DatabaseReadinessStatus.ServerVersionMismatch, server.Status);
        Assert.AreEqual(DatabaseReadinessStatus.BootstrapInvalid, format.Status);
        Assert.AreEqual(DatabaseReadinessStatus.BootstrapInvalid, checksum.Status);
    }

    [TestMethod]
    public void DatabaseReadinessPayloadNeverClaimsBusinessOrWriteReadiness()
    {
        var ready = CoreServerEndpointPayloads.Database(DatabaseReadinessResult.Ready(10));
        var unavailable = CoreServerEndpointPayloads.Database(
            DatabaseReadinessResult.NotReady(DatabaseReadinessStatus.Unavailable));
        var readyJson = CoreServerHostJson.Serialize(ready);
        var unavailableJson = CoreServerHostJson.Serialize(unavailable);

        Assert.AreEqual("host_database", ready.Scope);
        Assert.IsTrue(ready.DatabaseReady);
        Assert.IsFalse(ready.BusinessApiReady);
        Assert.IsFalse(ready.ProductionFileWritesEnabled);
        Assert.AreEqual("not_ready", unavailable.Status);
        Assert.IsFalse(unavailable.DatabaseReady);
        StringAssert.Contains(readyJson, "\"database_contract\":\"v01-010/1\"");
        StringAssert.Contains(unavailableJson, "\"reason\":\"database_not_ready\"");
        Assert.DoesNotContain("Host=", unavailableJson, StringComparison.OrdinalIgnoreCase);
    }

    private static DatabaseMigrationSnapshot Snapshot(DatabaseMigrationContract contract) =>
        new(
            160015,
            1,
            contract.BootstrapChecksum,
            contract.Migrations.Select(migration => new AppliedDatabaseMigration(
                migration.Version,
                migration.Name,
                migration.Module,
                migration.OwnerRole,
                migration.Checksum)).ToArray());

    private static JsonNode ManifestJson()
    {
        using var stream = typeof(DatabaseMigrationContract).Assembly
            .GetManifestResourceStream(DatabaseMigrationContract.ResourceName)!;
        return JsonNode.Parse(stream)!;
    }

    private static DatabaseMigrationContract Parse(JsonNode manifest)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        return DatabaseMigrationContract.Parse(stream);
    }
}
