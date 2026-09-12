namespace AssetLibrary.Windows.Setup;

internal sealed record SetupOptions(string Command, string Package, string? Sandbox, bool Quiet)
{
    internal static SetupOptions Parse(string[] args)
    {
        string command = "install";
        string package = AppContext.BaseDirectory;
        string? sandbox = null;
        bool quiet = false;
        int index = 0;
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)) { command = args[index++]; }
        while (index < args.Length)
        {
            string option = args[index++];
            if (option == "--quiet") { quiet = true; }
            else if (option is "--package" or "--sandbox" && index < args.Length)
            {
                string value = Path.GetFullPath(args[index++]);
                if (option == "--package") { package = value; }
                else { sandbox = value; }
            }
            else { throw new SetupException("invalid_arguments", "参数无效。支持 install、uninstall、status、--package、--sandbox、--quiet。"); }
        }
        if (command is not ("install" or "uninstall" or "status")) { throw new SetupException("invalid_arguments", "未知安装操作。"); }
        if (sandbox is not null)
        {
            SafePath.RequireInside(Path.GetTempPath(), sandbox);
            SafePath.RejectReparsePoints(sandbox);
            quiet = true;
        }
        return new(command, package, sandbox, quiet);
    }
}
