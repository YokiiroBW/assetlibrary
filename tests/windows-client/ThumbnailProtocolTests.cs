using System.Text.Json;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class ThumbnailProtocolTests
{
    private static string Repository
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            { if (File.Exists(Path.Combine(directory.FullName, "global.json"))) { return directory.FullName; } }
            throw new InvalidOperationException("Repository fixture unavailable.");
        }
    }
    [TestMethod]
    [DataRow(DerivedImageProfile.Thumbnail512, "thumbnail")]
    [DataRow(DerivedImageProfile.Preview1600, "preview")]
    public void IndependentFrozenVectorsCoverResponseAndRequestBounds(DerivedImageProfile profile, string name)
    {
        using var data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Repository, $"contracts/windows-shell/{name}-vectors-v1.json")));
        var root = data.RootElement;
        var request = new ThumbnailRequest(root.GetProperty("request_id").GetUInt32(), root.GetProperty("epoch").GetGuid(), root.GetProperty("node").GetGuid());
        foreach (var vector in root.GetProperty("vectors").EnumerateArray())
        {
            var bytes = Convert.FromHexString(vector.GetProperty("hex").GetString()!);
            var valid = vector.GetProperty("valid").GetBoolean();
            if (vector.GetProperty("direction").GetString() == "request")
            {
                if (!valid) { Assert.ThrowsExactly<InvalidDataException>(() => ImageProjectionProtocol.DecodeRequest(profile, bytes)); }
                else { Assert.AreEqual(request, ImageProjectionProtocol.DecodeRequest(profile, bytes)); CollectionAssert.AreEqual(bytes, ImageProjectionProtocol.EncodeRequest(profile, request)); }
            }
            else if (!valid) { Assert.ThrowsExactly<InvalidDataException>(() => ImageProjectionProtocol.DecodeResponse(profile, bytes, request)); }
            else
            {
                var response = ImageProjectionProtocol.DecodeResponse(profile, bytes, request);
                CollectionAssert.AreEqual(bytes, ImageProjectionProtocol.EncodeResponse(profile, request.RequestId, response));
            }
        }
    }
}
