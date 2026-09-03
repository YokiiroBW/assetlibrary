namespace AssetLibrary.TransferOperation.Tests;

internal enum SandboxFaultPoint
{
    None = 0,
    AfterStageWritten = 1,
    AfterTargetCommitted = 2,
    BeforeSourceTrash = 3,
    AfterSourceTrash = 4,
}

internal sealed class SandboxFaultPlan
{
    private int triggered;

    public SandboxFaultPoint Point { get; set; }

    public void Trigger(SandboxFaultPoint point)
    {
        if (Point == point && Interlocked.Exchange(ref triggered, 1) == 0)
        {
            throw new IOException($"Injected V01-007 sandbox failure at {point}.");
        }
    }
}
