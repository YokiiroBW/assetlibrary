using System.Text.Json;
using AssetLibrary.Windows.AssetHost;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class ThumbnailProtocolTests
{
    [TestMethod]
    public void IndependentFrozenVectorsCoverResponseAndRequestBounds()
    {
        using var data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ThumbnailTestSupport.Repository, "contracts/windows-shell/thumbnail-vectors-v1.json")));
        var root = data.RootElement;
        var request = new ThumbnailRequest(root.GetProperty("request_id").GetUInt32(), root.GetProperty("epoch").GetGuid(), root.GetProperty("node").GetGuid());
        foreach (var vector in root.GetProperty("vectors").EnumerateArray())
        {
            var bytes = Convert.FromHexString(vector.GetProperty("hex").GetString()!);
            var valid = vector.GetProperty("valid").GetBoolean();
            if (vector.GetProperty("direction").GetString() == "request")
            {
                if (!valid) { Assert.ThrowsExactly<InvalidDataException>(() => ThumbnailProtocol.DecodeRequest(bytes)); }
                else { Assert.AreEqual(request, ThumbnailProtocol.DecodeRequest(bytes)); CollectionAssert.AreEqual(bytes, ThumbnailProtocol.EncodeRequest(request)); }
            }
            else if (!valid) { Assert.ThrowsExactly<InvalidDataException>(() => ThumbnailProtocol.DecodeResponse(bytes, request)); }
            else
            {
                var response = ThumbnailProtocol.DecodeResponse(bytes, request);
                CollectionAssert.AreEqual(bytes, ThumbnailProtocol.EncodeResponse(request.RequestId, response));
            }
        }
    }
}
