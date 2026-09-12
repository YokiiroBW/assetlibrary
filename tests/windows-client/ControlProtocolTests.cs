using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class ControlProtocolTests
{
    [TestMethod]
    public async Task CredentialsExistOnlyInConnectRequestAndRoundTripWithoutDiagnosticDisclosure()
    {
        var input = new ConnectionInput("https://fixture.example", null, "fixture", "synthetic-secret", true);
        var request = new ControlRequest(1, Guid.NewGuid(), ControlOperation.Connect, input);
        using var stream = new MemoryStream();
        await ControlProtocol.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        Assert.AreEqual(request, await ControlProtocol.ReadAsync<ControlRequest>(stream, CancellationToken.None));
        Assert.DoesNotContain(input.Password, request.ToString());
        Assert.DoesNotContain(input.Password, input.ToString());
        using var response = new MemoryStream();
        await ControlProtocol.WriteAsync(response, new ControlResponse(1, request.RequestId, true, null,
            new ConnectionStatus(ConnectionState.Connected, input.Origin)), CancellationToken.None);
        var json = Encoding.UTF8.GetString(response.ToArray());
        Assert.DoesNotContain("password", json);
        Assert.DoesNotContain("csrf", json);
        Assert.DoesNotContain(input.Password, json);
    }

    [TestMethod]
    [DataRow("{\"version\":1,\"version\":1,\"request_id\":\"11111111-2222-3333-4444-555555555555\",\"operation\":\"status\"}")]
    [DataRow("{\"version\":1,\"request_id\":\"11111111222233334444555555555555\",\"operation\":\"status\"}")]
    [DataRow("{\"version\":1,\"request_id\":\"11111111-2222-3333-4444-555555555555\",\"operation\":\"status\",\"connection\":null}")]
    [DataRow("{\"version\":1,\"request_id\":\"11111111-2222-3333-4444-555555555555\",\"operation\":\"connect\"}")]
    public async Task MalformedControlShapeIsRejected(string json)
    {
        using var stream = Frame(json);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlProtocol.ReadAsync<ControlRequest>(stream, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("{\"request_id\":\"11111111-2222-3333-4444-555555555555\",\"operation\":\"status\"}")]
    [DataRow("{\"version\":1,\"request_id\":\"11111111-2222-3333-4444-555555555555\",\"operation\":\"status\",\"extra\":true}")]
    public async Task MissingAndUnknownPropertiesAreRejected(string json)
    {
        using var stream = Frame(json);
        await Assert.ThrowsExactlyAsync<JsonException>(() => ControlProtocol.ReadAsync<ControlRequest>(stream, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(16385)]
    [DataRow(-1)]
    public async Task LengthIsRejectedBeforePayloadAllocation(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlProtocol.ReadAsync<ControlRequest>(stream, CancellationToken.None));
    }

    private static MemoryStream Frame(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        stream.Write(header); stream.Write(bytes); stream.Position = 0;
        return stream;
    }
}
