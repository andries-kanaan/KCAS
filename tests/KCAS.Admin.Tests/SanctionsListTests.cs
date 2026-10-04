using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using KCAS.Admin.Data;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

public sealed class SanctionsListTests
{
    internal static byte[] Xml(string name = "Listed Person", string entity = "Listed Company") => Encoding.UTF8.GetBytes($"""
        <NewDataSet><Table><ReferenceNumber>QDi.001</ReferenceNumber><FullName>{name}</FullName>
        <IndividualAlias>Good, Known Alias</IndividualAlias><IndividualDocument>Passport AB12345</IndividualDocument></Table>
        <Table1><ReferenceNumber>QDe.001</ReferenceNumber><FirstName>{entity}</FirstName></Table1></NewDataSet>
        """);

    [Fact]
    public void Parses_actual_FIC_format_without_inventing_publication_time()
    {
        var list = SanctionsList.Parse(Xml(), 1, 1);
        Assert.Equal(2, list.Entries.Count);
        Assert.Null(list.PublishedAtUtc);
        Assert.Equal(64, list.Sha256.Length);
        Assert.Equal("Known Alias", Assert.Single(list.Entries[0].Aliases));
        Assert.Equal("PossibleMatch", SanctionsList.Match(list, new(["Alias Known"], [])).Outcome);
        Assert.Equal("PossibleMatch", SanctionsList.Match(list, new(["Other Person"], ["AB12345"])).Outcome);
        Assert.Equal("NoMatch", SanctionsList.Match(list, new(["Different Individual"], [])).Outcome);
    }

    [Theory]
    [InlineData("L Person")]
    [InlineData("Person Listed")]
    [InlineData("Dr Listed Person")]
    [InlineData("Lísted Person")]
    public void Name_alias_normalisation_returns_candidates_not_confirmed_designations(string name)
        => Assert.Equal("PossibleMatch", SanctionsList.Match(SanctionsList.Parse(Xml(), 1, 1), new([name], [])).Outcome);

    [Theory]
    [InlineData("X Y")]
    [InlineData("Family Beneficiaries")]
    [InlineData("A and B Example")]
    public void Weak_or_joint_identity_cannot_be_cleared(string name)
        => Assert.Equal("UnidentifiedSubject", SanctionsList.Match(SanctionsList.Parse(Xml(), 1, 1), new([name], [])).Outcome);

    [Fact]
    public void Duplicate_tokens_do_not_reuse_one_listed_token()
    {
        var list = SanctionsList.Parse(Xml("Alpha Beta"), 1, 1);
        Assert.Equal("NoMatch", SanctionsList.Match(list, new(["Alpha Alpha"], [])).Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>error</html>")]
    [InlineData("<NewDataSet/>")]
    [InlineData("<CONSOLIDATED_LIST><INDIVIDUALS/></CONSOLIDATED_LIST>")]
    public void Empty_error_or_partial_sources_fail_closed(string xml)
        => Assert.Throws<ValidationException>(() => SanctionsList.Parse(Encoding.UTF8.GetBytes(xml), 1, 1));

    [Fact]
    public void DTD_is_prohibited()
        => Assert.Throws<System.Xml.XmlException>(() => SanctionsList.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///secret'>]><NewDataSet>&x;</NewDataSet>"), 1, 1));

    [Theory]
    [InlineData("http://tfs.fic.gov.za/file.xml")]
    [InlineData("https://tfs.fic.gov.za.attacker.example/file.xml")]
    [InlineData("https://user:password@tfs.fic.gov.za/file.xml")]
    public void Untrusted_source_URL_is_rejected(string url)
        => Assert.Throws<ValidationException>(() => OfficialSanctionsFeed.ValidateUrl(url));

    [Fact]
    public async Task Untrusted_redirect_is_rejected_before_requesting_it()
    {
        var handler = new RedirectHandler();
        var feed = new OfficialSanctionsFeed(new HttpClient(handler), Options.Create(new SanctionsAutomationOptions()));
        await Assert.ThrowsAsync<ValidationException>(() => feed.DownloadAsync(default));
        Assert.Equal(1, handler.Requests);
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect) { RequestMessage = request };
            response.Headers.Location = new Uri("https://untrusted.example/file.xml");
            return Task.FromResult(response);
        }
    }
}
