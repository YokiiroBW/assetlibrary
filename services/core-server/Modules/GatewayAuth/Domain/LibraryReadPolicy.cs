using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Domain;

public static class LibraryReadPolicy
{
    public static bool CanRead(LibraryAccessLevel accessLevel) =>
        accessLevel is >= LibraryAccessLevel.ReadOnly and <= LibraryAccessLevel.LibraryAdministrator;
}
