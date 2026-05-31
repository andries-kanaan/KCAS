namespace KCAS.Admin.Tests;

[CollectionDefinition(Name)]
public sealed class KcasTestCollection : ICollectionFixture<KcasWebApplicationFactory>
{
    public const string Name = "KCAS integration tests";
}
