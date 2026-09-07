using System.Net;
using System.Text.Json;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.Packaging.Tests;

[TestClass]
public sealed class TrialAuthenticationLimitTests
{
    [TestMethod]
    public async Task GlobalWindowIsBoundedAndRejectsBeforeParsingOrPasswordWork()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var admitted = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: "{}");
            Assert.AreEqual(HttpStatusCode.BadRequest, admitted.StatusCode);
        }

        using var limited = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: "{}");
        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsTrue(limited.Headers.RetryAfter!.Delta > TimeSpan.Zero);
        Assert.AreEqual(0, host.Store.CredentialCalls);
    }

    [TestMethod]
    public void ConcurrencyLimitHasTwoPermitsAndNoQueue()
    {
        using var limiter = new TrialLoginLimiter();
        using var first = limiter.Acquire();
        using var second = limiter.Acquire();
        using var third = limiter.Acquire();
        Assert.IsTrue(first.IsAllowed);
        Assert.IsTrue(second.IsAllowed);
        Assert.IsFalse(third.IsAllowed);
        Assert.AreEqual(1, third.RetryAfterSeconds);
        first.Dispose();
        using var afterRelease = limiter.Acquire();
        Assert.IsTrue(afterRelease.IsAllowed);
    }

    [TestMethod]
    public async Task ThirdConcurrentLoginIsRejectedBeforeCredentialOrPasswordWork()
    {
        await using var host = await TrialAuthenticationTestHost.StartAsync();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Store.HoldCredentials = hold;
        var body = JsonSerializer.Serialize(new { account_name = "trial-admin", password = TrialAuthenticationStore.Password });
        var first = host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: body);
        var second = host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: body);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (host.Store.CredentialCalls != 2)
            {
                await Task.Delay(10, deadline.Token);
            }

            using var denied = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login", body: body);
            Assert.AreEqual(HttpStatusCode.TooManyRequests, denied.StatusCode);
            Assert.AreEqual(2, host.Store.CredentialCalls);
        }
        finally
        {
            hold.TrySetResult();
            using var firstResponse = await first;
            using var secondResponse = await second;
            Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);
        }
    }
}
