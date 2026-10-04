using KCAS.Admin.Security;

namespace KCAS.Admin.Components.Layout;

public sealed record WorkspaceNavigationItem(string Label, string Href, string Policy);
public sealed record WorkspaceNavigationGroup(string Label, string Icon, IReadOnlyList<WorkspaceNavigationItem> Items);

public static class WorkspaceNavigation
{
    public static readonly IReadOnlyList<WorkspaceNavigationGroup> Groups =
    [
        new("Clients", "bi-person-fill-nav-menu", [
            new("Client register", "/clients", KcasPermissions.ClientsView),
            new("Client compliance reviews", "/clients/operational-review", KcasPermissions.ClientsView),
            new("Evidence readiness", "/compliance/client-evidence", KcasPermissions.ComplianceView),
            new("Client risk register", "/compliance/client-risk", KcasPermissions.RiskAssessmentsView)]),
        new("Investments", "bi-list-nested-nav-menu", [
            new("Investment summary", "/investments/summary", KcasPermissions.InvestmentsView),
            new("Reconciliation", "/investments/reconciliation", KcasPermissions.InvestmentsManage)]),
        new("Advice", "bi-list-nested-nav-menu", [new("Advice register", "/advice", KcasPermissions.AdviceView)]),
        new("Compliance", "bi-person-badge-nav-menu", [
            new("Overview", "/compliance", KcasPermissions.ComplianceView),
            new("Monitoring and remediation", "/compliance/work-register", KcasPermissions.ComplianceView),
            new("Sanctions coverage", "/compliance/sanctions", KcasPermissions.ComplianceView),
            new("Complaints register", "/compliance/complaints", KcasPermissions.ComplianceView),
            new("Employee compliance", "/compliance/employees", KcasPermissions.EmployeesView),
            new("Programme and regulatory evidence", "/compliance/programme", KcasPermissions.ComplianceView),
            new("Inspection response", "/compliance/inspections", KcasPermissions.InspectionsView)]),
        new("Administration", "bi-person-badge-nav-menu", [
            new("Users and permissions", "/security", KcasPermissions.SecurityManage),
            new("Kanaan SQL imports", "/imports", KcasPolicies.AdministratorOnly),
            new("Client review transfers", "/compliance/review-transfers", KcasPolicies.AdministratorOnly),
            new("Duplicate reconciliations", "/compliance/duplicate-transfers", KcasPolicies.AdministratorOnly),
            new("Advice transfers", "/advice/transfers", KcasPolicies.AdministratorOnly),
            new("Programme transfers", "/compliance/programme-transfers", KcasPolicies.AdministratorOnly),
            new("Employee transfers", "/compliance/employees/transfers", KcasPermissions.EmployeesTransfer),
            new("goAML transfers", "/compliance/goaml/transfers", KcasPolicies.AdministratorOnly)])
    ];

    public static IReadOnlyList<WorkspaceNavigationGroup> VisibleGroups(IReadOnlySet<string> allowedPolicies) =>
        Groups.Select(group => group with { Items = group.Items.Where(item => allowedPolicies.Contains(item.Policy)).ToArray() })
            .Where(group => group.Items.Count > 0).ToArray();

    public static string? ActiveHref(string relativeUrl)
    {
        var path = "/" + relativeUrl.Split('?', '#')[0].Trim('/');
        if (path == "/") return "/";
        // Specific routes precede their broader workspace so only one destination is selected.
        foreach (var item in Groups.SelectMany(group => group.Items).Where(item => item.Href != "/clients" && item.Href != "/compliance" && item.Href != "/advice")
                     .OrderByDescending(item => item.Href.Length))
            if (IsWithin(path, item.Href)) return item.Href;
        if (IsWithin(path, "/advice") || (IsWithin(path, "/clients") && path.Split('/').Skip(3).FirstOrDefault()?.Equals("advice", StringComparison.OrdinalIgnoreCase) == true)) return "/advice";
        if (IsWithin(path, "/clients")) return "/clients";
        if (IsWithin(path, "/compliance")) return path.Equals("/compliance", StringComparison.OrdinalIgnoreCase) || IsWithin(path, "/compliance/goaml") ? "/compliance" : "/compliance/programme";
        return null;
    }

    private static bool IsWithin(string path, string prefix) => path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
