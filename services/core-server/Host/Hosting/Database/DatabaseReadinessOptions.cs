using Npgsql;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed record DatabaseReadinessOptions(DatabaseConnectionSecret Connection)
{
    public const string EnvironmentName = "ASSETLIBRARY_DATABASE_READINESS_CONNECTION";
    public const int TimeoutSeconds = 5;
    public const int MaximumPoolSize = 4;
    private const int MaximumConnectionStringLength = 8192;
    private const string ApplicationName = "AssetLibrary.CoreServer.DatabaseReadiness";

    public static DatabaseReadinessOptionsResult Parse(string? value)
    {
        if (value is null)
        {
            return DatabaseReadinessOptionsResult.Disabled();
        }

        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumConnectionStringLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            return DatabaseReadinessOptionsResult.Invalid();
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(builder.Host)
                || string.IsNullOrWhiteSpace(builder.Database)
                || string.IsNullOrWhiteSpace(builder.Username)
                || !string.IsNullOrWhiteSpace(builder.Options))
            {
                return DatabaseReadinessOptionsResult.Invalid();
            }

            builder.ApplicationName = ApplicationName;
            builder.Timeout = TimeoutSeconds;
            builder.CommandTimeout = TimeoutSeconds;
            builder.Pooling = true;
            builder.MinPoolSize = 0;
            builder.MaxPoolSize = MaximumPoolSize;
            builder.Multiplexing = false;
            builder.NoResetOnClose = false;
            builder.Enlist = false;
            builder.IncludeErrorDetail = false;
            return DatabaseReadinessOptionsResult.Enabled(
                new DatabaseReadinessOptions(new DatabaseConnectionSecret(builder.ConnectionString)));
        }
        catch (ArgumentException)
        {
            return DatabaseReadinessOptionsResult.Invalid();
        }
    }
}

internal sealed class DatabaseConnectionSecret
{
    private readonly string value;

    public DatabaseConnectionSecret(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    internal string Reveal() => value;

    public override string ToString() => "[redacted]";
}

internal sealed record DatabaseReadinessOptionsResult(
    bool IsValid,
    bool IsEnabled,
    DatabaseReadinessOptions? Options,
    string ErrorCode)
{
    public static DatabaseReadinessOptionsResult Disabled() =>
        new(true, false, null, string.Empty);

    public static DatabaseReadinessOptionsResult Enabled(DatabaseReadinessOptions options) =>
        new(true, true, options, string.Empty);

    public static DatabaseReadinessOptionsResult Invalid() =>
        new(false, false, null, "invalid_database_configuration");
}
