using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxFixture : IDisposable
{
    private readonly Dictionary<TransferLocationToken, string> locations = [];
    private bool disposed;

    public SandboxFixture()
    {
        RepositoryRoot = FindRepositoryRoot();
        var sandboxBase = Path.Combine(
            RepositoryRoot,
            ".runtime",
            "sandbox-storage",
            "V01-007");
        Directory.CreateDirectory(sandboxBase);
        Root = Path.Combine(sandboxBase, $"fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        File.WriteAllText(
            Path.Combine(Root, SandboxPathBoundary.MarkerName),
            SandboxPathBoundary.MarkerValue);
        Boundary = SandboxPathBoundary.Open(RepositoryRoot, Root);
    }

    public SandboxPathBoundary Boundary { get; }

    public string RepositoryRoot { get; }

    public string Root { get; }

    public TransferLocationToken Register(string tokenValue, string relativePath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var token = new TransferLocationToken(tokenValue);
        Boundary.ResolveRelative(relativePath);
        locations.Add(token, relativePath);
        return token;
    }

    public string Resolve(TransferLocationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!locations.TryGetValue(token, out var relative))
        {
            throw new InvalidOperationException("The sandbox token is not registered.");
        }

        return Boundary.ResolveRelative(relative);
    }

    public string InternalTrashPath(OperationPlanId planId, OperationItemId itemId) =>
        Boundary.ResolveInternal(
            "trash",
            planId.Value.ToString("N"),
            itemId.Value.ToString("N"),
            "payload");

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var reopened = SandboxPathBoundary.Open(RepositoryRoot, Root);
        if (!string.Equals(reopened.Root, Root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The sandbox cleanup target changed.");
        }

        Directory.Delete(Root, recursive: true);
    }

    private static string FindRepositoryRoot()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            if (File.Exists(Path.Combine(cursor.FullName, "AGENTS.md"))
                && (File.Exists(Path.Combine(cursor.FullName, ".git"))
                    || Directory.Exists(Path.Combine(cursor.FullName, ".git"))))
            {
                return cursor.FullName;
            }

            cursor = cursor.Parent;
        }

        throw new InvalidOperationException("The repository root could not be found.");
    }
}
