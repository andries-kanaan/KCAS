using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Data;

public sealed record SanctionsListEntry(string Reference, string Kind, string Name, IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Documents, IReadOnlyList<string> BirthDates);
public sealed record ParsedSanctionsList(IReadOnlyList<SanctionsListEntry> Entries, string Sha256, DateTime? PublishedAtUtc)
{
    internal IReadOnlyList<(SanctionsListEntry Entry, string[] Names)> MatchingEntries { get; } =
        Entries.Select(e => (e, new[] { e.Name }.Concat(e.Aliases).Select(SanctionsList.Normalise).ToArray())).ToList();
}
public sealed record SanctionsCandidate(string Reference, string Name, string Basis, IReadOnlyList<string> Documents, IReadOnlyList<string> BirthDates);
public sealed record SanctionsNameScope(IReadOnlyList<string> Names, IReadOnlyList<string> Identifiers, bool IsEntity = false, bool RequiresIndividualIdentification = false);
public sealed record SanctionsMatchResult(string Outcome, string Finding, IReadOnlyList<SanctionsCandidate> Candidates);

public static class SanctionsList
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    public const string MatcherVersion = "KCAS_TFS_NAMES_V1";

    public static ParsedSanctionsList Parse(byte[] payload, int minimumIndividuals = 100, int minimumEntities = 25)
    {
        if (payload.Length is 0 or > MaximumBytes) throw new ValidationException("The official XML list is empty or exceeds the size limit.");
        using var stream = new MemoryStream(payload);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes
        });
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw new ValidationException("Missing sanctions XML root.");
        var entries = new List<SanctionsListEntry>();
        DateTime? published = null;
        if (root.Name.LocalName == "NewDataSet")
        {
            foreach (var row in root.Elements())
            {
                if (row.Name.LocalName is not ("Table" or "Table1")) throw new ValidationException("Unexpected FIC XML record type; the list is not accepted as complete.");
                var person = row.Name.LocalName == "Table";
                entries.Add(new(Value(row, "ReferenceNumber"), person ? "Individual" : "Entity",
                    Value(row, person ? "FullName" : "FirstName"),
                    SplitAliases(Value(row, person ? "IndividualAlias" : "EntityAlias")),
                    Values(row, person ? "IndividualDocument" : "EntityDocument"), Values(row, "IndividualDateOfBirth")));
            }
        }
        else if (root.Name.LocalName == "CONSOLIDATED_LIST")
        {
            var sections = root.Elements().ToList();
            if (sections.Count != 2 || !sections.Any(x => x.Name.LocalName == "INDIVIDUALS") || !sections.Any(x => x.Name.LocalName == "ENTITIES"))
                throw new ValidationException("Both official individual and entity sections are required.");
            foreach (var section in sections)
                foreach (var row in section.Elements())
                {
                    var person = section.Name.LocalName == "INDIVIDUALS";
                    if (row.Name.LocalName != (person ? "INDIVIDUAL" : "ENTITY")) throw new ValidationException("Unexpected UN XML record type.");
                    var names = person ? new[] { "FIRST_NAME", "SECOND_NAME", "THIRD_NAME", "FOURTH_NAME" } : ["FIRST_NAME"];
                    var aliases = row.Elements().Where(x => x.Name.LocalName == (person ? "INDIVIDUAL_ALIAS" : "ENTITY_ALIAS")).Select(x => Value(x, "ALIAS_NAME")).ToList();
                    var original = Value(row, "NAME_ORIGINAL_SCRIPT");
                    if (!string.IsNullOrWhiteSpace(original)) aliases.Add(original);
                    entries.Add(new(Value(row, "REFERENCE_NUMBER"), person ? "Individual" : "Entity",
                        string.Join(" ", names.Select(x => Value(row, x)).Where(x => x.Length > 0)), aliases,
                        row.Elements().Where(x => x.Name.LocalName == "INDIVIDUAL_DOCUMENT").Select(x => Value(x, "NUMBER")).Where(x => x.Length > 0).ToList(),
                        row.Elements().Where(x => x.Name.LocalName == "INDIVIDUAL_DATE_OF_BIRTH").Select(x => x.Value.Trim()).ToList()));
                }
            if (DateTime.TryParse((string?)root.Attribute("dateGenerated"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)) published = date;
        }
        else throw new ValidationException("The response is not a supported official FIC/UN XML list.");
        if (entries.Any(x => string.IsNullOrWhiteSpace(x.Name) || !Regex.IsMatch(x.Reference, @"^[A-Za-z]{2,3}[ie]\.\d{3,}$", RegexOptions.CultureInvariant)))
            throw new ValidationException("A designation is missing its name or official reference; partial parsing cannot be cleared.");
        if (entries.Select(x => x.Reference).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new ValidationException("Duplicate official references require source review.");
        if (entries.Count(x => x.Kind == "Individual") < minimumIndividuals || entries.Count(x => x.Kind == "Entity") < minimumEntities)
            throw new ValidationException("The source is empty or implausibly incomplete; no clear results are recorded.");
        if (published > DateTime.UtcNow.AddMinutes(5)) throw new ValidationException("The source carries a future publication timestamp.");
        return new(entries, Convert.ToHexString(SHA256.HashData(payload)), published);
    }

    public static SanctionsMatchResult Match(ParsedSanctionsList list, SanctionsNameScope scope)
    {
        var names = scope.Names.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalise).Distinct().ToList();
        var ids = scope.Identifiers.Select(Identifier).Where(x => x.Length >= 5).Distinct().ToList();
        var candidates = new List<SanctionsCandidate>();
        foreach (var (entry, aliases) in list.MatchingEntries)
        {
            var identifier = ids.Any(id => entry.Documents.Any(d => Identifier(d).Contains(id, StringComparison.Ordinal)));
            var name = names.Any(subject => aliases.Any(alias => Similar(subject, alias)));
            if (identifier || name) candidates.Add(new(entry.Reference, entry.Name, identifier ? "Identifier candidate" : "Name/alias candidate", entry.Documents, entry.BirthDates));
        }
        if (candidates.Count > 0)
            return new("PossibleMatch", "Potential designation candidates require human identifier comparison and recorded resolution; this is not a confirmed designation.", candidates);
        if (scope.RequiresIndividualIdentification || !names.Any(n => scope.IsEntity ? n.Length >= 4 : StrongPersonalName(n)))
            return new("UnidentifiedSubject", "Obtain the actual applicable identity/full names; initials, a joint label or an unidentified beneficiary class cannot be automatically cleared.", []);
        return new("NoMatch", "No designation candidates found for the recorded names/aliases and available identifiers in this validated snapshot. This is limited deterministic screening, not independent identity verification or universal clearance.", []);
    }

    public static string ScopeHash(SanctionsNameScope scope) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        System.Text.Json.JsonSerializer.Serialize(scope))));
    public static string Normalise(string name)
    {
        var text = new string(name.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
        text = Regex.Replace(text, @"[^\p{L}\p{N}]+", " ", RegexOptions.CultureInvariant);
        return string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x is not ("MR" or "MRS" or "MS" or "MISS" or "DR" or "PROF")));
    }
    private static bool StrongPersonalName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 && words.All(x => x.Length >= 2 && x.Any(char.IsLetter)) &&
            !words.Any(x => x is "AND" or "EN" or "FAMILY" or "UNKNOWN" or "BENEFICIARIES" or "CLASS") &&
            Regex.IsMatch(name, @"[A-Z]", RegexOptions.CultureInvariant);
    }
    private static bool Similar(string subject, string listed)
    {
        if (subject.Length == 0 || listed.Length == 0) return false;
        if (subject == listed) return true;
        var a = subject.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = listed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length < 2 || b.Length < 2) return false;
        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length <= b.Length ? b : a;
        var used = new HashSet<int>();
        foreach (var word in shorter)
        {
            var index = Enumerable.Range(0, longer.Length).FirstOrDefault(i => !used.Contains(i) &&
                (word == longer[i] || word.Length == 1 && longer[i].StartsWith(word, StringComparison.Ordinal) ||
                 longer[i].Length == 1 && word.StartsWith(longer[i], StringComparison.Ordinal)), -1);
            if (index < 0) return false;
            used.Add(index);
        }
        return true;
    }
    private static string Identifier(string value) => Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", "", RegexOptions.CultureInvariant);
    private static string Value(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim() ?? "";
    private static List<string> Values(XElement e, string name) => e.Elements().Where(x => x.Name.LocalName == name).Select(x => x.Value.Trim()).Where(x => x.Length > 0).ToList();
    private static List<string> SplitAliases(string text) => Regex.Split(text, @"(?:Good|Low|a\.k\.a\.|f\.k\.a\.),\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        .Select(x => x.Trim(' ', ',')).Where(x => x.Length > 0).ToList();
}

public interface ISanctionsFeed
{
    Task<byte[]> DownloadAsync(CancellationToken cancellationToken);
}

public sealed class OfficialSanctionsFeed(HttpClient http, IOptions<SanctionsAutomationOptions> options) : ISanctionsFeed
{
    public async Task<byte[]> DownloadAsync(CancellationToken cancellationToken)
    {
        var uri = ValidateUrl(options.Value.SourceUrl);
        HttpResponseMessage response;
        for (var redirects = 0; ; redirects++)
        {
            response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) break;
            var location = response.Headers.Location;
            response.Dispose();
            if (redirects >= 4 || location is null) throw new ValidationException("Invalid official-source redirect.");
            uri = ValidateUrl(new Uri(uri, location).AbsoluteUri);
        }
        using var disposableResponse = response;
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && (final.Scheme != "https" || !AllowedHost(final.Host)))
            throw new ValidationException("The official download redirected outside the permitted official source hosts.");
        if (response.Content.Headers.ContentLength > SanctionsList.MaximumBytes) throw new ValidationException("Official list exceeds the download size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + count > SanctionsList.MaximumBytes) throw new ValidationException("Official list exceeds the download size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    public static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || !AllowedHost(uri.Host))
            throw new ValidationException("Configure an official HTTPS FIC/UN XML source without credentials.");
        return uri;
    }
    private static bool AllowedHost(string host) => host is "fic.gov.za" or "tfs.fic.gov.za" or "transfer.fic.gov.za" or "scsanctions.un.org" or "main.un.org" or "unsolprodfiles.blob.core.windows.net";
}
