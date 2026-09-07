using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class PwnedPasswordsRangeCompatibilityTests
{
    [TestMethod]
    [DataRow(2509, 42, true)]
    [DataRow(5000, 0, false)]
    public async Task GrowingRangesIncludeTheCompleteFinalRecordWithoutATerminatingNewline(int lineCount, int matchCount, bool compromised)
    {
        const string secretText = "range compatibility synthetic operator secret";
        var records = PwnedPasswordsTestData.ValidResponse(secretText, matchingCount: matchCount, lineCount: lineCount)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var body = string.Join("\r\n", records.Skip(1).Append(records[0]));
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(PwnedPasswordsTestData.TextResponse(body)));
        using var checker = PwnedPasswordsTestData.Checker(handler, new PwnedPasswordsSecretRiskOptions());
        using var secret = new LocalSecret(secretText);

        var actual = await checker.EvaluateAsync(secret, CancellationToken.None);

        Assert.AreEqual(compromised ? LocalSecretRisk.Compromised : LocalSecretRisk.Allowed, actual);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public void IncompleteOrMalformedFinalRecordsRemainInvalid()
    {
        var complete = PwnedPasswordsTestData.ValidResponse("parser boundary synthetic secret");
        var prefix = complete[..complete.LastIndexOf('\r', complete.Length - 3)];
        foreach (var ending in new[]
        {
            "\r\nABC", "\r\n" + new string('A', 35) + ":",
            "\r\n" + new string('A', 35) + ":1\r", "\r\n" + complete[..37],
        })
        {
            Assert.IsFalse(PwnedPasswordsRangeParser.TryParse(System.Text.Encoding.UTF8.GetBytes(prefix + ending),
                800, 5000, out _));
        }
    }
}
