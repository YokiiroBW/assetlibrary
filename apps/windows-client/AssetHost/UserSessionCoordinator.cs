using System.Runtime.Versioning;
using AssetLibrary.Windows.Client;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
public sealed class UserSessionCoordinator : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly UserConnectionStore persistence;
    private readonly Func<ServerProfile, ClientTransport> createTransport;
    private readonly Action? changed;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task observer;
    private CancellationTokenSource? connecting;
    private ConnectedSession? snapshots;
    private ConnectionStatus status = new(ConnectionState.Unconfigured);
    private Guid unavailableEpoch = Guid.NewGuid();

    public UserSessionCoordinator(UserConnectionStore persistence, Func<ServerProfile, ClientTransport>? createTransport = null, Action? changed = null)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        this.persistence = persistence;
        this.createTransport = createTransport ?? (profile => new ClientTransport(profile));
        this.changed = changed;
        observer = ObserveAsync(stopping.Token);
    }

    public ConnectionStatus Status { get { lock (gate) { return status; } } }
    public Task<ThumbnailResponse> ReadThumbnailAsync(ThumbnailRequest request, CancellationToken token)
    {
        lock (gate)
        {
            return snapshots?.ReadThumbnailAsync(request, token) ?? Task.FromResult(new ThumbnailResponse(
                status.State == ConnectionState.AccessDenied ? ThumbnailStatus.AccessDenied : ThumbnailStatus.Unavailable,
                unavailableEpoch, request.Node));
        }
    }
    public Task<ThumbnailResponse> ReadPreviewAsync(ThumbnailRequest request, CancellationToken token)
    {
        lock (gate)
        {
            return snapshots?.ReadPreviewAsync(request, token) ?? Task.FromResult(new ThumbnailResponse(
                status.State == ConnectionState.AccessDenied ? ThumbnailStatus.AccessDenied : ThumbnailStatus.Unavailable,
                request.Epoch, request.Node));
        }
    }

    public SnapshotResponse Query(SnapshotRequest request)
    {
        lock (gate)
        {
            return snapshots?.Query(request) ?? ConnectedSession.Unavailable(unavailableEpoch, status.State == ConnectionState.AccessDenied);
        }
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        using var initialization = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token);
        await transition.WaitAsync(initialization.Token).ConfigureAwait(false);
        lock (gate) { connecting = initialization; }
        try
        {
            var settings = await persistence.LoadSettingsAsync(initialization.Token).ConfigureAwait(false);
            if (settings is not null)
            {
                _ = new ServerProfile(settings.Origin, settings.CertificateSha256);
                lock (gate) { status = FromSettings(settings, ConnectionState.Disconnected) with { RememberLogin = false }; }
            }
            var remembered = await persistence.LoadRememberedAsync(initialization.Token).ConfigureAwait(false);
            if (remembered is not null)
            {
                persistence.DeleteRemembered();
                _ = await ConnectAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Connect, remembered), initialization.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (UserConnectionStore.IsStorageFailure(error) || error is ArgumentException)
        { lock (gate) { status = new(ConnectionState.Unavailable); } }
        catch (OperationCanceledException) { lock (gate) { status = status with { State = ConnectionState.Disconnected }; } }
        finally { lock (gate) { connecting = null; } transition.Release(); }
    }

    public async Task<ControlResponse> ExecuteAsync(ControlRequest request, CancellationToken token)
    {
        ControlProtocol.Validate(request);
        if (request.Operation == ControlOperation.Status) { return Response(request); }
        if (request.Operation is ControlOperation.Disconnect or ControlOperation.Shutdown)
        {
            lock (gate) { connecting?.Cancel(); }
            await transition.WaitAsync(token).ConfigureAwait(false);
        }
        else if (!await transition.WaitAsync(0, token).ConfigureAwait(false)) { return Response(request, ControlError.Busy); }
        try
        {
            await ClearAsync(ConnectionState.Disconnected, forget: request.Operation != ControlOperation.Shutdown).ConfigureAwait(false);
            if (request.Operation is ControlOperation.Disconnect or ControlOperation.Shutdown)
            {
                lock (gate) { status = status with { RememberLogin = request.Operation == ControlOperation.Shutdown && status.RememberLogin }; }
                return Response(request);
            }
            return await ConnectAsync(request, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return Response(request, ControlError.Cancelled); }
        catch (Exception error) when (UserConnectionStore.IsStorageFailure(error))
        {
            await ClearAsync(ConnectionState.Unavailable).ConfigureAwait(false);
            return Response(request, ControlError.StorageError);
        }
        finally { transition.Release(); }
    }

    private async Task<ControlResponse> ConnectAsync(ControlRequest request, CancellationToken token)
    {
        var input = request.Connection!;
        ServerProfile profile;
        try { profile = new ServerProfile(input.Origin, input.CertificateSha256); }
        catch (ArgumentException) { return Response(request, ControlError.InvalidRequest); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token);
        lock (gate)
        {
            connecting = cancellation;
            status = FromSettings(input.Settings, ConnectionState.Connecting) with { RememberLogin = false };
        }
        var candidate = createTransport(profile);
        var published = false;
        try
        {
            var session = await candidate.SignInAsync(input.AccountName, input.Password, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            await persistence.SaveAsync(input with { Origin = profile.Origin, CertificateSha256 = profile.CertificateSha256 }, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            ConnectedSession store;
            lock (gate)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                store = new ConnectedSession(candidate, session);
                snapshots = store;
                status = FromSettings(input.Settings, ConnectionState.Connected) with
                { DisplayName = session.DisplayName, ExpiresAt = session.ExpiresAt };
                published = true;
            }
            store.Prime();
            changed?.Invoke();
            return Response(request);
        }
        catch (ClientException error)
        {
            lock (gate) { status = status with { State = error.ClearsData ? ConnectionState.AccessDenied : ConnectionState.Unavailable }; }
            return Response(request, error.ClearsData ? ControlError.AccessDenied : ControlError.Unavailable);
        }
        catch (OperationCanceledException)
        {
            lock (gate) { status = status with { State = ConnectionState.Disconnected }; }
            return Response(request, ControlError.Cancelled);
        }
        finally
        {
            lock (gate) { connecting = null; }
            if (!published) { candidate.Dispose(); persistence.DeleteRemembered(); }
        }
    }

    private async Task ClearAsync(ConnectionState state, bool forget = false)
    {
        ConnectedSession? previous;
        lock (gate)
        {
            previous = snapshots;
            snapshots = null;
            unavailableEpoch = Guid.NewGuid();
            status = status with { State = state, DisplayName = null, ExpiresAt = null };
        }
        changed?.Invoke();
        try
        {
            if (forget)
            {
                persistence.DeleteRemembered();
                lock (gate) { status = status with { RememberLogin = false }; }
            }
        }
        finally { if (previous is not null) { await previous.DisposeAsync().ConfigureAwait(false); } }
    }

    private async Task ObserveAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, token).ConfigureAwait(false);
                if (!await transition.WaitAsync(0, token).ConfigureAwait(false)) { continue; }
                try
                {
                    ConnectedSession? current;
                    lock (gate) { current = snapshots; }
                    if (current?.IsRevoked != true) { continue; }
                    await ClearAsync(ConnectionState.AccessDenied, forget: true).ConfigureAwait(false);
                }
                catch (Exception error) when (UserConnectionStore.IsStorageFailure(error))
                { lock (gate) { status = status with { State = ConnectionState.Unavailable, RememberLogin = false }; } }
                finally { transition.Release(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Host shutdown. */ }
    }

    private ControlResponse Response(ControlRequest request, ControlError? error = null) => new(1, request.RequestId, error is null, error, Status);
    private static ConnectionStatus FromSettings(ConnectionSettings settings, ConnectionState state) =>
        new(state, settings.Origin, settings.CertificateSha256, settings.AccountName, RememberLogin: settings.RememberLogin);

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        lock (gate) { connecting?.Cancel(); }
        await observer.ConfigureAwait(false);
        await transition.WaitAsync().ConfigureAwait(false);
        try { await ClearAsync(ConnectionState.Disconnected).ConfigureAwait(false); }
        finally { transition.Release(); transition.Dispose(); stopping.Dispose(); }
    }
}
