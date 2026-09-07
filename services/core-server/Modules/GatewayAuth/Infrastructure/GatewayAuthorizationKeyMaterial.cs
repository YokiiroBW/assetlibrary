using System.Buffers.Binary;
using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class GatewayAuthorizationKeyMaterial : IDisposable
{
    private const int SerializedLength = 57;
    private byte[]? secret;

    private GatewayAuthorizationKeyMaterial(GatewayAuthorizationKeyState state, byte[] secret)
    {
        State = state;
        this.secret = secret;
    }

    public GatewayAuthorizationKeyState State { get; }

    public ReadOnlySpan<byte> Secret => secret
        ?? throw new ObjectDisposedException(nameof(GatewayAuthorizationKeyMaterial));

    public static GatewayAuthorizationKeyMaterial Create(DateTimeOffset now) =>
        new(new GatewayAuthorizationKeyState(Guid.NewGuid(), now), RandomNumberGenerator.GetBytes(32));

    public byte[] Serialize()
    {
        var bytes = new byte[SerializedLength];
        bytes[0] = 1;
        State.KeyId.TryWriteBytes(bytes.AsSpan(1, 16));
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(17, 8), State.CreatedAt.UtcTicks);
        Secret.CopyTo(bytes.AsSpan(25));
        return bytes;
    }

    public static GatewayAuthorizationKeyMaterial Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SerializedLength || bytes[0] != 1)
        {
            throw new InvalidDataException("The gateway authorization key is invalid.");
        }

        var keyId = new Guid(bytes.Slice(1, 16));
        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(17, 8));
        if (keyId == Guid.Empty || ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks)
        {
            throw new InvalidDataException("The gateway authorization key is invalid.");
        }

        return new GatewayAuthorizationKeyMaterial(
            new GatewayAuthorizationKeyState(keyId, new DateTimeOffset(ticks, TimeSpan.Zero)),
            bytes[25..].ToArray());
    }

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref secret, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public override string ToString() => "[redacted]";
}
