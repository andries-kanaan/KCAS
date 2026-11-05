using KCAS.Admin.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

public sealed class GoAmlReminderConfigurationTests
{
    [Theory]
    [InlineData("Production", true, false, true, "https://live.example.test")]
    [InlineData("Production", false, true, false, "https://live.example.test")]
    [InlineData("Development", true, false, false, "https://local.example.test")]
    [InlineData("Development", true, true, true, "https://local.example.test")]
    [InlineData("Development", false, true, false, "https://local.example.test")]
    [InlineData("Testing", true, true, false, "https://live.example.test")]
    [InlineData("Staging", true, true, false, "https://live.example.test")]
    public void Bound_settings_require_explicit_local_opt_in_and_block_other_environments(
        string environment, bool enabled, bool localEnabled, bool expectedEnabled, string expectedUrl)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoAmlReminders:Enabled"] = enabled.ToString(),
            ["GoAmlReminders:LocalEnabled"] = localEnabled.ToString(),
            ["GoAmlReminders:PublicBaseUrl"] = "https://live.example.test",
            ["GoAmlReminders:LocalPublicBaseUrl"] = "https://local.example.test"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<GoAmlReminderOptions>().BindConfiguration("GoAmlReminders")
            .PostConfigure(options => options.ApplyEnvironment(environment));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GoAmlReminderOptions>>().Value;
        Assert.Equal(expectedEnabled, options.Enabled);
        Assert.Equal(expectedUrl, options.PublicBaseUrl);
    }

    [Fact]
    public void Missing_password_blocks_configured_Gmail_sender()
    {
        var sender = new GoAmlSmtpEmailSender(Options.Create(new GoAmlSmtpOptions
        {
            Host = "smtp.gmail.com", From = "sender@example.test", UserName = "sender@example.test"
        }));
        Assert.NotNull(sender.ConfigurationProblem);
    }
}
