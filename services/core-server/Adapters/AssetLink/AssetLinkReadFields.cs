using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

internal static class AssetLinkReadFields
{
    public static int? OptionalInteger(JsonObject body, string propertyName)
    {
        var value = body[propertyName];
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<int>();
        }
        catch (InvalidOperationException error)
        {
            throw new JsonException($"The property {propertyName} has an invalid type.", error);
        }
    }

    public static string RequiredString(JsonObject body, string propertyName)
    {
        var value = body[propertyName]
            ?? throw new JsonException($"The required property {propertyName} is missing.");
        return StringValue(value, propertyName);
    }

    public static string? OptionalString(JsonObject body, string propertyName)
    {
        var value = body[propertyName];
        return value is null ? null : StringValue(value, propertyName);
    }

    public static string StringValue(JsonNode value, string propertyName)
    {
        try
        {
            return value.GetValue<string>();
        }
        catch (InvalidOperationException error)
        {
            throw new JsonException($"The property {propertyName} has an invalid type.", error);
        }
    }
}
