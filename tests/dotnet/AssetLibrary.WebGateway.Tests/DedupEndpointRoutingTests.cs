using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Adapters.AssetLink;
using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.IntegrationTestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The transport surface of the dedup workbench, exercised over real HTTPS on a loopback port with the
/// same route map the trial host installs. The page can only work if all six operations exist at their
/// documented paths, and the read-only boundary is only real if a caller without a signed-in session
/// cannot reach any of them. Both facts are easy to lose in a refactor and invisible to a fixture-driven
/// browser test. Only the authentication middleware is left out here, because it needs a database-backed
/// account store that this surface never reaches before it has authorized the caller.
/// </summary>
[TestClass]
public sealed class DedupEndpointRoutingTests
{
    private static readonly string[] Operations = [.. TrialDedupOperation.Names];

    /// <summary>
    /// The bodies the page actually sends. They are copied from <c>apps/web/src/dedup/dedupClient.ts</c>,
    /// so this test fails if the surface starts requiring a field that client never sends.
    /// </summary>
    private static readonly string[] ClientBodies =
    [
        """{"library_id":"11111111-1111-4111-8111-111111111111","operation_key":"22222222-2222-4222-8222-222222222222","retry":false}""",
        """{"library_id":"11111111-1111-4111-8111-111111111111","task_id":"33333333-3333-4333-8333-333333333333"}""",
        """{"library_id":"11111111-1111-4111-8111-111111111111","task_id":"33333333-3333-4333-8333-333333333333"}""",
        """{"library_id":"11111111-1111-4111-8111-111111111111","operation_key":"22222222-2222-4222-8222-222222222222"}""",
        """{"library_id":"11111111-1111-4111-8111-111111111111","task_id":"33333333-3333-4333-8333-333333333333","plan_digest":"aa","operation_key":"22222222-2222-4222-8222-222222222222"}""",
        """{"library_id":"11111111-1111-4111-8111-111111111111","task_id":"33333333-3333-4333-8333-333333333333","analysis_version":"v","plan_digest":"aa"}""",
    ];

