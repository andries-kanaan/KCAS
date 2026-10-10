using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class ServerFolderBrowserTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"kcas-server-folder-browser-{Guid.NewGuid():N}");
    // Browsing uses only the filesystem; it must not need or create a database.
    private readonly ClientEvidenceReadinessService service = new(null!);

    public ServerFolderBrowserTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task OpensAtTheExactSavedFolderAndListsItsChildren()
    {
        var client = Directory.CreateDirectory(Path.Combine(root, "CLIENT A"));
        var child = Directory.CreateDirectory(Path.Combine(client.FullName, "Storage Data"));

        var browser = await service.BrowseServerFoldersAsync(client.FullName);

        Assert.Equal(client.FullName, browser.CurrentPath);
        Assert.Equal(root, browser.ParentPath);
        Assert.Null(browser.ErrorMessage);
        var entry = Assert.Single(browser.Folders);
        Assert.Equal("Storage Data", entry.Name);
        Assert.Equal(child.FullName, entry.FullPath);
    }

    [Fact]
    public async Task NavigatesNestedFoldersWithoutLosingAnyParentPath()
    {
        var parent = Directory.CreateDirectory(Path.Combine(root, "CLIENT A", "Storage Data"));
        var child = Directory.CreateDirectory(Path.Combine(parent.FullName, "2026"));

        var browser = await service.BrowseServerFoldersAsync(child.FullName);

        Assert.Equal(child.FullName, browser.CurrentPath);
        Assert.Equal(parent.FullName, browser.ParentPath);
        Assert.Empty(browser.Folders);
        Assert.Null(browser.ErrorMessage);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
