using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxAccessPolicy
{
    private readonly HashSet<TransferLocationToken> denied = [];
    private readonly HashSet<TransferLocationToken> protectedSources = [];

    public long? AvailableBytesOverride { get; set; }

    public void Deny(TransferLocationToken token) => denied.Add(token);

    public void Protect(TransferLocationToken token) => protectedSources.Add(token);

    public OperationPreflightDecision AccessDecision(OperationItemRequest item)
    {
        if (denied.Contains(item.Source)
            || item.Target.HasValue && denied.Contains(item.Target.Value))
        {
            return OperationPreflightDecision.PermissionDenied;
        }

        return protectedSources.Contains(item.Source)
            ? OperationPreflightDecision.Protected
            : OperationPreflightDecision.Ready;
    }

    public long AvailableBytes(string fixtureRoot)
    {
        if (AvailableBytesOverride.HasValue)
        {
            return AvailableBytesOverride.Value;
        }

        var volumeRoot = Path.GetPathRoot(fixtureRoot)
            ?? throw new InvalidOperationException("The sandbox volume root is unavailable.");
        return new DriveInfo(volumeRoot).AvailableFreeSpace;
    }
}
