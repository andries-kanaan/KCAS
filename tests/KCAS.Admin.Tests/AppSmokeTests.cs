using System.Net;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class AppSmokeTests(KcasWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Public_pages_return_success(string url)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("/clients")]
    [InlineData("/clients/new")]
    [InlineData("/clients/1/edit")]
    [InlineData("/clients/1/notes/new")]
    [InlineData("/security")]
    [InlineData("/imports")]
    [InlineData("/compliance")]
    [InlineData("/compliance/settings")]
    [InlineData("/compliance/methodologies")]
    [InlineData("/compliance/audit")]
    [InlineData("/compliance/client-evidence")]
    [InlineData("/clients/1/evidence")]
    public async Task Protected_pages_redirect_anonymous_users_to_login(string url)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Kanaan_header_image_is_served_as_jpeg()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/images/kanaan-header.jpg");

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Kcas_css_contains_shell_rules()
    {
        var client = factory.CreateClient();

        var css = await client.GetStringAsync("/kcas.css");

        Assert.Contains(".top-brand", css);
        Assert.Contains(".sidebar-toggle:checked", css);
    }
}
