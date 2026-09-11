using System.Text.Json;
using System.Text.Json.Nodes;
using AssetLibrary.Windows.AssetHost;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class HostProfileTests
{
    [TestMethod]
    public async Task PrivateFixtureSchemaAcceptsKnownMetadataAndDoesNotPrintCredentials()
    {
        var path = Path.Combine(Path.GetTempPath(), "assethost-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, Profile().ToJsonString());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var profile = await PrivateProfile.LoadAsync(path, deadline.Token);
            Assert.AreEqual("fixture-only", profile.Account);
            Assert.AreEqual("fixture-password", profile.Password);
            Assert.AreEqual("PrivateProfile", profile.ToString());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("unexpected")]
    [DataRow("sample_file_count")]
    [DataRow("expires_at")]
    [DataRow("origin")]
    public async Task InvalidProfileFieldsFailBeforeLogin(string field)
    {
        var path = Path.Combine(Path.GetTempPath(), "assethost-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = Profile();
            settings[field] = "invalid";
            await File.WriteAllTextAsync(path, settings.ToJsonString());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var failure = await Assert.ThrowsAsync<Exception>(() => PrivateProfile.LoadAsync(path, deadline.Token));
            Assert.IsTrue(failure is ArgumentException or InvalidDataException);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task OversizedDuplicateAndIncompleteProfilesAreRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "assethost-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            foreach (var text in new[] { new string(' ', 16385), "{\"origin\":\"a\",\"origin\":\"b\"}", "{}" })
            {
                await File.WriteAllTextAsync(path, text, deadline.Token);
                await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PrivateProfile.LoadAsync(path, deadline.Token));
            }
            await File.WriteAllTextAsync(path, "[[", deadline.Token);
            await Assert.ThrowsAsync<JsonException>(() => PrivateProfile.LoadAsync(path, deadline.Token));
        }
        finally { File.Delete(path); }
    }

    private static JsonObject Profile() => new()
    {
        ["origin"] = "https://fixture.example:5443",
        ["account_name"] = "fixture-only",
        ["password"] = "fixture-password",
        ["sample_file_count"] = 104,
        ["expires_at"] = "2026-09-12T12:00:00Z",
    };
}
