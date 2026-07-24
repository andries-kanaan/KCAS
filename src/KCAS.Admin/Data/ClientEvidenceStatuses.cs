namespace KCAS.Admin.Data;

public static class ClientEvidenceStatuses
{
    public const string Linked = "Linked";
    public const string Verified = "Verified";
    public const string Expired = "Expired";
    public const string Replaced = "Replaced";
    public const string Rejected = "Rejected";
}

public static class ClientEvidenceRequirementStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
}

public static class ClientEvidenceScanStatuses
{
    public const string Running = "Running";
    public const string Cancelling = "Cancelling";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public static class ClientEvidenceScanFileStatuses
{
    public const string Linked = "Linked";
    public const string Unmatched = "Unmatched";
    public const string Ambiguous = "Ambiguous";
    public const string Skipped = "Skipped";
}

public static class ClientEvidenceSelectionStatuses
{
    public const string Candidate = "Candidate";
    public const string Current = "Current";
    public const string Historical = "Historical";
    public const string Rejected = "Rejected";
}
