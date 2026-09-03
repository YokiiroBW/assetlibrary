using System.Security.Cryptography;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal static class PayloadTestData
{
    public static readonly byte[] Payload = "v01-007-transfer-payload"u8.ToArray();

    public static PayloadFacts Facts(byte[]? payload = null)
    {
        var bytes = payload ?? Payload;
        return new(
            bytes.LongLength,
            new Sha256Digest(Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }
}

internal sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
{
    public DateTimeOffset Current { get; set; } = current;

    public override DateTimeOffset GetUtcNow() => Current;
}
