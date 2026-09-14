namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public sealed class ServiceReadToken : AuthenticationToken
{
    internal ServiceReadToken(byte[] value) : base(value) { }
    public static ServiceReadToken Parse(string encoded) => new(AuthenticationTokenEncoding.Decode(encoded, nameof(encoded)));
}

public sealed record ServiceReadIdentity(Guid PrincipalId, AuthenticatedSubject Subject);

public sealed record ServiceReadOperation
{
    public ServiceReadOperation(string operatorId, Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(operatorId) || operatorId.Length > 200
            || operatorId.Any(char.IsControl) || correlationId == Guid.Empty)
            throw new ArgumentException("Service operator correlation is invalid.");
        OperatorId = operatorId;
        CorrelationId = correlationId;
    }
    public string OperatorId { get; }
    public Guid CorrelationId { get; }
}

public sealed class IssuedServiceReadCredential(
    Guid credentialId, ServiceReadToken token, DateTimeOffset expiresAt) : IDisposable
{
    public Guid CredentialId { get; } = credentialId;
    public ServiceReadToken Token { get; } = token;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public void Dispose() => Token.Dispose();
    public override string ToString() => "[redacted]";
}
