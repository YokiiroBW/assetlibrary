using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetLibrary.CoreServer.Hosting;

internal static class CoreServerHostJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string Error(string code) => Serialize(new CoreServerErrorPayload("error", code));

    private sealed record CoreServerErrorPayload(string Level, string Code);
}
