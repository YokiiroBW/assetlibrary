using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal static class PostgresReadPage
{
    public static ReadPageCursor? Complete<T>(List<T> rows, int pageSize, Func<T, ReadPageCursor> encodeCursor)
    {
        if (rows.Count <= pageSize)
        {
            return null;
        }

        if (rows.Count != pageSize + 1)
        {
            throw new InvalidOperationException("A read page exceeded its lookahead bound.");
        }

        rows.RemoveAt(pageSize);
        return encodeCursor(rows[^1]);
    }
}
