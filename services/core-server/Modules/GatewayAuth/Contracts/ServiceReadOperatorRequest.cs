namespace AssetLibrary.Modules.GatewayAuth.Contracts;

public enum ServiceReadOperatorAction { Create, Grant, Ungrant, Issue, Rotate, Revoke, Disable }

public sealed class ServiceReadOperatorRequest
{
    public ServiceReadOperatorRequest(Guid authorizationId, Guid operationId, ServiceReadOperatorAction action,
        Guid principalId, string operatorId, DateTimeOffset expiresAt, string? displayName = null,
        IEnumerable<Guid>? libraryIds = null, Guid? credentialId = null, int? lifetimeDays = null)
    {
        var libraries = libraryIds?.ToArray() ?? [];
        if (authorizationId == Guid.Empty || operationId == Guid.Empty || principalId == Guid.Empty
            || !Enum.IsDefined(action) || string.IsNullOrWhiteSpace(operatorId) || operatorId.Length > 200
            || operatorId.Any(char.IsControl) || libraries.Length > 100 || libraries.Any(id => id == Guid.Empty)
            || libraries.Distinct().Count() != libraries.Length
            || !ValidActionFields(action, displayName, libraries, credentialId, lifetimeDays))
        {
            throw new ArgumentException("Invalid service operator request.");
        }
        AuthorizationId = authorizationId;
        OperationId = operationId;
        Action = action;
        PrincipalId = principalId;
        OperatorId = operatorId;
        ExpiresAt = expiresAt.ToUniversalTime();
        DisplayName = displayName;
        LibraryIds = Array.AsReadOnly(libraries.Order().ToArray());
        CredentialId = credentialId;
        LifetimeDays = lifetimeDays;
    }

    private static bool ValidActionFields(ServiceReadOperatorAction action, string? displayName,
        Guid[] libraries, Guid? credentialId, int? lifetimeDays) =>
        !((action == ServiceReadOperatorAction.Create) != (displayName is not null)
            || (displayName is not null && (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200 || displayName.Any(char.IsControl)))
            || (action is ServiceReadOperatorAction.Grant or ServiceReadOperatorAction.Ungrant) != (libraries.Length > 0)
            || (action is ServiceReadOperatorAction.Rotate or ServiceReadOperatorAction.Revoke) != credentialId.HasValue
            || credentialId == Guid.Empty || lifetimeDays is < 1 or > 365
            || (lifetimeDays.HasValue && action is not (ServiceReadOperatorAction.Issue or ServiceReadOperatorAction.Rotate)));

    public Guid AuthorizationId { get; }
    public Guid OperationId { get; }
    public ServiceReadOperatorAction Action { get; }
    public Guid PrincipalId { get; }
    public string OperatorId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string? DisplayName { get; }
    public IReadOnlyList<Guid> LibraryIds { get; }
    public Guid? CredentialId { get; }
    public int? LifetimeDays { get; }
}
