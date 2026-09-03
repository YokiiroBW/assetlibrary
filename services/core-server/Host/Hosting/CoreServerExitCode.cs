namespace AssetLibrary.CoreServer.Hosting;

internal enum CoreServerExitCode
{
    Success = 0,
    InvalidConfiguration = 64,
    Unavailable = 69,
    SoftwareError = 70,
}
