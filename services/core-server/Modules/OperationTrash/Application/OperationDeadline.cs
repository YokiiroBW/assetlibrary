namespace AssetLibrary.Modules.OperationTrash.Application;

internal static class OperationDeadline
{
    public static TimeSpan Shortest(params TimeSpan[] values)
    {
        var shortest = values[0];
        foreach (var value in values)
        {
            if (value < shortest)
            {
                shortest = value;
            }
        }

        return shortest <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : shortest;
    }

    public static DateTimeOffset Earliest(params DateTimeOffset[] values)
    {
        var earliest = values[0];
        foreach (var value in values)
        {
            if (value < earliest)
            {
                earliest = value;
            }
        }

        return earliest;
    }
}
