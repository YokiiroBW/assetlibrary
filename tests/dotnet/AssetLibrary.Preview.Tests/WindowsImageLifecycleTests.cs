using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using AssetLibrary.ReadCore.Tests;

namespace AssetLibrary.Preview.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageLifecycleTests
{
    [TestInitialize]
    public void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows profile lifecycle requires the actual platform.");
    }

    [TestMethod]
    public async Task CancellationBeforeStartupCreatesNoState()
    {
        using var sandbox = new RepositorySandbox();
        var state = Path.Combine(sandbox.Root, "profiles");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => WindowsImageProcess.StartAsync(
            Path.Combine(sandbox.Root, "missing.exe"), state, cancellation.Token));
        Assert.IsFalse(Directory.Exists(state));
    }

    [TestMethod]
    public void SuccessfulProfileDisposalRemovesBothOwnedDirectories()
    {
        using var sandbox = new RepositorySandbox();
        var state = Path.Combine(sandbox.Root, "profiles");
        var profile = WindowsImageProfile.Create(Worker(), state);
        var staging = profile.DirectoryPath;
        var systemProfile = SystemProfilePath(profile.Sid);
        try
        {
            Assert.IsTrue(Directory.Exists(staging));
            Assert.IsTrue(Directory.Exists(systemProfile));
        }
        finally { profile.Dispose(); }
        Assert.IsFalse(Directory.Exists(staging));
        Assert.IsFalse(Directory.Exists(systemProfile));
        AssertNoPendingRecords(state);
        Assert.AreEqual(0, WindowsImageProfile.Recover(state));
    }

    [TestMethod]
    public void CleanupFailureRetainsOwnershipForRecovery()
    {
        using var sandbox = new RepositorySandbox();
        var state = Path.Combine(sandbox.Root, "profiles");
        var profile = WindowsImageProfile.Create(Worker(), state);
        var staging = profile.DirectoryPath;
        using (var held = File.Open(profile.Executable, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsExactly<IOException>(profile.Dispose);
            Assert.AreEqual(1, Directory.EnumerateFiles(state, "*.owner", SearchOption.AllDirectories).Count());
        }
        Assert.AreEqual(1, WindowsImageProfile.Recover(state));
        Assert.IsFalse(Directory.Exists(staging));
        AssertNoPendingRecords(state);
    }

    [TestMethod]
    public void StartupCancellationKeepsItsErrorWhenCleanupAlsoFails()
    {
        using var sandbox = new RepositorySandbox();
        var state = Path.Combine(sandbox.Root, "profiles");
        var profile = WindowsImageProfile.Create(Worker(), state);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using (var held = File.Open(profile.Executable, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var pending = Assert.ThrowsExactly<ImageChildCleanupPendingException>(() => WindowsImageProcess.Start(profile, cancellation.Token));
            Assert.IsInstanceOfType<OperationCanceledException>(pending.InnerException);
            Assert.AreEqual("preview_profile_cleanup_failed", pending.InnerException.Data["windows_image_cleanup_code"]);
            Assert.IsTrue(pending.Completion.IsFaulted);
        }
        Assert.AreEqual(1, WindowsImageProfile.Recover(state));
        AssertNoPendingRecords(state);
    }

    [TestMethod]
    public void RecoveryToleratesAlreadyDeletedOwnedProfileBeforeJournalUpdate()
    {
        using var sandbox = new RepositorySandbox();
        var state = Path.Combine(sandbox.Root, "profiles");
        var profile = WindowsImageProfile.Create(Worker(), state);
        var staging = profile.DirectoryPath;
        var systemProfile = SystemProfilePath(profile.Sid);
        Assert.AreEqual(0, Native.DeleteAppContainerProfile(profile.Name));
        Assert.IsFalse(Directory.Exists(systemProfile));
        Assert.AreEqual(0, Native.DeleteAppContainerProfile(profile.Name));
        // Preserve the older Created record, as a crash between the API and journal update would.
        profile.ReleaseForRecovery();
        Assert.AreEqual(1, WindowsImageProfile.Recover(state));
        Assert.IsFalse(Directory.Exists(staging));
        AssertNoPendingRecords(state);
    }

    [TestMethod]
    public void RecoveryDoesNotAdoptLegacyOrMalformedRecords()
    {
        using var sandbox = new RepositorySandbox();
        var state = Path.Combine(sandbox.Root, "profiles");
        using (var profile = WindowsImageProfile.Create(Worker(), state)) { }
        var legacy = Path.Combine(state, "alimg.legacy.owner");
        File.WriteAllText(legacy, "legacy record must not be adopted");
        Assert.AreEqual(0, WindowsImageProfile.Recover(state));
        Assert.IsTrue(File.Exists(legacy));
        var root = Path.Combine(state, "windows-image-owners-v2");
        var name = "alimg2." + Guid.NewGuid().ToString("N");
        var record = Path.Combine(root, name + ".owner");
        File.WriteAllText(record, "malformed");
        File.WriteAllText(Path.Combine(root, name + ".lease"), string.Empty);
        var before = SHA256.HashData(File.ReadAllBytes(record));
        var failure = Assert.ThrowsExactly<ReadOnlyWorkerException>(() => WindowsImageProfile.Recover(state));
        Assert.AreEqual("preview_owner_record_invalid", failure.Code);
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(record)));
    }

    private static string Worker()
    {
        var executable = Environment.GetEnvironmentVariable("ASSETLIBRARY_IMAGE_WORKER_TEST_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable)) Assert.Inconclusive("Published Windows worker is required.");
        return executable;
    }

    private static void AssertNoPendingRecords(string state)
    {
        Assert.AreEqual(0, Directory.EnumerateFiles(state, "*.owner*", SearchOption.AllDirectories).Count());
        Assert.AreEqual(0, Directory.EnumerateFiles(state, "*.lease", SearchOption.AllDirectories).Count());
    }

    private static string SystemProfilePath(nint sid)
    {
        var result = Native.GetAppContainerFolderPath(new SecurityIdentifier(sid).Value, out var path);
        Assert.AreEqual(0, result);
        try { return Marshal.PtrToStringUni(path)!; }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    private static class Native
    {
        [DllImport("userenv.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int DeleteAppContainerProfile(string name);

        [DllImport("userenv.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int GetAppContainerFolderPath(string sid, out nint path);
    }
}
