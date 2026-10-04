using Microsoft.AspNetCore.WebUtilities;

namespace KCAS.Admin.Components.Shared;

public static class ClientListNavigation
{
    private static readonly string[] ListPaths = ["/clients", "/clients/operational-review", "/compliance/client-evidence"];

    public static string? SafeReturnPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains('#') || path.Any(char.IsControl)) return null;
        var route = path.Split('?', 2)[0];
        return ListPaths.Contains(route, StringComparer.Ordinal) ? path : null;
    }

    public static string? ReturnPathFromUri(string uri)
    {
        var query = QueryHelpers.ParseQuery(new Uri(uri).Query);
        return query.TryGetValue("returnTo", out var value) ? SafeReturnPath(value.ToString()) : null;
    }

    public static string WithReturnPath(string destination, string? returnPath) =>
        SafeReturnPath(returnPath) is { } safe ? QueryHelpers.AddQueryString(destination, "returnTo", safe) : destination;

    public static string ClientLink(int clientId, string suffix, string listUri) =>
        WithReturnPath($"/clients/{clientId}{suffix}", new Uri(listUri).PathAndQuery);

    public static string AdviceStatus(string? status) => status switch
    {
        "ReadyForReview" => "Awaiting independent review",
        "ApprovedForIssue" => "Approved for issue",
        "Returned" => "Returned for changes",
        null => "No advice case",
        _ => status
    };
}
