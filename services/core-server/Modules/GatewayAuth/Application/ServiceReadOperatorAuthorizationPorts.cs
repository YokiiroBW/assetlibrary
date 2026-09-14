using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public interface IServiceReadOperatorAuthorization
{
    ValueTask<OutOfBandAuthorizationProof> IssueAsync(ServiceReadOperatorRequest request, CancellationToken cancellationToken);
    ValueTask<bool> VerifyAsync(ServiceReadOperatorRequest request, OutOfBandAuthorizationProof proof, CancellationToken cancellationToken);
}
