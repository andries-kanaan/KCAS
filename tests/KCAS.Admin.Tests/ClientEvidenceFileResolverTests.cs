using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class ClientEvidenceFileResolverTests
{
    [Fact]
    public void Preferred_server_path_maps_local_evidence_to_active_live_root()
    {
        var result = ClientEvidenceFileResolver.PreferredServerPath(
            @"C:\Download\_kanaan\ClientsKanaan\VAN ASWEGEN G and MWD (Yuba Trust)\Storage Data\FICA\address.pdf",
            @"Storage Data\FICA\address.pdf",
            "address.pdf",
            @"E:\Userdata\Kanaan Trust\Clients\VAN ASWEGEN G and MWD (Yuba Trust)",
            @"E:\Userdata\Kanaan Trust\Clients");

        Assert.Equal(
            @"E:\Userdata\Kanaan Trust\Clients\VAN ASWEGEN G and MWD (Yuba Trust)\Storage Data\FICA\address.pdf",
            result);
    }

    [Fact]
    public void Resolve_existing_path_uses_client_folder_and_relative_path()
    {
        var clientFolder = Path.Combine(Path.GetTempPath(), $"kcas-evidence-{Guid.NewGuid():N}");
        var relativePath = Path.Combine("FICA", "address.pdf");
        var expected = Path.Combine(clientFolder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllText(expected, "test evidence");

        try
        {
            var result = ClientEvidenceFileResolver.ResolveExistingPath(
                @"C:\unavailable\address.pdf",
                relativePath,
                "address.pdf",
                clientFolder,
                null);

            Assert.Equal(expected, result);
        }
        finally
        {
            Directory.Delete(clientFolder, recursive: true);
        }
    }

    [Fact]
    public void Resolve_existing_path_rejects_relative_path_traversal()
    {
        var clientFolder = Path.Combine(Path.GetTempPath(), $"kcas-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(clientFolder);

        try
        {
            Assert.Null(ClientEvidenceFileResolver.ResolveExistingPath(
                null,
                @"..\outside.pdf",
                null,
                clientFolder,
                null));
        }
        finally
        {
            Directory.Delete(clientFolder, recursive: true);
        }
    }
}
