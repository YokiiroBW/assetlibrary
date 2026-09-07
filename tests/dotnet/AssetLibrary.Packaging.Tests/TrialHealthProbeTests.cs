using AssetLibrary.CoreServer.Hosting.Trial;
using AssetLibrary.IntegrationTestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class TrialHealthProbeTests
{
    [TestMethod]
    public async Task LocalProbePreservesPublicAuthorityAndRejectsWrongIdentityOrCertificate()
    {
        using var fixture = new TrialConfigurationFixture();
        using var generated = TrialTestTls.CreateSelfSigned("nas-probe.invalid");
        var files = TrialCertificateTestSupport.WriteCertificate(fixture, generated, "probe-server");
        var origin = new Uri($"https://nas-probe.invalid:{TrialTestTls.ReserveOrigin().Port}");
        var configuration = fixture.Configuration with
        {
            PublicOrigin = origin.AbsoluteUri,
            BindHost = "0.0.0.0",
            TlsCertificateFile = files.CertificateFile,
            TlsCertificatePasswordFile = files.PasswordFile,
        };
        fixture.WriteConfiguration(configuration);

        using var certificate = await TrialCertificate.LoadAsync(configuration, CancellationToken.None);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        TrialHttpTransport.Configure(builder, configuration, certificate);
        await using var application = builder.Build();
        var returnedIdentity = configuration.DeploymentId;
        string? receivedHost = null;
        application.MapGet("/readyz", (HttpContext context) =>
        {
            receivedHost = context.Request.Host.Value;
            return Results.Json(new
            {
                contract = "v01-015/1",
                status = "ready",
                deployment_id = returnedIdentity,
                business_api_ready = true,
                production_file_writes_enabled = false,
            });
        });
        await application.StartAsync();

        Assert.AreEqual(0, await TrialHealthProbe.RunAsync(fixture.ConfigurationPath));
        Assert.AreEqual(origin.Authority, receivedHost);
        returnedIdentity = Guid.NewGuid();
        Assert.AreEqual(69, await TrialHealthProbe.RunAsync(fixture.ConfigurationPath));

        returnedIdentity = configuration.DeploymentId;
        using var alternate = TrialTestTls.CreateSelfSigned("nas-probe.invalid");
        var alternateFiles = TrialCertificateTestSupport.WriteCertificate(fixture, alternate, "other-server");
        fixture.WriteConfiguration(configuration with
        {
            TlsCertificateFile = alternateFiles.CertificateFile,
            TlsCertificatePasswordFile = alternateFiles.PasswordFile,
        });
        Assert.AreEqual(69, await TrialHealthProbe.RunAsync(fixture.ConfigurationPath));
    }

}
