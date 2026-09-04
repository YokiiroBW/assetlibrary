using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed record DatabaseMigrationContract(
    int PostgreSqlMajor,
    string BootstrapChecksum,
    string AuditorRole,
    IReadOnlyList<ExpectedDatabaseMigration> Migrations)
{
    public const string Contract = "v01-010/1";
    public const string ResourceName = "AssetLibrary.Database.Migrations.Manifest";
    private const int MaximumMigrations = 1000;
    private static readonly Regex NamePattern = new(
        "^[a-z][a-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex RolePattern = new(
        "^assetlibrary_[a-z0-9_]+_owner$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public int LatestVersion => Migrations[^1].Version;

    public static DatabaseMigrationContract LoadCurrent() =>
        Load(typeof(DatabaseMigrationContract).Assembly);

    internal static DatabaseMigrationContract Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The database migration manifest resource is missing.");
        return Parse(stream);
    }

    internal static DatabaseMigrationContract Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The database migration manifest JSON is invalid.", exception);
        }

        using (document)
        {
            return Parse(document.RootElement);
        }
    }

    private static DatabaseMigrationContract Parse(JsonElement documentRoot)
    {
        var root = RequireObject(documentRoot, "manifest");
        if (RequireInt32(root, "format_version") != 1)
        {
            throw new InvalidDataException("The database migration manifest format is unsupported.");
        }

        var postgres = RequireObject(Require(root, "postgresql"), "postgresql");
        var major = RequireInt32(postgres, "major");
        if (major < 1)
        {
            throw new InvalidDataException("The PostgreSQL major version is invalid.");
        }

        var bootstrap = RequireObject(Require(root, "bootstrap"), "bootstrap");
        var bootstrapChecksum = RequireString(bootstrap, "sha256");
        if (!Sha256Pattern.IsMatch(bootstrapChecksum))
        {
            throw new InvalidDataException("The bootstrap checksum is invalid.");
        }

        var roles = RequireObject(Require(root, "role_provisioning"), "role_provisioning");
        var auditor = RequireString(roles, "auditor");
        if (!string.Equals(auditor, "assetlibrary_database_auditor", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The database auditor role is invalid.");
        }

        var migrationsElement = Require(root, "migrations");
        if (migrationsElement.ValueKind != JsonValueKind.Array
            || migrationsElement.GetArrayLength() is < 1 or > MaximumMigrations)
        {
            throw new InvalidDataException("The database migration list is invalid.");
        }

        var migrations = new List<ExpectedDatabaseMigration>(migrationsElement.GetArrayLength());
        foreach (var element in migrationsElement.EnumerateArray())
        {
            var migration = RequireObject(element, "migration");
            var version = RequireInt32(migration, "version");
            var name = RequireString(migration, "name");
            var module = RequireString(migration, "module");
            var owner = RequireString(migration, "owner_role");
            var checksum = RequireString(migration, "sha256");
            if (version != migrations.Count + 1
                || !NamePattern.IsMatch(name)
                || string.IsNullOrWhiteSpace(module)
                || module.Length > 100
                || module.Any(char.IsControl)
                || !RolePattern.IsMatch(owner)
                || !Sha256Pattern.IsMatch(checksum))
            {
                throw new InvalidDataException("A database migration entry is invalid.");
            }

            migrations.Add(new ExpectedDatabaseMigration(
                version,
                name,
                module,
                owner,
                checksum));
        }

        return new DatabaseMigrationContract(major, bootstrapChecksum, auditor, migrations);
    }

    private static JsonElement Require(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value)
            ? value
            : throw new InvalidDataException($"The database migration manifest is missing {propertyName}.");

    private static JsonElement RequireObject(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"The database migration manifest {name} must be an object.");

    private static string RequireString(JsonElement parent, string propertyName)
    {
        var value = Require(parent, propertyName);
        return value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text
            : throw new InvalidDataException(
                $"The database migration manifest {propertyName} must be a string.");
    }

    private static int RequireInt32(JsonElement parent, string propertyName)
    {
        var value = Require(parent, propertyName);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException(
                $"The database migration manifest {propertyName} must be an integer.");
    }
}

internal sealed record ExpectedDatabaseMigration(
    int Version,
    string Name,
    string Module,
    string OwnerRole,
    string Checksum);
