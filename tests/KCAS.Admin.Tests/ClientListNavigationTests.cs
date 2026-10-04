using KCAS.Admin.Components.Shared;

namespace KCAS.Admin.Tests;

public sealed class ClientListNavigationTests
{
    [Theory]
    [InlineData("/clients?q=Example&sort=currentValue&direction=desc&page=2")]
    [InlineData("/clients/operational-review?lifecycle=Current&review=incomplete")]
    [InlineData("/compliance/client-evidence?kanaanId=EXAMPLE&readiness=NotReady")]
    public void List_context_round_trips_without_losing_filters(string path)
    {
        var link = ClientListNavigation.ClientLink(42, "/evidence", "https://kcas.test:8443" + path);
        Assert.Equal(path, ClientListNavigation.ReturnPathFromUri("https://kcas.test:8443" + link));
        var investments = ClientListNavigation.WithReturnPath("/clients/42#investments", path);
        Assert.EndsWith("#investments", investments);
        Assert.Equal(path, ClientListNavigation.ReturnPathFromUri("https://kcas.test:8443" + investments));
    }

    [Theory]
    [InlineData("https://example.test/clients")]
    [InlineData("//example.test/clients")]
    [InlineData("/imports")]
    [InlineData("/clients/42")]
    [InlineData("/clients\\other")]
    [InlineData("/clients#other")]
    [InlineData("/clients\r\nLocation: elsewhere")]
    public void Return_links_only_accept_known_local_client_lists(string path)
    {
        Assert.Null(ClientListNavigation.SafeReturnPath(path));
        Assert.Equal("/clients/42", ClientListNavigation.WithReturnPath("/clients/42", path));
    }

    [Theory]
    [InlineData(null, "No advice case")]
    [InlineData("ReadyForReview", "Awaiting independent review")]
    [InlineData("ApprovedForIssue", "Approved for issue")]
    [InlineData("Returned", "Returned for changes")]
    [InlineData("Issued", "Issued")]
    public void Advice_status_labels_do_not_imply_approval(string? status, string expected) =>
        Assert.Equal(expected, ClientListNavigation.AdviceStatus(status));
}
