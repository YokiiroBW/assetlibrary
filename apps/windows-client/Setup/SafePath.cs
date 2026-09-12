namespace AssetLibrary.Windows.Setup;

internal static class SafePath
{
    internal static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 220 || relative.Contains('\\') ||
            Path.IsPathRooted(relative) || relative.Split('/').Length > 16)
        {
            throw new SetupException("unsafe_path", "安装包包含无效相对路径。");
        }
        foreach (string part in relative.Split('/'))
        {
            string name = part.Split('.')[0];
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsReserved(name))
            {
                throw new SetupException("unsafe_path", "安装包包含保留名称或越界路径。");
            }
        }
        string result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        RequireInside(root, result);
        RejectReparsePoints(result);
        return result;
    }

    internal static void RequireInside(string root, string candidate)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(candidate).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new SetupException("unsafe_path", "安装目标不在授权目录内。");
        }
    }

    internal static void RejectReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            // GetAttributes also detects a dangling link, unlike File.Exists.
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new SetupException("reparse_point", "安装路径包含链接或重解析点。");
                }
            }
            catch (FileNotFoundException) { /* The new path will be created under a verified parent. */ }
            catch (DirectoryNotFoundException) { /* The new path will be created under a verified parent. */ }
        }
    }

    internal static IEnumerable<string> Files(string root)
    {
        RejectReparsePoints(root);
        Stack<string> pending = new();
        pending.Push(root);
        int count = 0;
        while (pending.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 8192) { throw new SetupException("manifest_limit", "安装目录条目数量超过限制。"); }
                Resolve(root, Path.GetRelativePath(root, entry).Replace('\\', '/'));
                if (Directory.Exists(entry)) { pending.Push(entry); }
                else { yield return entry; }
            }
        }
    }

    private static bool IsReserved(string value) =>
        value.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
        (value.Length == 4 && value[3] is >= '1' and <= '9' &&
         (value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
          value.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
}
