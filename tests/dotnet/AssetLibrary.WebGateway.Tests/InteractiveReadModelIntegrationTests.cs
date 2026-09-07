using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class InteractiveReadModelIntegrationTests
{
    [TestMethod]
    public async Task PostgreSqlPagesDetailsAnchorsAndScopesKeepTheirBoundaries()
    {
        var connection = Environment.GetEnvironmentVariable("ASSETLIBRARY_TEST_INTERACTIVE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Assert.Inconclusive("The required PostgreSQL driver supplies the interactive read fixture.");
        }

        await using var source = NpgsqlDataSource.Create(connection!);
        var queries = new PostgresAuthorizedReadModelQuery(source, new EphemeralDataProtectionProvider());
        var libraryId = new LibraryId(Guid.Parse(Environment.GetEnvironmentVariable("ASSETLIBRARY_TEST_INTERACTIVE_LIBRARY")!));
        var subject = new AuthenticatedSubject("oidc:interactive-reader");
        var request = new BrowseEntriesQuery(subject, libraryId, new BrowseParentPath("folder"),
            new ReadPageOptions(31, TimeSpan.FromSeconds(5)));
        var baseline = await InteractiveReadSortingAssertions.VerifyAsync(queries, request);
        await InteractiveReadAnchorAssertions.VerifyAsync(queries, request, baseline);
        await InteractiveReadScopeAssertions.VerifyAsync(queries, request);
    }
}
