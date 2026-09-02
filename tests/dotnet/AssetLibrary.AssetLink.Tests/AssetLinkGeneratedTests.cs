using System.Text.Json.Nodes;
using AssetLibrary.AssetLink;

namespace AssetLibrary.AssetLink.Tests;

[TestClass]
public sealed class AssetLinkGeneratedTests
{
    [TestMethod]
    public void KnownMessageKeepsUnknownFieldsAcrossRoundTrip()
    {
        const string json = """
            {
              "message_type": "control.request",
              "request_id": "request-1",
              "operation": "future.operation",
              "body": {},
              "future_field": { "answer": 42 }
            }
            """;

        var parsed = AssetLinkCodec.Parse(json);

        Assert.IsInstanceOfType<ControlRequestMessage>(parsed);
        var request = (ControlRequestMessage)parsed;
        Assert.AreEqual("future.operation", request.Operation);
        Assert.AreEqual(AssetLinkMessageKind.ControlRequest, AssetLinkCodec.Classify(parsed.MessageType));
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(parsed.ToJson())));
    }

    [TestMethod]
    public void UnknownMessageUsesExplicitUnknownBranchAndRoundTrips()
    {
        const string json = """{"message_type":"future.message","future_enum":"new-value"}""";

        var parsed = AssetLinkCodec.Parse(json);

        Assert.IsInstanceOfType<UnknownAssetLinkMessage>(parsed);
        Assert.AreEqual(AssetLinkMessageKind.Unknown, AssetLinkCodec.Classify(parsed.MessageType));
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(parsed.ToJson())));
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("1")]
    [DataRow("18446744073709551615")]
    public void Uint64AcceptsCanonicalBoundaries(string wireValue)
    {
        var parsed = AssetLinkUInt64.Parse(wireValue);

        Assert.AreEqual(wireValue, AssetLinkUInt64.Format(parsed));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("+1")]
    [DataRow("-1")]
    [DataRow("00")]
    [DataRow("01")]
    [DataRow("18446744073709551616")]
    [DataRow("100000000000000000000")]
    public void Uint64RejectsNonCanonicalOrOverflowValues(string wireValue)
    {
        Assert.ThrowsExactly<FormatException>(() => AssetLinkUInt64.Parse(wireValue));
    }

    [TestMethod]
    public void MissingMessageTypeFailsClosed()
    {
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => AssetLinkCodec.Parse("{}"));
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => AssetLinkCodec.Parse("[]"));
        Assert.ThrowsExactly<System.InvalidOperationException>(() => AssetLinkCodec.Parse("{\"message_type\":42}"));
    }
}
