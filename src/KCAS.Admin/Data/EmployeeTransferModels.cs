namespace KCAS.Admin.Data;

public sealed class EmployeeTransferPackage
{
    public int FormatVersion { get; set; } = 1;
    public string PackageId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string ExportedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public string SourceSystemKey { get; set; } = "";
    public List<EmployeeTransferSnapshot> Employees { get; set; } = [];
    public Dictionary<int, EmployeeTransferIdentity> People { get; set; } = [];
    public Dictionary<string, string> Actors { get; set; } = [];
    public List<EmployeeTransferFile> Files { get; set; } = [];
}

public sealed record EmployeeTransferIdentity(string Key, string LegalName, string? Email);
public sealed record EmployeeTransferFile(string Reference, string Sha256, byte[] Content);
public sealed record EmployeeTransferExport(string PackageId, string FileName, int Employees, long Bytes);
public sealed record EmployeeTransferRow(string Employee, string Action, int Reviews, int Checks, int Documents);
public sealed class EmployeeTransferPreview
{
    public required EmployeeTransferPackage Package { get; init; }
    public List<EmployeeTransferRow> Rows { get; init; } = [];
    public List<string> Conflicts { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public string TargetStamp { get; set; } = "";
    public bool CanApply => Conflicts.Count == 0;
}

public sealed class EmployeeTransferSnapshot
{
    public EmployeeProfile Profile { get; set; } = new();
    public List<EmployeeAccountLink> Accounts { get; set; } = [];
    public List<EmployeeComplianceReview> Reviews { get; set; } = [];
    public List<EmployeeComplianceCheck> Checks { get; set; } = [];
    public List<EmployeeAccessConfirmation> Access { get; set; } = [];
    public List<EmployeeReviewDecision> Decisions { get; set; } = [];
    public List<EmployeeEvidenceDocument> Documents { get; set; } = [];
    public List<EmployeeComplianceTask> Tasks { get; set; } = [];
    public List<EmployeeTfsBatch> Batches { get; set; } = [];
    public List<EmployeeComplianceAuditEvent> Audit { get; set; } = [];
}

public sealed class EmployeeTransferMapping
{
    public Dictionary<string, string> ImmutableDigests { get; set; } = [];
    public Dictionary<int, int> Reviews { get; set; } = [];
    public Dictionary<int, int> Checks { get; set; } = [];
    public Dictionary<int, int> Access { get; set; } = [];
    public Dictionary<int, int> Decisions { get; set; } = [];
    public Dictionary<int, int> Documents { get; set; } = [];
    public Dictionary<int, int> Tasks { get; set; } = [];
    public Dictionary<int, int> Audit { get; set; } = [];
}
