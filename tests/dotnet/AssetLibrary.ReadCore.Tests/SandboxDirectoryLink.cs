using System.Diagnostics;

namespace AssetLibrary.ReadCore.Tests;

internal static class SandboxDirectoryLink
{
    public static async Task CreateAsync(RepositorySandbox sandbox, string link, string target)
    {
        var prefix = Path.GetFullPath(sandbox.Root) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!Path.GetFullPath(link).StartsWith(prefix, comparison)
            || !Path.GetFullPath(target).StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException("Both directory-link paths must belong to the test fixture.");
        }

        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            sandbox.RegisterDirectoryLink(link);
            return;
        }

        var start = new ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var helper = Process.Start(start)
            ?? throw new InvalidOperationException("The test junction helper did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await helper.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            helper.Kill(entireProcessTree: true);
            throw;
        }

        if (helper.ExitCode != 0)
        {
            throw new InvalidOperationException("The test junction helper failed.");
        }

        sandbox.RegisterDirectoryLink(link);
    }
}
