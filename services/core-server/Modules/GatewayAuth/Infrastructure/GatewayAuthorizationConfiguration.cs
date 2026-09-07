namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

public sealed record GatewayAuthorizationConfiguration
{
    public GatewayAuthorizationConfiguration(string keyFilePath, Guid deploymentId)
    {
        if (string.IsNullOrWhiteSpace(keyFilePath) || !Path.IsPathFullyQualified(keyFilePath)
            || deploymentId == Guid.Empty)
        {
            throw new ArgumentException("The gateway authorization configuration is invalid.");
        }

        KeyFilePath = Path.GetFullPath(keyFilePath);
        DeploymentId = deploymentId;
    }

    public string KeyFilePath { get; }

    public Guid DeploymentId { get; }

    public override string ToString() => "[gateway authorization configuration]";
}
