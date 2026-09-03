using AssetLibrary.Modules.TransferSync.Application;

namespace AssetLibrary.TransferOperation.Tests;

internal static class SandboxTransferComposition
{
    public static TransferService CreateService(
        SandboxTransferPayloadPort payloadPort) =>
        TransferTestData.Service(new MemoryTransferStore(), payloadPort);
}
