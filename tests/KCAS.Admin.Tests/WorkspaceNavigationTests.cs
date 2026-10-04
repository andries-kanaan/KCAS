using KCAS.Admin.Components.Layout;
using KCAS.Admin.Security;

namespace KCAS.Admin.Tests;

public sealed class WorkspaceNavigationTests
{
    [Theory]
    [InlineData("", "/")]
    [InlineData("clients", "/clients")]
    [InlineData("clients/123/edit", "/clients")]
    [InlineData("clients/123/evidence", "/clients")]
    [InlineData("clients/123/investments/45/returns", "/clients")]
    [InlineData("clients/operational-review?search=example", "/clients/operational-review")]
    [InlineData("clients/123/advice/4/preparation", "/advice")]
    [InlineData("clients/123#advice", "/clients")]
    [InlineData("advice/transfers?clientId=123", "/advice/transfers")]
    [InlineData("compliance/employees/transfers", "/compliance/employees/transfers")]
    [InlineData("compliance/employees/4", "/compliance/employees")]
    [InlineData("compliance/client-evidence", "/compliance/client-evidence")]
    [InlineData("compliance/client-risk", "/compliance/client-risk")]
    [InlineData("compliance/business-risk/4", "/compliance/programme")]
    [InlineData("compliance/methodologies/2/review", "/compliance/programme")]
    [InlineData("compliance/tasks", "/compliance/programme")]
    [InlineData("compliance/work-register/1", "/compliance/work-register")]
    [InlineData("compliance/sanctions/1", "/compliance/sanctions")]
    [InlineData("compliance/goaml", "/compliance")]
    [InlineData("compliance/goaml/transfers", "/compliance/goaml/transfers")]
    [InlineData("COMPLIANCE/RMCP", "/compliance/programme")]
    [InlineData("imports/4", "/imports")]
    [InlineData("Account/Manage", null)]
    [InlineData("clientsevil", null)]
    public void Only_the_specific_workspace_destination_is_active(string url, string? expected)
    {
        Assert.Equal(expected, WorkspaceNavigation.ActiveHref(url));
    }

    [Fact]
    public void Permission_filtering_removes_inaccessible_items_and_empty_groups()
    {
        Assert.Empty(WorkspaceNavigation.VisibleGroups(new HashSet<string>()));
        var groups = WorkspaceNavigation.VisibleGroups(new HashSet<string> { KcasPermissions.ClientsView });
        var group = Assert.Single(groups);
        Assert.Equal("Clients", group.Label);
        Assert.Equal(new[] { "/clients", "/clients/operational-review" }, group.Items.Select(item => item.Href));
    }

    [Fact]
    public void Transfer_permissions_remain_distinct_from_general_compliance_access()
    {
        var groups = WorkspaceNavigation.VisibleGroups(new HashSet<string> { KcasPermissions.ComplianceView, KcasPermissions.EmployeesView });
        Assert.DoesNotContain(groups, group => group.Label == "Administration");
        Assert.DoesNotContain(groups.SelectMany(group => group.Items), item => item.Href.EndsWith("transfers"));
        var transferGroups = WorkspaceNavigation.VisibleGroups(new HashSet<string> { KcasPermissions.EmployeesTransfer });
        Assert.Equal("/compliance/employees/transfers", Assert.Single(Assert.Single(transferGroups).Items).Href);
    }

    [Fact]
    public void Menu_destinations_are_unique_and_goaml_check_remains_on_the_overview()
    {
        var items = WorkspaceNavigation.Groups.SelectMany(group => group.Items).ToArray();
        Assert.Equal(items.Length, items.Select(item => item.Href).Distinct().Count());
        Assert.DoesNotContain(items, item => item.Href == "/compliance/goaml");
        Assert.Equal(new[] { "Clients", "Investments", "Advice", "Compliance", "Administration" }, WorkspaceNavigation.Groups.Select(group => group.Label));
    }
}
