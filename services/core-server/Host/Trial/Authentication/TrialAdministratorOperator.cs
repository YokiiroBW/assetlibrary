using System.Text.Json;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal static class TrialAdministratorOperator
{
    public static async Task<TrialOperatorResult> ExecuteAsync(
        string action,
        GatewayAuthenticationRuntime runtime,
        Stream input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(input);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            if (action is "initialize-key" or "rotate-key")
            {
                var key = action == "initialize-key"
                    ? await runtime.InitializeAuthorizationKeyAsync(deadline.Token).ConfigureAwait(false)
                    : await runtime.RotateAuthorizationKeyAsync(deadline.Token).ConfigureAwait(false);
                return new TrialOperatorResult(0, JsonSerializer.Serialize(new
                {
                    outcome = "applied",
                    key_id = key.KeyId,
                    created_at = key.CreatedAt,
                }));
            }

            if (action is not ("bootstrap" or "recover"))
            {
                return Error(CoreServerExitCode.InvalidConfiguration, "invalid_request", "管理员操作不受支持。");
            }

            using var request = await TrialOperatorInput.ReadAsync(action, input, deadline.Token).ConfigureAwait(false);
            var result = action == "bootstrap"
                ? await runtime.BootstrapAdministratorAsync(request.Request, request.Secret, deadline.Token)
                    .ConfigureAwait(false)
                : await runtime.RecoverAdministratorAsync(request.Request, request.Secret, deadline.Token)
                    .ConfigureAwait(false);
            return Result(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is ArgumentException or JsonException or FormatException)
        {
            return Error(CoreServerExitCode.InvalidConfiguration, "invalid_request", "管理员操作输入格式不正确。");
        }
        catch (Exception)
        {
            return Error(CoreServerExitCode.Unavailable, "service_unavailable", "管理员操作暂时不可用，请检查部署状态后重试。");
        }
    }

    private static TrialOperatorResult Result(AdministratorBootstrapRecoveryResult result)
    {
        if (result.Outcome == AdministratorBootstrapRecoveryOutcome.Applied)
        {
            return new TrialOperatorResult(0, JsonSerializer.Serialize(new
            {
                outcome = "applied",
                was_replayed = result.WasReplayed,
                principal_id = result.Account!.PrincipalId,
                credential_version = result.Account.CredentialVersion,
                principal_session_version = result.Account.PrincipalSessionVersion,
            }));
        }

        return result.Outcome switch
        {
            AdministratorBootstrapRecoveryOutcome.SecretRejected =>
                Error(CoreServerExitCode.InvalidConfiguration, "secret_rejected", "请使用至少15个字符且未泄露的口令。"),
            AdministratorBootstrapRecoveryOutcome.AuthorizationRejected =>
                Error(CoreServerExitCode.InvalidConfiguration, "authorization_rejected", "管理员授权无效或已过期。"),
            AdministratorBootstrapRecoveryOutcome.StateConflict or AdministratorBootstrapRecoveryOutcome.RequestConflict =>
                Error(CoreServerExitCode.InvalidConfiguration, "state_conflict", "管理员操作与当前状态或已有请求冲突。"),
            _ => Error(CoreServerExitCode.Unavailable, "service_unavailable",
                "管理员操作依赖暂时不可用；新口令需要联网完成风险检查，请稍后重试。"),
        };
    }

    private static TrialOperatorResult Error(CoreServerExitCode exitCode, string code, string message) =>
        new((int)exitCode, JsonSerializer.Serialize(new { code, message }));

}

internal sealed record TrialOperatorResult(int ExitCode, string Json);
