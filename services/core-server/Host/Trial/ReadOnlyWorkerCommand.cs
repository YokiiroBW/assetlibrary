using System.Text;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class ReadOnlyWorkerCommand
{
    public static ReadOnlyWorkerProcessOptions ProcessOptions()
    {
        var executable = Environment.ProcessPath ?? throw new TrialConfigurationException("trial_executable_unavailable");
        var arguments = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new[] { typeof(CoreServerHost).Assembly.Location } : [];
        return new ReadOnlyWorkerProcessOptions(executable, arguments,
            probeTimeout: TimeSpan.FromSeconds(2), scanInactivityTimeout: TimeSpan.FromMinutes(1), terminationTimeout: TimeSpan.FromSeconds(5));
    }

    public static async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 2 || arguments[1] is not ("probe" or "scan" or "preview-source"))
        {
            return (int)CoreServerExitCode.InvalidConfiguration;
        }

        Console.InputEncoding = new UTF8Encoding(false, true);
        Console.OutputEncoding = new UTF8Encoding(false, true);
        if (arguments[1] == "preview-source")
        {
            return ImageSourceWorker.Run(Console.OpenStandardInput(), Console.OpenStandardOutput());
        }
        return arguments[1] == "probe"
            ? await LibraryRootProbeWorker.RunAsync(Console.In, Console.Out, CancellationToken.None).ConfigureAwait(false)
            : await ReadOnlyScanWorker.RunAsync(Console.In, Console.Out, CancellationToken.None).ConfigureAwait(false);
    }
}
