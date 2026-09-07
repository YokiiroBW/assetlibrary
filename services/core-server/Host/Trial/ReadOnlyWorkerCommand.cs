using System.Text;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class ReadOnlyWorkerCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 2 || arguments[1] is not ("probe" or "scan"))
        {
            return (int)CoreServerExitCode.InvalidConfiguration;
        }

        Console.InputEncoding = new UTF8Encoding(false, true);
        Console.OutputEncoding = new UTF8Encoding(false, true);
        return arguments[1] == "probe"
            ? await LibraryRootProbeWorker.RunAsync(Console.In, Console.Out, CancellationToken.None).ConfigureAwait(false)
            : await ReadOnlyScanWorker.RunAsync(Console.In, Console.Out, CancellationToken.None).ConfigureAwait(false);
    }
}
