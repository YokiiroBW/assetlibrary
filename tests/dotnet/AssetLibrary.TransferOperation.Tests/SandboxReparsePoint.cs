using System.Diagnostics;

namespace AssetLibrary.TransferOperation.Tests;

internal static class SandboxReparsePoint
{
    public static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        using var process = Process.Start(CreateJunctionStartInfo(link, target))
            ?? throw new InvalidOperationException("Unable to start the junction helper.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to create the test junction (exit {process.ExitCode}).");
        }
    }

    private static ProcessStartInfo CreateJunctionStartInfo(
        string link,
        string target)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        return startInfo;
    }
}
