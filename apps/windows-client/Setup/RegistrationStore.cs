using Microsoft.Win32;
using System.Text.Json;

namespace AssetLibrary.Windows.Setup;

// Two concrete adapters keep sandbox tests on the same installer transaction path without touching HKCU.
internal interface IRegistrationStore
{
    RegistrationState Read();
    void Write(RegistrationState state);
}

internal sealed class SandboxRegistrationStore(string path) : IRegistrationStore
{
    public RegistrationState Read()
    {
        SafePath.RejectReparsePoints(path);
        return File.Exists(path) ? JsonSerializer.Deserialize<RegistrationState>(File.ReadAllText(path), Product.Json)
            ?? throw new SetupException("invalid_state", "沙箱注册状态损坏。") : RegistrationState.Empty();
    }

    public void Write(RegistrationState state) => StateFile.Write(path, state);
}

internal sealed class WindowsRegistrationStore : IRegistrationStore
{
    public RegistrationState Read()
    {
        RegistrationState state = RegistrationState.Empty();
        using RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        foreach (string root in RegistrationPlan.OwnedRoots) { ReadTree(user, root, state); }
        using RegistryKey? run = user.OpenSubKey(RegistrationPlan.RunKey);
        object? value = run?.GetValue("AssetLibrary", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is not null)
        {
            state.Keys[RegistrationPlan.RunKey] = new() { ["AssetLibrary"] = ConvertValue(value) };
        }
        return state;
    }

    public void Write(RegistrationState state)
    {
        using RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        foreach (string root in RegistrationPlan.OwnedRoots) { user.DeleteSubKeyTree(root, false); }
        using (RegistryKey? run = user.OpenSubKey(RegistrationPlan.RunKey, true)) { run?.DeleteValue("AssetLibrary", false); }
        // Ownership is written before any other value, making interrupted writes recoverable and attributable.
        foreach ((string path, Dictionary<string, RegistryValue> values) in state.Keys)
        {
            using RegistryKey key = user.CreateSubKey(path);
            foreach ((string name, RegistryValue value) in values.OrderBy(entry =>
                         entry.Key is "Owner" or "AssetLibraryOwner" ? 0 : 1))
            {
                if (value.Dword is int number) { key.SetValue(name, number, RegistryValueKind.DWord); }
                else { key.SetValue(name, value.Text ?? throw new SetupException("invalid_state", "注册值为空。"), RegistryValueKind.String); }
            }
            key.Flush();
        }
    }

    private static void ReadTree(RegistryKey user, string path, RegistrationState state)
    {
        if (state.Keys.Count > 32) { throw new SetupException("registry_conflict", "已有注册项过多。"); }
        using RegistryKey? key = user.OpenSubKey(path);
        if (key is null) { return; }
        Dictionary<string, RegistryValue> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in key.GetValueNames())
        {
            values.Add(name, ConvertValue(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)));
        }
        state.Keys.Add(path, values);
        foreach (string child in key.GetSubKeyNames()) { ReadTree(user, path + "\\" + child, state); }
    }

    private static RegistryValue ConvertValue(object? value) => value switch
    {
        string text => new(Text: text),
        int number => new(Dword: number),
        _ => throw new SetupException("registry_conflict", "已有注册值类型不受支持。")
    };
}

internal static class StateFile
{
    internal static void Write<T>(string path, T value)
    {
        SafePath.RejectReparsePoints(path);
        string temporary = path + ".new-" + Guid.NewGuid().ToString("N");
        using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Product.Json);
            stream.Flush(true);
        }
        SafePath.RejectReparsePoints(path);
        File.Move(temporary, path, true);
    }
}
