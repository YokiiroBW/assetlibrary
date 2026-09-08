using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal sealed record WorkerRequest(int Version, string CanonicalRoot, bool CaseSensitive);

internal sealed record WorkerFrame(
    int Version,
    string Type,
    string? Status = null,
    string? CanonicalRoot = null,
    string? RelativePath = null,
    int? Kind = null,
    long? ContentLength = null,
    DateTimeOffset? LastWriteTimeUtc = null,
    int? Attributes = null,
    int? ObservedEntries = null,
    string? Code = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkerRequest))]
[JsonSerializable(typeof(WorkerFrame))]
internal sealed partial class ReadOnlyWorkerJsonContext : JsonSerializerContext;

internal static class ReadOnlyWorkerProtocol
{
    public const int RequestLimit = 16 * 1024;
    public const int FrameLimit = 64 * 1024;

    public static async ValueTask<WorkerRequest> ReadRequestAsync(TextReader input, CancellationToken token)
    {
        var reader = new BoundedNdjsonReader(input, RequestLimit);
        var line = await reader.ReadAsync(token).ConfigureAwait(false);
        var request = line is null ? null : JsonSerializer.Deserialize(line, ReadOnlyWorkerJsonContext.Default.WorkerRequest);
        if (request is null || request.Version != 1 || string.IsNullOrWhiteSpace(request.CanonicalRoot)
            || request.CanonicalRoot.Length > 4096 || request.CanonicalRoot.Contains('\0')
            || await reader.ReadAsync(token).ConfigureAwait(false) is not null)
        {
            throw new ReadOnlyWorkerException("worker_request_invalid");
        }

        return request;
    }

    public static async ValueTask WriteAsync(TextWriter output, WorkerFrame frame, CancellationToken token)
    {
        var line = JsonSerializer.Serialize(frame, ReadOnlyWorkerJsonContext.Default.WorkerFrame);
        if (Encoding.UTF8.GetByteCount(line) > FrameLimit)
        {
            throw new ReadOnlyWorkerException("worker_frame_limit");
        }

        await output.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }
}

internal sealed class ReadOnlyWorkerException(string code, int? nativeError = null) : IOException("The isolated read-only worker failed.")
{
    public string Code { get; } = code;
    public int? NativeError { get; } = nativeError;
}

internal sealed class BoundedNdjsonReader(TextReader input, int maximumBytes)
{
    private readonly char[] buffer = new char[4096];
    private int offset;
    private int count;

    public async ValueTask<string?> ReadAsync(CancellationToken token)
    {
        var line = new StringBuilder();
        while (true)
        {
            if (offset == count)
            {
                count = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                offset = 0;
                if (count == 0)
                {
                    if (line.Length == 0)
                    {
                        return null;
                    }

                    throw new ReadOnlyWorkerException("worker_truncated_frame");
                }
            }

            var character = buffer[offset++];
            if (character == '\n')
            {
                var result = line.ToString().TrimEnd('\r');
                if (Encoding.UTF8.GetByteCount(result) > maximumBytes)
                {
                    throw new ReadOnlyWorkerException("worker_frame_limit");
                }

                return result;
            }

            if (line.Length >= maximumBytes)
            {
                throw new ReadOnlyWorkerException("worker_frame_limit");
            }

            line.Append(character);
        }
    }
}
