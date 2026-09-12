using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetLibrary.Windows.Session;

internal static class ControlJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();
    internal static T Deserialize<T>(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 5 });
        RejectDuplicateProperties(document.RootElement);
        if (typeof(T) == typeof(ControlRequest)) { ValidateRequestShape(document.RootElement); }
        return document.RootElement.Deserialize<T>(Options) ?? throw new InvalidDataException("Invalid control frame.");
    }
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 5,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }

    private static void ValidateRequestShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("request_id", out var id)
            || id.ValueKind != JsonValueKind.String || !Guid.TryParseExact(id.GetString(), "D", out _)
            || !root.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.String)
        { throw new InvalidDataException("Invalid control request shape."); }
        var hasConnection = root.TryGetProperty("connection", out var connection);
        if (operation.GetString() == "connect" ? !hasConnection || connection.ValueKind != JsonValueKind.Object : hasConnection)
        { throw new InvalidDataException("Unexpected control connection."); }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) { return; }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) { throw new InvalidDataException("Duplicate control field."); }
            RejectDuplicateProperties(property.Value);
        }
    }

}
