using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public sealed class ReadOnlyBrowseService(
    IAuthorizedReadModelQuery query,
    ILogger<ReadOnlyBrowseService> logger)
{
    private readonly IAuthorizedReadModelQuery query =
        query ?? throw new ArgumentNullException(nameof(query));
    private readonly ILogger<ReadOnlyBrowseService> logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public ValueTask<ReadPage<AuthorizedLibrary>> ListLibrariesAsync(
        ListLibrariesQuery request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            request.Page,
            "libraries.list",
            token => query.ListLibrariesAsync(request, token),
            (result, pageSize) => AuthorizedReadResultValidator.Libraries(result, pageSize),
            cancellationToken);

    public ValueTask<AuthorizedEntryPage?> BrowseEntriesAsync(
        BrowseEntriesQuery request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            request.Page,
            "entries.browse",
            token => query.BrowseEntriesAsync(request, token),
            (result, pageSize) => AuthorizedReadResultValidator.Entries(result, request, pageSize),
            cancellationToken);

    public ValueTask<ReadPage<AuthorizedSearchHit>> SearchAssetsAsync(
        SearchAssetsQuery request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            request.Page,
            "assets.search",
            token => query.SearchAssetsAsync(request, token),
            (result, pageSize) => AuthorizedReadResultValidator.Search(result, pageSize),
            cancellationToken);

    private async ValueTask<TResult> ExecuteAsync<TResult>(
        ReadPageOptions page,
        string operation,
        Func<CancellationToken, ValueTask<TResult>> execute,
        Func<TResult, int, TResult> validate,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(page.Timeout);
        var started = TimeProvider.System.GetTimestamp();
        try
        {
            var result = await execute(timeout.Token).ConfigureAwait(false);
            var validated = validate(result, page.PageSize);
            var elapsedMilliseconds = TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
            if (logger.IsEnabled(LogLevel.Information))
            {
                AuthorizedReadLog.Completed(
                    logger,
                    operation,
                    elapsedMilliseconds);
            }

            return validated;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                AuthorizedReadLog.TimedOut(logger, operation, page.Timeout.TotalMilliseconds);
            }

            throw new TimeoutException("The read-only query exceeded its deadline.");
        }
    }
}
