using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace AssetLibrary.Windows.Session;

[SupportedOSPlatform("windows")]
public sealed class UserConnectionStore
{
    private readonly PrivateStateFiles files;
    public UserConnectionStore(string? directory = null)
    {
        files = new PrivateStateFiles(Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AssetLibrary", "WindowsClient")));
    }

    public async Task<ConnectionSettings?> LoadSettingsAsync(CancellationToken token)
    {
        files.EnsureDirectory();
        var bytes = await files.ReadAsync("connection.json", token).ConfigureAwait(false);
        return bytes is null ? null : JsonSerializer.Deserialize<ConnectionSettings>(bytes, ControlProtocol.JsonOptions);
    }

    public async Task<ConnectionInput?> LoadRememberedAsync(CancellationToken token)
    {
        files.EnsureDirectory();
        var bytes = await files.ReadAsync("remembered.bin", token).ConfigureAwait(false);
        if (bytes is null) { return null; }
        var clear = UserDataProtection.Unprotect(bytes);
        try
        {
            var connection = JsonSerializer.Deserialize<ConnectionInput>(clear, ControlProtocol.JsonOptions)
                ?? throw new InvalidDataException("Invalid remembered login.");
            ControlProtocol.Validate(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Connect, connection));
            return connection.RememberLogin ? connection : null;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public async Task SaveAsync(ConnectionInput connection, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(connection);
        files.EnsureDirectory();
        DeleteRemembered();
        await files.WriteAsync("connection.json", JsonSerializer.SerializeToUtf8Bytes(connection.Settings, ControlProtocol.JsonOptions), token).ConfigureAwait(false);
        if (!connection.RememberLogin) { return; }
        var clear = JsonSerializer.SerializeToUtf8Bytes(connection, ControlProtocol.JsonOptions);
        try { await files.WriteAsync("remembered.bin", UserDataProtection.Protect(clear), token).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void DeleteRemembered() => files.Delete("remembered.bin");
    public static bool IsStorageFailure(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or JsonException;
}