    [TestMethod]
    public async Task EveryOperationIsMappedAndRefusesAnAnonymousCaller()
    {
        await using var host = await SurfaceHost.StartAsync();
        for (var index = 0; index < Operations.Length; index++)
        {
            var operation = Operations[index];
            using var request = host.Request(HttpMethod.Post, $"/assetlink/v1/dedup/{operation}");
            request.Content = Json(ClientBodies[index]);
            using var response = await host.Client.SendAsync(request);

            // The hidden button is not a permission: the refusal comes from the operation pipeline
            // itself, and it is 403 rather than 404, which proves the route exists and is guarded.
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, operation);
            var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            Assert.AreEqual(403, payload["status"]!.GetValue<int>(), operation);
            Assert.AreEqual("permission_denied", payload["error"]!["code"]!.GetValue<string>(), operation);
        }
    }

    /// <summary>
    /// Requiring the report identity is a per-operation rule. Applying it to every operation rejected
    /// the two bodies the page really sends for a start and a cancel, so this states the rule where it
    /// belongs instead of relying on a probe to notice.
    /// </summary>
    [TestMethod]
    public void OnlyTheOperationsThatAddressAReportRequireATaskId()
    {
        foreach (var operation in Operations)
        {
            var expected = operation is "status" or "results" or "revalidate" or "export";
            Assert.AreEqual(expected, TrialDedupOperation.AddressesReport(operation), operation);
        }

        var start = TrialDedupRequest.Parse(new DefaultHttpContext(), ClientBodies[0]);
        Assert.IsNull(start.TaskId);
        Assert.AreEqual(new Guid("22222222-2222-4222-8222-222222222222"), start.RequireOperationKey());

        var cancel = TrialDedupRequest.Parse(new DefaultHttpContext(), ClientBodies[3]);
        Assert.IsNull(cancel.TaskId);
        Assert.AreEqual(new Guid("22222222-2222-4222-8222-222222222222"), cancel.RequireOperationKey());
    }

    /// <summary>An unknown path must not answer like a real operation, or a typo would look wired.</summary>
    [TestMethod]
    public async Task AnUnknownPathAndAnotherMethodAreNotAnsweredAsOperations()
    {
        await using var host = await SurfaceHost.StartAsync();
        using var unknownRequest = host.Request(HttpMethod.Post, "/assetlink/v1/dedup/analyse");
        unknownRequest.Content = Json("{}");
        using var unknown = await host.Client.SendAsync(unknownRequest);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode, await unknown.Content.ReadAsStringAsync());

        // A read must never start or change an analysis, so the operations answer POST only.
        using var read = await host.Client.SendAsync(host.Request(HttpMethod.Get, "/assetlink/v1/dedup/status"));
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, read.StatusCode);
    }

    /// <summary>
    /// The body ceiling is a transport policy applied before anything else, so an oversized or wrongly
    /// typed body is refused instead of being buffered or truncated into a valid-looking operation. Both
    /// refusals are checked directly on the shared reader, because the operation pipeline answers 403 to
    /// a caller that has not signed in before it would ever read a body.
    /// </summary>
    [TestMethod]
    public async Task AnOversizedOrNonJsonBodyIsRefusedByTheSharedReader()
    {
        var oversized = new DefaultHttpContext();
        oversized.Request.ContentType = "application/json";
        oversized.Request.ContentLength = AssetLinkRequestBody.MaximumBytes + 1;
        await Assert.ThrowsExactlyAsync<RequestBodyTooLargeException>(
            async () => await AssetLinkRequestBody.ReadAsync(oversized.Request, CancellationToken.None));

        var streaming = new DefaultHttpContext();
        streaming.Request.ContentType = "application/json";
        streaming.Request.Body = new MemoryStream(new byte[AssetLinkRequestBody.MaximumBytes + 1]);
        await Assert.ThrowsExactlyAsync<RequestBodyTooLargeException>(
            async () => await AssetLinkRequestBody.ReadAsync(streaming.Request, CancellationToken.None));

        var form = new DefaultHttpContext();
        form.Request.ContentType = "application/x-www-form-urlencoded";
        form.Request.ContentLength = 12;
        await Assert.ThrowsExactlyAsync<UnsupportedRequestMediaTypeException>(
            async () => await AssetLinkRequestBody.ReadAsync(form.Request, CancellationToken.None));
    }

    private static StringContent Json(string body) =>
        new(body, new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" });

    /// <summary>
    /// The same route map as <c>TrialWebEndpoints.Configure</c>, over the same HTTPS origin rules, without
    /// the database-backed services this surface never reaches before it has authorized the caller.
    /// </summary>
    private sealed class SurfaceHost : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly X509Certificate2 certificate;

        private SurfaceHost(WebApplication application, X509Certificate2 certificate, Uri origin)
        {
            this.application = application;
            this.certificate = certificate;
            PublicOrigin = origin.GetLeftPart(UriPartial.Authority);
            Client = TrialTestTls.Client(certificate, origin);
        }

        public HttpClient Client { get; }

        public string PublicOrigin { get; }

        /// <summary>Every trial surface request states the origin it came from, so the pipeline sees one.</summary>
        public HttpRequestMessage Request(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Origin", PublicOrigin);
            return request;
        }

        public static async Task<SurfaceHost> StartAsync()
        {
            var certificate = TrialTestTls.Certificate();
            var reserved = TrialTestTls.ReserveOrigin();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(
                IPAddress.Loopback,
                reserved.Port,
                listen => listen.UseHttps(certificate)));
            var application = builder.Build();
            TrialDedupEndpoints.Map(application);
            await application.StartAsync();
            var address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new SurfaceHost(application, certificate, new Uri(address));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            certificate.Dispose();
        }
    }
}
