using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AssetLibrary.ImageSupervisor;

if (!OperatingSystem.IsLinux() || RuntimeFeature.IsDynamicCodeSupported || RuntimeInformation.ProcessArchitecture != Architecture.X64) return 2;
if (args is ["--health"])
{
    try { return SupervisorStartup.Healthy() ? 0 : 1; }
    catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException) { return 1; }
}
if (args.Length != 0 && (args.Length != 1 || !SupervisorModes.IsProbe(args[0]))) return 2;
using var stopping = new CancellationTokenSource();
using var operatorStop = new CancellationTokenSource();
using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => { context.Cancel = true; operatorStop.Cancel(); stopping.Cancel(); });
using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; operatorStop.Cancel(); stopping.Cancel(); });
try
{
    if (!LinuxSupervisorFiles.DirectoryIsPrivate()) throw new IOException("Supervisor directory rejected.");
    using var circuit = new ImageCircuitLedger();
    using var listener = SupervisorStartup.Bind();
    if (args.Length == 1) return await SupervisorProbe.RunAsync(args[0], circuit).ConfigureAwait(false);
    await new ImageSocketServer(listener, circuit).RunAsync(stopping).ConfigureAwait(false);
    return 0;
}
catch (ImageNamespaceFailure failure)
{
    Console.Error.WriteLine(failure.Message); // Fixed internal stage; never child stderr or input data.
    return 72; // PID1 exit destroys this private process namespace; the attempt was durably reserved before spawn.
}
catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine("image_supervisor:configuration_or_state_rejected");
    // Permanent deployment/state failures remain unhealthy without repeated decoder creation or container restart.
    try { await Task.Delay(Timeout.InfiniteTimeSpan, operatorStop.Token).ConfigureAwait(false); }
    catch (OperationCanceledException) { /* Operator requested a normal stop. */ }
    return 2;
}
