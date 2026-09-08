using System.Runtime.Versioning;
using System.Text;
using System.Diagnostics;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;

namespace AssetLibrary.Preview.Tests;

[SupportedOSPlatform("windows")]
internal sealed class WindowsImageProbe(WindowsImageProcess process) : IAsyncDisposable
{
    private readonly StreamReader output = new(process.Output, Encoding.ASCII, false, 1024, leaveOpen: true);
    public WindowsImageProcess Child => process;

    public static string Executable()
    {
        var executable = Environment.GetEnvironmentVariable("ASSETLIBRARY_WINDOWS_IMAGE_PROBE");
        if (string.IsNullOrWhiteSpace(executable)) Assert.Inconclusive("The owned native Windows confinement probe is required.");
        return executable;
    }

    public static async Task<WindowsImageProbe> StartAsync(string state, CancellationToken token)
        => new(await WindowsImageProcess.StartAsync(Executable(), state, token));

    public static async Task<Dictionary<string, string>> RunTrustedControlAsync(char mode, byte[]? arguments, string directory, CancellationToken token)
    {
        // This process only runs the owned constant native probe, never a decoder or image input.
        var start = new ProcessStartInfo(Executable())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory,
        };
        start.Environment.Clear();
        start.Environment["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        start.Environment["WINDIR"] = start.Environment["SystemRoot"];
        start.Environment["LOCALAPPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        start.Environment["TEMP"] = directory; start.Environment["TMP"] = directory;
        using var control = Process.Start(start)!;
        try
        {
            await control.StandardInput.BaseStream.WriteAsync(new byte[] { checked((byte)mode) }, token);
            if (arguments is not null) await control.StandardInput.BaseStream.WriteAsync(arguments, token);
            control.StandardInput.Dispose();
            Assert.AreEqual("ready=1", await control.StandardOutput.ReadLineAsync(token));
            await control.WaitForExitAsync(token);
            var buffer = new char[4097];
            var count = await control.StandardOutput.ReadBlockAsync(buffer, token);
            Assert.IsLessThanOrEqualTo(4096, count);
            Assert.AreEqual(0, control.ExitCode);
            return Parse(new string(buffer, 0, count));
        }
        finally { if (!control.HasExited) { control.Kill(entireProcessTree: true); control.WaitForExit(2000); } }
    }

    public async Task SendAsync(char mode, byte[]? arguments, CancellationToken token)
    {
        await process.Input.WriteAsync(new byte[] { checked((byte)mode) }, token);
        if (arguments is not null) await process.Input.WriteAsync(arguments, token);
        await process.Input.FlushAsync(token);
        Assert.AreEqual("ready=1", await output.ReadLineAsync(token));
    }

    public async Task<Dictionary<string, string>> FinishAsync(CancellationToken token)
    {
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var detail = new char[1024];
            var read = await output.ReadBlockAsync(detail, drain.Token);
            Assert.Fail($"Probe did not finish; native exit=0x{process.ExitCode:X8}; output={new string(detail, 0, read)}.");
        }
        var buffer = new char[4097];
        var count = await output.ReadBlockAsync(buffer, token);
        Assert.IsLessThanOrEqualTo(4096, count);
        return Parse(new string(buffer, 0, count));
    }

    private static Dictionary<string, string> Parse(string text)
        => text.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(field => field.Split('=', 2))
            .ToDictionary(field => field[0], field => field[1], StringComparer.Ordinal);

    public static byte[] PathArgument(string path)
    {
        var bytes = Encoding.Unicode.GetBytes(path);
        Assert.IsLessThan(2048, bytes.Length);
        var result = new byte[bytes.Length + 4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(result, path.Length);
        bytes.CopyTo(result, 4);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        output.Dispose();
        await process.DisposeAsync();
    }
}
