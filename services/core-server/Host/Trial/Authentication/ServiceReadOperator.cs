using System.Text.Json;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal static class ServiceReadOperator
{
    public static async Task<TrialOperatorResult> ExecuteAsync(string action, IServiceProvider services,
        Stream input, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var request = await ServiceReadOperatorInput.ReadAsync(action, input, deadline.Token).ConfigureAwait(false);
            var authorization = services.GetRequiredService<IServiceReadOperatorAuthorization>();
            using var proof = await authorization.IssueAsync(request, deadline.Token).ConfigureAwait(false);
            if (!await authorization.VerifyAsync(request, proof, deadline.Token).ConfigureAwait(false))
                return Error("authorization_rejected");
            var credentials = services.GetRequiredService<ServiceReadAuthenticationService>();
            var operation = new ServiceReadOperation(request.OperatorId, request.OperationId);
            if (request.Action is ServiceReadOperatorAction.Grant or ServiceReadOperatorAction.Ungrant)
            {
                await services.GetRequiredService<IServiceLibraryReadGrantManagement>().SetAsync(
                    new ServiceLibraryReadGrantChange(request.PrincipalId, request.LibraryIds.Select(id => new LibraryId(id)).ToArray(),
                        request.Action == ServiceReadOperatorAction.Grant),
                    new ServiceLibraryReadGrantOperation(request.OperatorId, request.OperationId), deadline.Token).ConfigureAwait(false);
                return Applied(request);
            }
            if (request.Action is ServiceReadOperatorAction.Issue or ServiceReadOperatorAction.Rotate)
            {
                if (!services.GetRequiredService<TrialConfiguration>().ServiceReadEnabled) return Error("service_read_disabled");
                using var issued = await credentials.IssueAsync(request.PrincipalId, request.CredentialId,
                    request.LifetimeDays is { } days ? TimeSpan.FromDays(days) : null, operation, deadline.Token).ConfigureAwait(false);
                if (issued is null) return Error("state_conflict");
                return new TrialOperatorResult(0, JsonSerializer.Serialize(new
                {
                    outcome = "applied",
                    principal_id = request.PrincipalId,
                    credential_id = issued.CredentialId,
                    expires_at = issued.ExpiresAt,
                    token = issued.Token.Export(),
                }));
            }
            var applied = request.Action switch
            {
                ServiceReadOperatorAction.Create => await credentials.CreatePrincipalAsync(request.PrincipalId, request.DisplayName!, operation, deadline.Token).ConfigureAwait(false),
                ServiceReadOperatorAction.Revoke => await credentials.RevokeAsync(request.PrincipalId, request.CredentialId!.Value, operation, deadline.Token).ConfigureAwait(false),
                ServiceReadOperatorAction.Disable => await credentials.DisableAsync(request.PrincipalId, operation, deadline.Token).ConfigureAwait(false),
                _ => false,
            };
            return applied ? Applied(request) : Error("state_conflict");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return Error("authorization_rejected"); }
        catch (ReadOnlyTrialException error) { return Error(error.Code); }
        catch (Exception error) when (error is ArgumentException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return Error("invalid_request");
        }
        catch (Exception)
        {
            return new TrialOperatorResult((int)CoreServerExitCode.Unavailable,
                JsonSerializer.Serialize(new { code = "service_unavailable" }));
        }
    }

    private static TrialOperatorResult Applied(ServiceReadOperatorRequest request) => new(0,
        JsonSerializer.Serialize(new { outcome = "applied", principal_id = request.PrincipalId, operation_id = request.OperationId }));

    private static TrialOperatorResult Error(string code) => new((int)CoreServerExitCode.InvalidConfiguration,
        JsonSerializer.Serialize(new { code }));
}
