namespace AssetLibrary.Windows.Setup;

internal sealed record RegistryValue(string? Text = null, int? Dword = null);
internal sealed record RegistrationState(Dictionary<string, Dictionary<string, RegistryValue>> Keys)
{
    internal static RegistrationState Empty() => new(new Dictionary<string, Dictionary<string, RegistryValue>>(StringComparer.OrdinalIgnoreCase));
}

internal static class RegistrationPlan
{
    internal const string ProductKey = @"Software\AssetLibrary\WindowsClient";
    internal const string ClassKey = @"Software\Classes\CLSID\" + Product.Clsid;
    internal const string NamespaceKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\" + Product.Clsid;
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AssetLibrary";
    internal const string AppPathKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\AssetLibrary.Settings.exe";
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static readonly string[] OwnedRoots = [ClassKey, NamespaceKey, ProductKey, UninstallKey, AppPathKey];

    internal static RegistrationState Create(string directory, string version)
    {
        string host = Path.Combine(directory, "AssetLibrary.Host.exe");
        string setup = Path.Combine(directory, "AssetLibrary.Setup.exe");
        RegistrationState result = RegistrationState.Empty();
        Add(result, ClassKey, ("", "资产库"), ("AssetLibraryOwner", Product.Owner));
        result.Keys[ClassKey]["System.IsPinnedToNameSpaceTree"] = new(Dword: 1);
        Add(result, ClassKey + @"\InprocServer32", ("", Path.Combine(directory, "AssetLibrary.Explorer.dll")),
            ("ThreadingModel", "Apartment"));
        result.Keys[ClassKey + @"\ShellFolder"] = new() { ["Attributes"] = new(Dword: -1476132864) };
        Add(result, ClassKey + @"\DefaultIcon", ("", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "shell32.dll") + ",3"));
        Add(result, NamespaceKey, ("", "资产库"), ("AssetLibraryOwner", Product.Owner));
        Add(result, ProductKey, ("Owner", Product.Owner), ("InstallDir", directory), ("Version", version));
        Add(result, RunKey, ("AssetLibrary", $"\"{host}\" --user-session"));
        Add(result, AppPathKey, ("", Path.Combine(directory, "AssetLibrary.Settings.exe")), ("AssetLibraryOwner", Product.Owner));
        Add(result, UninstallKey, ("DisplayName", "资产库 Explorer 只读浏览（未签名预览）"),
            ("DisplayVersion", version), ("Publisher", "AssetLibrary"), ("InstallLocation", directory),
            ("UninstallString", $"\"{setup}\" uninstall"), ("QuietUninstallString", $"\"{setup}\" uninstall --quiet"),
            ("AssetLibraryOwner", Product.Owner));
        result.Keys[UninstallKey]["NoModify"] = new(Dword: 1);
        result.Keys[UninstallKey]["NoRepair"] = new(Dword: 1);
        return result;
    }

    internal static void ValidateOwnership(RegistrationState state)
    {
        RegistrationState schema = Create("schema", "schema");
        foreach ((string path, Dictionary<string, RegistryValue> values) in state.Keys)
        {
            if (!schema.Keys.TryGetValue(path, out Dictionary<string, RegistryValue>? allowed) ||
                values.Keys.Any(name => !allowed.ContainsKey(name)))
            {
                throw new SetupException("registry_conflict", "注册项含有其他内容，拒绝修改。");
            }
            if (OwnedRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                string field = path == ProductKey ? "Owner" : "AssetLibraryOwner";
                if (!values.TryGetValue(field, out RegistryValue? owner) || owner.Text != Product.Owner)
                {
                    throw new SetupException("foreign_owner", "已有注册不属于资产库，拒绝修改。");
                }
            }
        }
        if (state.Keys.TryGetValue(RunKey, out Dictionary<string, RegistryValue>? run))
        {
            string? directory = InstallDirectory(state);
            if (directory is null || run["AssetLibrary"].Text !=
                Create(directory, "schema").Keys[RunKey]["AssetLibrary"].Text)
            {
                throw new SetupException("registry_conflict", "已有登录启动项不属于本次安装。");
            }
        }
    }

    internal static string? InstallDirectory(RegistrationState state) =>
        state.Keys.TryGetValue(ProductKey, out Dictionary<string, RegistryValue>? values) &&
        values.TryGetValue("InstallDir", out RegistryValue? directory) ? directory.Text : null;

    internal static bool Equal(RegistrationState left, RegistrationState right) =>
        left.Keys.Count == right.Keys.Count && left.Keys.All(entry =>
            right.Keys.TryGetValue(entry.Key, out Dictionary<string, RegistryValue>? values) &&
            entry.Value.Count == values.Count && entry.Value.All(value =>
                values.TryGetValue(value.Key, out RegistryValue? other) && value.Value == other));

    private static void Add(RegistrationState state, string key, params (string Name, string Value)[] values) =>
        state.Keys[key] = values.ToDictionary(value => value.Name, value => new RegistryValue(value.Value), StringComparer.OrdinalIgnoreCase);
}
