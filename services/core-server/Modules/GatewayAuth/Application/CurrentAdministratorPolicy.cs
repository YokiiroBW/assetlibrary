using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public static class CurrentAdministratorPolicy
{
    public static bool Allows(AuthenticatedIdentity? currentIdentity) =>
        currentIdentity is { IsSystemAdministrator: true };
}
