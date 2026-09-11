using System.Text.Json;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.AssetHost;

internal sealed record PrivateProfile(ServerProfile Server, string Account, string Password)
{
    public override string ToString() => nameof(PrivateProfile);

    internal static async Task<PrivateProfile> LoadAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is < 2 or > 16384) { throw new InvalidDataException("Invalid profile size."); }
        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        return Parse(document.RootElement);
    }

    private static PrivateProfile Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Invalid profile schema."); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name) || !ValidProperty(property))
            { throw new InvalidDataException("Invalid profile schema."); }
        }
        string Required(string name, int limit)
        {
            if (!root.TryGetProperty(name, out var value) || value.GetString() is not { Length: > 0 } text
                || text.Length > limit || text.Any(char.IsControl)) { throw new InvalidDataException("Invalid profile schema."); }
            return text;
        }
        var certificate = root.TryGetProperty("certificate_sha256", out var pin) ? pin.GetString() : null;
        return new PrivateProfile(new ServerProfile(Required("origin", 2048), certificate), Required("account_name", 256), Required("password", 4096));
    }

    private static bool Allowed(string name) => name is "origin" or "certificate_sha256" or "account_name" or "password"
        or "invisible_account_name" or "invisible_account_password" or "library_id";

    private static bool ValidProperty(JsonProperty property)
    {
        if (property.Name == "sample_file_count")
        { return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var count) && count is >= 0 and <= 1_000_000; }
        if (property.Name == "expires_at")
        { return property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetDateTimeOffset(out _); }
        return Allowed(property.Name) && property.Value.ValueKind == JsonValueKind.String;
    }
}
