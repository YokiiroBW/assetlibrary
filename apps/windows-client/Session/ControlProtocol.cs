using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetLibrary.Windows.Session;

public enum ConnectionState { Unconfigured, Disconnected, Connecting, Connected, AccessDenied, Unavailable }
public enum ControlOperation { Status, Connect, Disconnect, Shutdown }
public enum ControlError { InvalidRequest, Busy, Unavailable, AccessDenied, Cancelled, StorageError }
public sealed record ConnectionSettings(string Origin, string? CertificateSha256, string AccountName, bool RememberLogin);
public sealed record ConnectionInput(string Origin, string? CertificateSha256, string AccountName, string Password, bool RememberLogin)
{
    [JsonIgnore]
    public ConnectionSettings Settings => new(Origin, CertificateSha256, AccountName, RememberLogin);
    public override string ToString() => nameof(ConnectionInput);
}
public sealed record ConnectionStatus(ConnectionState State, string? Origin = null, string? CertificateSha256 = null,
    string? AccountName = null, string? DisplayName = null, DateTimeOffset? ExpiresAt = null, bool RememberLogin = false);
public sealed record ControlRequest(int Version, Guid RequestId, ControlOperation Operation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConnectionInput? Connection = null)
{
    public override string ToString() => nameof(ControlRequest);
}
public sealed record ControlResponse(int Version, Guid RequestId, bool Ok, ControlError? ErrorCode, ConnectionStatus Status);

public static class ControlProtocol
{
    public const int MaximumPayload = 16384;
    public static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(12);
    internal static JsonSerializerOptions JsonOptions { get; } = ControlJson.Options;

    public static void Validate(ControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version != 1 || request.RequestId == Guid.Empty || !Enum.IsDefined(request.Operation)
            || (request.Operation == ControlOperation.Connect) != (request.Connection is not null))
        { throw new InvalidDataException("Invalid control request."); }
        if (request.Connection is { } connection)
        {
            ValidateText(connection.Origin, 2048);
            ValidateText(connection.AccountName, 256);
            ValidateText(connection.Password, 4096);
            if (connection.CertificateSha256 is not null) { ValidateText(connection.CertificateSha256, 256); }
        }
    }

    private static void ValidateText(string? text, int maximum)
    {
        if (string.IsNullOrEmpty(text) || text.Length > maximum || text.Any(char.IsControl))
        { throw new InvalidDataException("Invalid connection field."); }
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 2 or > MaximumPayload) { throw new InvalidDataException("Invalid control frame size."); }
        var bytes = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            return ControlJson.Deserialize<T>(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            if (bytes.Length > MaximumPayload) { throw new InvalidDataException("Control frame too large."); }
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            await stream.WriteAsync(header, token).ConfigureAwait(false);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
