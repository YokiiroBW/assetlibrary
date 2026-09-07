using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class ProtectedGatewayAuthorizationKeyStore : IGatewayAuthorizationKeyLifecycle
{
    private const int MaximumProtectedBytes = 4096;
    private readonly string keyFile;
    private readonly IDataProtector protector;
    private readonly TimeProvider clock;

    public ProtectedGatewayAuthorizationKeyStore(
        GatewayAuthorizationConfiguration configuration,
        IDataProtectionProvider protection,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(protection);
        keyFile = configuration.KeyFilePath;
        protector = protection.CreateProtector(
            "AssetLibrary.GatewayAuth.OperatorKey.v1",
            configuration.DeploymentId.ToString("N"));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ValueTask<GatewayAuthorizationKeyState> InitializeAsync(CancellationToken cancellationToken) =>
        ReplaceAsync(initialize: true, cancellationToken);

    public ValueTask<GatewayAuthorizationKeyState> RotateAsync(CancellationToken cancellationToken) =>
        ReplaceAsync(initialize: false, cancellationToken);

    public async ValueTask<GatewayAuthorizationKeyMaterial> ReadAsync(CancellationToken cancellationToken)
    {
        PrivateAuthorizationFiles.ValidateFile(keyFile);
        await using var stream = new FileStream(
            keyFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            bufferSize: 4096, FileOptions.Asynchronous);
        if (stream.Length is < 1 or > MaximumProtectedBytes)
        {
            throw new InvalidDataException("The gateway authorization key is invalid.");
        }

        var encrypted = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(encrypted, cancellationToken).ConfigureAwait(false);
        var cleartext = protector.Unprotect(encrypted);
        try
        {
            return GatewayAuthorizationKeyMaterial.Parse(cleartext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
        }
    }

    private async ValueTask<GatewayAuthorizationKeyState> ReplaceAsync(
        bool initialize,
        CancellationToken cancellationToken)
    {
        using var deadline = AuthenticationOperationDeadline.Create(cancellationToken);
        PrivateAuthorizationFiles.ValidateParent(keyFile);
        await using var lease = await AcquireLeaseAsync(deadline.Token).ConfigureAwait(false);
        if (File.Exists(keyFile) == initialize)
        {
            throw new InvalidOperationException("The gateway authorization key state conflicts with this operation.");
        }

        if (!initialize)
        {
            using var previous = await ReadAsync(deadline.Token).ConfigureAwait(false);
        }

        using var key = GatewayAuthorizationKeyMaterial.Create(clock.GetUtcNow());
        var cleartext = key.Serialize();
        byte[] encrypted;
        try
        {
            encrypted = protector.Protect(cleartext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
        }

        await PersistAsync(encrypted, initialize, deadline.Token).ConfigureAwait(false);
        return key.State;
    }

    private async ValueTask PersistAsync(byte[] encrypted, bool initialize, CancellationToken cancellationToken)
    {
        var temporary = keyFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = PrivateAuthorizationFiles.Create(temporary))
            {
                await stream.WriteAsync(encrypted, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            PrivateAuthorizationFiles.ValidateParent(keyFile);
            File.Move(temporary, keyFile, overwrite: !initialize);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private async ValueTask<FileStream> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        var lockFile = keyFile + ".lock";
        if (!File.Exists(lockFile))
        {
            try
            {
                await using var created = PrivateAuthorizationFiles.Create(lockFile);
            }
            catch (IOException) when (File.Exists(lockFile))
            {
                // Another operator created the same stable lock file.
            }
        }

        PrivateAuthorizationFiles.ValidateFile(lockFile);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
