using System.Reflection;
using System.Runtime.InteropServices;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed record CoreServerBuildInfo(
    string Contract,
    string SourceRevision,
    string InformationalVersion,
    string Framework,
    string RuntimeIdentifier,
    string ProcessArchitecture)
{
    public const string CurrentContract = "v01-008/1";

    public static CoreServerBuildInfo Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var sourceRevision = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "AssetLibrary.SourceRevision")
            ?.Value;
        return new CoreServerBuildInfo(
            CurrentContract,
            string.IsNullOrWhiteSpace(sourceRevision) ? "unissued" : sourceRevision,
            string.IsNullOrWhiteSpace(version) ? "unknown" : version,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
    }
}
