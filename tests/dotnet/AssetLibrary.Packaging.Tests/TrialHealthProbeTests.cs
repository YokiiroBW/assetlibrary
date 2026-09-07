using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=nas-probe.invalid", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("nas-probe.invalid");
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var files = TrialCertificateTestSupport.WriteCertificate(fixture, generated, "probe-server");
        var origin = new Uri($"https://nas-probe.invalid:{TrialTestTls.ReserveOrigin().Port}");
        var configuration = fixture.Configuration with
        {
            PublicOrigin = origin.AbsoluteUri,
            BindHost = "0.0.0.0",
            TlsCertificateFile = files.CertificateFile,
            TlsCertificatePasswordFile = files.PasswordFile,
        };
        WriteConfiguration(fixture, configuration);

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
        using var alternateKey = RSA.Create(2048);
        var alternateRequest = new CertificateRequest("CN=nas-probe.invalid", alternateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        alternateRequest.CertificateExtensions.Add(names.Build());
        using var alternate = alternateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var alternateFiles = TrialCertificateTestSupport.WriteCertificate(fixture, alternate, "other-server");
        WriteConfiguration(fixture, configuration with
        {
            TlsCertificateFile = alternateFiles.CertificateFile,
            TlsCertificatePasswordFile = alternateFiles.PasswordFile,
        });
        Assert.AreEqual(69, await TrialHealthProbe.RunAsync(fixture.ConfigurationPath));
    }

    private static void WriteConfiguration(TrialConfigurationFixture fixture, TrialConfiguration configuration)
    {
        File.WriteAllText(fixture.ConfigurationPath, JsonSerializer.Serialize(configuration,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        TrialConfigurationFixture.MakePrivateFile(fixture.ConfigurationPath);
    }
}
