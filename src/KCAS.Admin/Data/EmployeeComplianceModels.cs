namespace KCAS.Admin.Data;

public sealed class EmployeeProfileEdit
{
    public int? Id { get; set; }
    public string? Version { get; set; }
    public string DisplayName { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string Aliases { get; set; } = "";
    public string? Email { get; set; }
    public string EmploymentStatus { get; set; } = "Current";
    public string IdentityReference { get; set; } = "";
    public string Responsibilities { get; set; } = "";
    public string AuthorityLimits { get; set; } = "";
    public string SourceReference { get; set; } = "";
    public string RoleExposure { get; set; } = "Standard";
    public string RiskRationale { get; set; } = "";
    public string SelectedChecks { get; set; } = "";
    public bool RequireTraining { get; set; } = true;
    public bool RequireRegulatedCompetence { get; set; }
    public bool RequireAdditionalCheck { get; set; }
    public int ProposedReviewMonths { get; set; } = 12;
    public DateOnly? EmploymentStart { get; set; }
    public DateOnly? EmploymentEnd { get; set; }
    public string ExternalAccessScope { get; set; } = "";
}

public sealed class EmployeeCheckEdit
{
    public string Kind { get; set; } = "Competence";
    public string Outcome { get; set; } = "Satisfied";
    public string PerformerType { get; set; } = "Manual";
    public DateTime PerformedAtLocal { get; set; } = DateTime.Now;
    public string SourceReference { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string? ListVersion { get; set; }
    public string IdentifierScope { get; set; } = "";
    public string Finding { get; set; } = "";
    public string Limitations { get; set; } = "";
    public string? EvidencePath { get; set; }
    public int? SupersedesCheckId { get; set; }
    public int? EmployeeTfsBatchId { get; set; }
}

public sealed class EmployeeAccessEdit
{
    public string Kind { get; set; } = "KCAS";
    public string SystemAndScope { get; set; } = "";
    public string ApprovedScope { get; set; } = "";
    public string ActualScope { get; set; } = "";
    public string ActionConfirmation { get; set; } = "";
    public bool IsAligned { get; set; }
    public DateTime VerifiedAtLocal { get; set; } = DateTime.Now;
    public string EvidenceReference { get; set; } = "";
}

public sealed record EmployeeAccountOption(string Id, string Label);
public sealed record EmployeePermissionState(bool CanManage, bool CanReview, bool CanLinkAccounts);
public sealed record EmployeeRegisterRow(EmployeeProfile Profile, EmployeeComplianceReview? LatestReview, int OpenTasks);
public sealed record EmployeeTaskRow(EmployeeComplianceTask Task, string EmployeeName, string RecipientNames = "", string? AcknowledgedByName = null);
public sealed record EmployeeBatchRow(EmployeeTfsBatch Batch, int Total, int Checked, int Remaining, int FollowUp);
public sealed record EmployeeReviewPage(
    EmployeeProfile Profile,
    EmployeeComplianceReview? CurrentReview,
    IReadOnlyList<EmployeeComplianceReview> History,
    IReadOnlyList<EmployeeComplianceCheck> Checks,
    IReadOnlyList<EmployeeAccessConfirmation> Access,
    IReadOnlyList<EmployeeReviewDecision> Decisions,
    IReadOnlyList<EmployeeAccountOption> Accounts,
    string CurrentAccountScope,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<EmployeeComplianceTask> Tasks,
    IReadOnlyList<EmployeeComplianceAuditEvent> Audit,
    IReadOnlyDictionary<string, string> UserNames)
{
    public IReadOnlyList<SanctionsAutomatedResult> AutomatedSanctions { get; init; } = [];
}

public sealed class EmployeeBaseline
{
    public string SourceReference { get; set; } = "";
    public List<EmployeeBaselineEntry> Employees { get; set; } = [];
}

public sealed class EmployeeBaselineEntry
{
    public EmployeeProfileEdit Profile { get; set; } = new();
    public List<string> AccountEmails { get; set; } = [];
    public List<EmployeeEvidenceEdit> Evidence { get; set; } = [];
}

public sealed class EmployeeEvidenceEdit
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Competence";
    public string EvidencePath { get; set; } = "";
    public string SourceNote { get; set; } = "";
}
