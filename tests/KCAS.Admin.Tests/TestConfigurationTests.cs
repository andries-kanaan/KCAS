namespace KCAS.Admin.Tests;

public sealed class TestConfigurationTests
{
    [Theory]
    [InlineData("server=localhost;database=kcas_blazor;user=test")]
    [InlineData("server=localhost;database=production;user=test")]
    [InlineData("server=localhost;user=test")]
    public void Dedicated_test_database_guard_rejects_unsafe_targets(string connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => TestConfiguration.RequireDedicatedTestDatabase(connectionString));

        Assert.Contains("only use a database whose name contains 'test'", exception.Message);
    }

    [Fact]
    public void Dedicated_test_database_guard_accepts_test_target()
    {
        const string connectionString =
            "server=localhost;database=kcas_blazor_test;user=test";

        Assert.Equal(
            connectionString,
            TestConfiguration.RequireDedicatedTestDatabase(connectionString));
    }
}
