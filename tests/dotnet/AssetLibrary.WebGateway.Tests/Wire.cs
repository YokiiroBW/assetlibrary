using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting.Trial;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// Key-level assertions for a JSON answer, shared by the dedup wire cases. Both sides of this contract
/// are exact: the page's decoder reads a fixed set of keys with fixed JSON token kinds, so a case that
/// only checked a few values would miss a renamed key or a number that arrives as a string.
/// </summary>
internal static class Wire
{
    /// <summary>
    /// Serializes and parses again, so a case asserts the text the browser actually receives rather
    /// than the in-memory node the Host happened to build.
    /// </summary>
    public static JsonObject RoundTrip(JsonObject payload) =>
        JsonNode.Parse(TrialDedupJson.Serialize(payload))!.AsObject();

    public static void Exact(JsonNode? node, params string[] keys)
    {
        Assert.IsInstanceOfType<JsonObject>(node, "the answer must be a JSON object");
        var actual = node!.AsObject().Select(pair => pair.Key).ToArray();
        CollectionAssert.AreEquivalent(keys, actual, $"keys: {string.Join(",", actual)}");
    }

    public static JsonObject Node(JsonNode? parent, string key)
    {
        var value = parent?[key];
        Assert.IsInstanceOfType<JsonObject>(value, $"'{key}' must be a JSON object");
        return value!.AsObject();
    }

    public static JsonArray Items(JsonNode? parent, string key)
    {
        var value = parent?[key];
        Assert.IsInstanceOfType<JsonArray>(value, $"'{key}' must be a JSON array");
        return value!.AsArray();
    }

    public static string? Text(JsonNode? node) =>
        node is null || node.GetValueKind() == System.Text.Json.JsonValueKind.Null ? null : node.GetValue<string>();

    public static void Text(JsonNode? parent, string key, string? expected)
    {
        // An absent key and an explicit null mean different things to the page, so presence is
        // asserted before the value: only a stated null may stand for "no value".
        bool present = parent?.AsObject().ContainsKey(key) == true;
        Assert.IsTrue(present, $"'{key}' must be present even when its value is null");
        Assert.AreEqual(expected, Text(parent![key]), $"'{key}' text");
    }
}
