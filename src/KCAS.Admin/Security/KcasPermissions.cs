namespace KCAS.Admin.Security;

public static class KcasPermissions
{
    public const string SecurityManage = "Security.Manage";
    public const string ClientsView = "Clients.View";
    public const string ClientsManage = "Clients.Manage";
    public const string NotesManage = "Notes.Manage";
    public const string InvestmentsView = "Investments.View";
    public const string InvestmentsManage = "Investments.Manage";
    public const string KycView = "Kyc.View";
    public const string KycManage = "Kyc.Manage";
    public const string ReportsView = "Reports.View";
    public const string LegacyImportsView = "LegacyImports.View";
    public const string LegacyImportsReview = "LegacyImports.Review";
    public const string LegacyImportsApply = "LegacyImports.Apply";
    public const string LegacyImportsAdminister = "LegacyImports.Administer";

    public static readonly IReadOnlyList<string> All =
    [
        SecurityManage,
        ClientsView,
        ClientsManage,
        NotesManage,
        InvestmentsView,
        InvestmentsManage,
        KycView,
        KycManage,
        ReportsView,
        LegacyImportsView,
        LegacyImportsReview,
        LegacyImportsApply,
        LegacyImportsAdminister
    ];
}
