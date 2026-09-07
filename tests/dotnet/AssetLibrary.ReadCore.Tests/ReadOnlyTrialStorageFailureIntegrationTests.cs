using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialStorageFailureIntegrationTests
{
    [TestMethod]
    public async Task DatabaseCapacityFailureKeepsTheSourceAndPublishesNoPartialIndex()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var library = await fixture.RegisterAsync();
        var before = fixture.Sandbox.CaptureStrongSnapshot();
        await fixture.SqlAsync($"""
            CREATE FUNCTION asset_identity.test_capacity_failure() RETURNS trigger LANGUAGE plpgsql AS $test$
            BEGIN
              IF NEW.library_id = '{library.Value:D}'::uuid THEN
                RAISE EXCEPTION USING ERRCODE='53100', MESSAGE='isolated capacity fault';
              END IF;
              RETURN NEW;
            END $test$;
            CREATE TRIGGER test_capacity BEFORE INSERT ON asset_identity.scan_observation_stage
            FOR EACH ROW EXECUTE FUNCTION asset_identity.test_capacity_failure();
            """);
        try
        {
            await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
            await fixture.Scans.RunNextAsync(CancellationToken.None);
            Assert.AreEqual(DurableTaskState.Failed, (await fixture.Scans.GetAsync(library, CancellationToken.None))!.State);
            Assert.IsNull(await fixture.Snapshots.FindAsync(library, CancellationToken.None));
            Assert.AreEqual(before, fixture.Sandbox.CaptureStrongSnapshot());
        }
        finally
        {
            await fixture.SqlAsync("DROP TRIGGER test_capacity ON asset_identity.scan_observation_stage; DROP FUNCTION asset_identity.test_capacity_failure();");
        }

        await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        await fixture.Scans.RunNextAsync(CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Succeeded, (await fixture.Scans.GetAsync(library, CancellationToken.None))!.State);
    }
}
