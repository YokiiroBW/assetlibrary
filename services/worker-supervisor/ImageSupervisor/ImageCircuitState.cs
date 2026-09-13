using System.Buffers.Binary;

namespace AssetLibrary.ImageSupervisor;

internal sealed class ImageCircuitState
{
    private const uint Magic = 0x31435349;
    internal int Failures { get; private set; }
    internal bool Open => Failures >= 3;
    internal static ImageCircuitState Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 8 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
            throw new InvalidDataException("Circuit state is invalid.");
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        if (count is < 0 or > 3) throw new InvalidDataException("Circuit count is invalid.");
        return new ImageCircuitState { Failures = count };
    }
    internal void ReserveAttempt()
    {
        if (Open) throw new InvalidOperationException("Image circuit is open.");
        ++Failures;
    }
    internal static bool InfrastructureCancellation(bool observedClientClosure, bool operatorStopping) => !observedClientClosure && !operatorStopping;
    internal void CompleteHealthy() => Failures = 0;
    internal byte[] Encode()
    {
        var bytes = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Failures); return bytes;
    }
}
