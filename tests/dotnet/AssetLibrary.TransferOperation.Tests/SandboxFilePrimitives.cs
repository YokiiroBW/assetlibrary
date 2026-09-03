namespace AssetLibrary.TransferOperation.Tests;

internal static class SandboxFilePrimitives
{
    public static void EnsureParent(SandboxPathBoundary boundary, string path)
    {
        boundary.Revalidate(path);
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("A sandbox file must have a parent.");
        boundary.Revalidate(parent);
        Directory.CreateDirectory(parent);
        boundary.Revalidate(parent);
    }

    public static bool MoveNoReplace(
        SandboxPathBoundary boundary,
        string source,
        string target)
    {
        boundary.Revalidate(source);
        boundary.Revalidate(target);
        EnsureParent(boundary, target);
        if (File.Exists(target) || Directory.Exists(target))
        {
            return false;
        }

        try
        {
            File.Move(source, target, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(target) || Directory.Exists(target))
        {
            return false;
        }
    }

    public static void DeleteEvidenceFile(
        SandboxPathBoundary boundary,
        string path)
    {
        boundary.Revalidate(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
