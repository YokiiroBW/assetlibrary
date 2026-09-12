using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetLibrary.Windows.Setup;

internal static class Product
{
    internal const string Owner = "AssetLibrary.Windows.Explorer";
    internal const string Version = "0.3.0-preview.5";
    internal const string Clsid = "{BBC992DE-CE5D-48C8-A86C-7230C7D72B02}";
    internal const string ManifestName = "manifest.json";
    internal const string OwnerFile = ".assetlibrary-owner.json";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12
    };
    internal static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AssetLibrary");
}

internal sealed class SetupException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}

internal sealed record Ownership(string Owner);
internal sealed record SetupReport(string Status, string? Version, string? InstallDir, string[] PendingCleanup);
