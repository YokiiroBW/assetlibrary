using AssetLibrary.ImageSupervisor;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class ImageContainerStateTests
{
    [TestMethod]
    public void ThreeUnfinishedAttemptsSurviveRestartAndDoNotAdmitAFourth()
    {
        var current = new ImageCircuitState();
        for (var attempt = 1; attempt <= 3; ++attempt)
        {
            current.ReserveAttempt();
            current = ImageCircuitState.Read(current.Encode());
            Assert.AreEqual(attempt, current.Failures);
        }
        Assert.IsTrue(current.Open);
        Assert.ThrowsExactly<InvalidOperationException>(current.ReserveAttempt);
        current.CompleteHealthy();
        Assert.IsFalse(ImageCircuitState.Read(current.Encode()).Open);
        Assert.AreEqual(0, current.Failures);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("49534331000000")]
    [DataRow("495343310000000000")]
    [DataRow("0053433100000000")]
    [DataRow("4953433104000000")]
    [DataRow("49534331ffffffff")]
    public void PartialUnknownAndOutOfBudgetStatesFailClosed(string hex) =>
        Assert.ThrowsExactly<InvalidDataException>(() => ImageCircuitState.Read(Convert.FromHexString(hex)));
}
