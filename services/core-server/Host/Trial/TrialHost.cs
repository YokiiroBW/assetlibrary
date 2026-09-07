using AssetLibrary.Modules.GatewayAuth.Application;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialHost
{
    public static async Task<int> RunAsync(string configurationPath, string? operatorAction = null)
    {
        try
        {
            var configuration = await TrialConfiguration.LoadAsync(configurationPath, CancellationToken.None).ConfigureAwait(false);
            using var certificate = await TrialCertificate.LoadAsync(configuration, CancellationToken.None).ConfigureAwait(false);
            using var history = await TrialCertificateHistory.LoadAsync(configuration, CancellationToken.None).ConfigureAwait(false);
            await using var connections = await TrialDatabaseConnections.CreateAsync(configuration.Database, CancellationToken.None).ConfigureAwait(false);
            await using var audit = PostgresDatabaseReadinessProbe.Create(connections.Audit);
            var readiness = new TrialRuntimeReadinessProbe(audit, connections);
            if (operatorAction is not ("initialize-key" or "rotate-key"))
            {
                var status = await readiness.CheckAsync(CancellationToken.None).ConfigureAwait(false);
                if (!status.IsReady)
                {
                    return await ErrorAsync(CoreServerExitCode.Unavailable, status.PublicCode).ConfigureAwait(false);
                }
            }

            using var instance = operatorAction is null ? TrialPrivateState.AcquireInstance(configuration.StatePath) : null;
            await using var application = TrialHostFactory.Build(configuration, connections, certificate, readiness,
                operatorMode: operatorAction is not null, decryptionCertificates: history.Certificates);
            if (operatorAction is not null)
            {
                var runtime = application.Services.GetRequiredService<GatewayAuthenticationRuntime>();
                var result = await TrialAdministratorOperator.ExecuteAsync(
                    operatorAction, runtime, Console.OpenStandardInput(), CancellationToken.None).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(result.Json).ConfigureAwait(false);
                return result.ExitCode;
            }

            await application.RunAsync().ConfigureAwait(false);
            return (int)CoreServerExitCode.Success;
        }
        catch (TrialConfigurationException exception)
        {
            return await ErrorAsync(CoreServerExitCode.InvalidConfiguration, exception.Code).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return await ErrorAsync(CoreServerExitCode.InvalidConfiguration, "trial_configuration_invalid").ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await ErrorAsync(CoreServerExitCode.Unavailable, "trial_host_unavailable").ConfigureAwait(false);
        }
    }

    private static async Task<int> ErrorAsync(CoreServerExitCode exitCode, string code)
    {
        await Console.Error.WriteLineAsync(CoreServerHostJson.Error(code)).ConfigureAwait(false);
        return (int)exitCode;
    }
}
