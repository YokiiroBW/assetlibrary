using System.Text;
using System.Text.Json;

namespace AssetLibrary.Modules.TaskHealth.Contracts;

public readonly record struct DurableTaskId
{
    public DurableTaskId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    public Guid Value { get; }

    public static DurableTaskId New() => new(Guid.NewGuid());
}

public readonly record struct OutboxEventId
{
    public OutboxEventId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    public Guid Value { get; }

    public static OutboxEventId New() => new(Guid.NewGuid());
}

public readonly record struct LeaseToken
{
    public LeaseToken(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    public Guid Value { get; }

    public static LeaseToken New() => new(Guid.NewGuid());
}

public readonly record struct TaskIdempotencyKey
{
    public TaskIdempotencyKey(string value)
    {
        Value = TaskHealthValueRules.RequiredText(value, 200, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct TaskTypeName
{
    public TaskTypeName(string value)
    {
        Value = TaskHealthValueRules.LowercaseKey(value, 100, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct EventTypeName
{
    public EventTypeName(string value)
    {
        Value = TaskHealthValueRules.LowercaseKey(value, 150, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct ModuleName
{
    public ModuleName(string value)
    {
        Value = TaskHealthValueRules.PascalCaseKey(value, 100, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct LeaseOwner
{
    public LeaseOwner(string value)
    {
        Value = TaskHealthValueRules.LowercaseKey(value, 200, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct FailureCode
{
    public FailureCode(string value)
    {
        Value = TaskHealthValueRules.LowercaseKey(value, 100, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct HealthComponentName
{
    public HealthComponentName(string value)
    {
        Value = TaskHealthValueRules.LowercaseKey(value, 100, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct HealthReasonCode
{
    public HealthReasonCode(string value)
    {
        Value = TaskHealthValueRules.LowercaseKey(value, 100, nameof(value));
    }

    public string Value { get; }
}

public readonly record struct JsonObjectPayload
{
    public const int MaximumUtf8Bytes = 262_144;

    public JsonObjectPayload(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (Encoding.UTF8.GetByteCount(value) > MaximumUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"A task payload cannot exceed {MaximumUtf8Bytes} UTF-8 bytes.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                value,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A task payload must be a JSON object.", nameof(value));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A task payload must contain valid JSON.", nameof(value), exception);
        }

        Value = value;
    }

    public string Value { get; }
}

internal static class TaskHealthValueRules
{
    public static string RequiredText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('\0')
            || value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value must be trimmed, contain no NUL and be at most {maximumLength} characters.",
                parameterName);
        }

        return value;
    }

    public static string LowercaseKey(string value, int maximumLength, string parameterName)
    {
        var candidate = RequiredText(value, maximumLength, parameterName);
        if (!char.IsAsciiLetterLower(candidate[0])
            || candidate.Any(character =>
                !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException(
                "The value must be a lowercase dotted identifier.",
                parameterName);
        }

        return candidate;
    }

    public static string PascalCaseKey(string value, int maximumLength, string parameterName)
    {
        var candidate = RequiredText(value, maximumLength, parameterName);
        if (!char.IsAsciiLetterUpper(candidate[0])
            || candidate.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "The value must start with an uppercase ASCII letter and contain only ASCII letters or digits.",
                parameterName);
        }

        return candidate;
    }
}
