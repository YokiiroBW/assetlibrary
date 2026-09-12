using System.Text.Json;

namespace AssetLibrary.Windows.Setup;

internal sealed record RegistrationJournal(RegistrationState Before, RegistrationState After);

internal sealed class RegistrationTransaction(string root, IRegistrationStore store)
{
    private string JournalPath => Path.Combine(root, "transaction.json");

    internal void Recover()
    {
        SafePath.RejectReparsePoints(JournalPath);
        if (!File.Exists(JournalPath)) { return; }
        if (new FileInfo(JournalPath).Length > 256 * 1024) { throw new SetupException("invalid_state", "恢复日志超过限制。"); }
        RegistrationJournal journal = JsonSerializer.Deserialize<RegistrationJournal>(File.ReadAllText(JournalPath), Product.Json)
            ?? throw new SetupException("invalid_state", "恢复日志损坏。");
        ValidateJournal(journal);
        RegistrationState current = store.Read();
        foreach ((string path, Dictionary<string, RegistryValue> values) in current.Keys)
        {
            foreach ((string name, RegistryValue value) in values)
            {
                if (!Matches(journal.Before, path, name, value) && !Matches(journal.After, path, name, value))
                {
                    throw new SetupException("recovery_conflict", "安装中断后注册项已被其他内容修改；保留日志并停止。");
                }
            }
        }
        store.Write(journal.Before);
        Verify(journal.Before);
        File.Delete(JournalPath);
    }

    internal void Commit(RegistrationState before, RegistrationState after)
    {
        RegistrationPlan.ValidateOwnership(before);
        RegistrationPlan.ValidateOwnership(after);
        StateFile.Write(JournalPath, new RegistrationJournal(before, after));
        try
        {
            Verify(before);
            store.Write(after);
            Verify(after);
            File.Delete(JournalPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SetupException)
        {
            // Leave the durable journal if rollback itself fails; the next run can recover it safely.
            Recover();
            throw;
        }
    }

    private void ValidateJournal(RegistrationJournal journal)
    {
        RegistrationPlan.ValidateOwnership(journal.Before);
        RegistrationPlan.ValidateOwnership(journal.After);
        foreach (RegistrationState state in new[] { journal.Before, journal.After })
        {
            string? directory = RegistrationPlan.InstallDirectory(state);
            if (directory is not null) { SafePath.RequireInside(Path.Combine(root, "versions"), directory); }
        }
    }

    private void Verify(RegistrationState expected)
    {
        if (!RegistrationPlan.Equal(store.Read(), expected))
        {
            throw new SetupException("registry_readback", "注册项回读不一致，安装已停止。");
        }
    }

    private static bool Matches(RegistrationState state, string path, string name, RegistryValue value) =>
        state.Keys.TryGetValue(path, out Dictionary<string, RegistryValue>? values) &&
        values.TryGetValue(name, out RegistryValue? expected) && expected == value;
}
