using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class SessionStorageTests
{
    [TestMethod]
    public async Task RememberIsOptInEncryptedAndRemovedByForget()
    {
        var path = Path.Combine(Path.GetTempPath(), "AssetLibrarySessionTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new UserConnectionStore(path);
            var input = new ConnectionInput("https://fixture.example", null, "fixture", "synthetic-storage-secret", false);
            await store.SaveAsync(input, CancellationToken.None);
            Assert.IsNull(await store.LoadRememberedAsync(CancellationToken.None));
            var config = await File.ReadAllTextAsync(Path.Combine(path, "connection.json"));
            Assert.DoesNotContain(input.Password, config);
            Assert.DoesNotContain("password", config);
            await store.SaveAsync(input with { RememberLogin = true }, CancellationToken.None);
            Assert.AreEqual(input.Password, (await store.LoadRememberedAsync(CancellationToken.None))!.Password);
            var encrypted = await File.ReadAllBytesAsync(Path.Combine(path, "remembered.bin"));
            Assert.DoesNotContain(input.Password, Encoding.UTF8.GetString(encrypted));
            var security = new DirectoryInfo(path).GetAccessControl();
            Assert.AreEqual(LocalPipe.UserSid, security.GetOwner(typeof(SecurityIdentifier))!.Value);
            Assert.IsTrue(security.AreAccessRulesProtected);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            { Assert.AreEqual(LocalPipe.UserSid, rule.IdentityReference.Value); }
            store.DeleteRemembered();
            Assert.IsNull(await store.LoadRememberedAsync(CancellationToken.None));
        }
        finally { if (Directory.Exists(path)) { Directory.Delete(path, true); } }
    }

    [TestMethod]
    public void CorruptDpapiBlobIsRejected()
    {
        var protectedBytes = UserDataProtection.Protect("synthetic-secret"u8.ToArray());
        protectedBytes[^1] ^= 0x55;
        Assert.ThrowsExactly<CryptographicException>(() => UserDataProtection.Unprotect(protectedBytes));
    }

    [TestMethod]
    public async Task BroadlyReadableExistingDirectoryIsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), "AssetLibrarySessionTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var store = new UserConnectionStore(path);
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => store.LoadSettingsAsync(CancellationToken.None));
        }
        finally { Directory.Delete(path, true); }
    }
}
