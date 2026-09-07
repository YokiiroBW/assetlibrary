using System.Net;
using Npgsql;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal sealed class TrialDatabaseConnections : IAsyncDisposable
{
    private TrialDatabaseConnections(
        DatabaseReadinessOptions audit,
        NpgsqlDataSource gateway,
        NpgsqlDataSource library,
        NpgsqlDataSource asset,
        NpgsqlDataSource scan,
        NpgsqlDataSource task)
    {
        Audit = audit;
        Gateway = gateway;
        Library = library;
        Asset = asset;
        Scan = scan;
        Task = task;
    }

    public DatabaseReadinessOptions Audit { get; }
    public NpgsqlDataSource Gateway { get; }
    public NpgsqlDataSource Library { get; }
    public NpgsqlDataSource Asset { get; }
    public NpgsqlDataSource Scan { get; }
    public NpgsqlDataSource Task { get; }

    public static async ValueTask<TrialDatabaseConnections> CreateAsync(TrialDatabaseFiles files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        var connections = new List<NpgsqlConnectionStringBuilder>();
        foreach (var path in files.Paths())
        {
            var text = (await TrialPrivateState.ReadTextAsync(path, 8192, cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
            connections.Add(Parse(text));
        }

        var audit = connections[0];
        if (connections.Any(connection => connection.Host != audit.Host || connection.Port != audit.Port || connection.Database != audit.Database)
            || connections.Select(connection => connection.Username).Distinct(StringComparer.Ordinal).Count() != connections.Count)
        {
            throw new TrialConfigurationException("trial_database_boundaries_invalid");
        }

        var auditOptions = DatabaseReadinessOptions.Parse(audit.ConnectionString).Options
            ?? throw new TrialConfigurationException("trial_database_configuration_invalid");
        var sources = new List<NpgsqlDataSource>();
        try
        {
            foreach (var connection in connections.Skip(1))
            {
                sources.Add(NpgsqlDataSource.Create(connection.ConnectionString));
            }

            return new TrialDatabaseConnections(auditOptions, sources[0], sources[1], sources[2], sources[3], sources[4]);
        }
        catch
        {
            foreach (var source in sources)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    internal static NpgsqlConnectionStringBuilder Parse(string text)
    {
        var parsed = DatabaseReadinessOptions.Parse(text);
        if (!parsed.IsValid || parsed.Options is null)
        {
            throw new TrialConfigurationException("trial_database_configuration_invalid");
        }

        var connection = new NpgsqlConnectionStringBuilder(parsed.Options.Connection.Reveal());
        var host = connection.Host ?? throw new TrialConfigurationException("trial_database_configuration_invalid");
        var loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
        if (host.Contains(',') || !loopback && connection.SslMode != SslMode.VerifyFull)
        {
            throw new TrialConfigurationException("trial_database_tls_required");
        }

        connection.ApplicationName = "AssetLibrary.ReadOnlyTrial";
        connection.MaxPoolSize = 8;
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in new[] { Gateway, Library, Asset, Scan, Task })
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }
}
