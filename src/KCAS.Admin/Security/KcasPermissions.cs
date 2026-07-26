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
    public const string ComplianceView = "Compliance.View";
    public const string ComplianceManage = "Compliance.Manage";
    public const string ComplianceApprove = "Compliance.Approve";
    public const string ComplianceAudit = "Compliance.Audit";
    public const string RiskAssessmentsView = "RiskAssessments.View";
    public const string RiskAssessmentsPrepare = "RiskAssessments.Prepare";
    public const string RiskAssessmentsFinalise = "RiskAssessments.Finalise";
    public const string RiskAssessmentsApprove = "RiskAssessments.Approve";
    public const string InspectionsView = "Inspections.View";
    public const string InspectionsManage = "Inspections.Manage";
    public const string InspectionsExport = "Inspections.Export";

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
        LegacyImportsAdminister,
        ComplianceView,
        ComplianceManage,
        ComplianceApprove,
        ComplianceAudit,
        RiskAssessmentsView,
        RiskAssessmentsPrepare,
        RiskAssessmentsFinalise,
        RiskAssessmentsApprove,
        InspectionsView,
        InspectionsManage,
        InspectionsExport
    ];
}
