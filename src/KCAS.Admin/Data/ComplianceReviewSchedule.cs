namespace KCAS.Admin.Data;

public static class ComplianceReviewSchedule
{
    public const int ReminderDays = 30;

    public static DateOnly BraDueDate(BusinessRiskAssessment assessment)
        => DateOnly.FromDateTime(assessment.UpdatedAtUtc).AddYears(1);

    public static DateOnly RmcpDueDate(RmcpVersion version)
        => DateOnly.FromDateTime(version.UpdatedAtUtc).AddYears(1);

    public static string Status(DateOnly dueDate, DateOnly today)
    {
        if (dueDate < today) return "Overdue";
        if (dueDate <= today.AddDays(ReminderDays)) return "Due soon";
        return "Current";
    }

    public static string BadgeClass(string status) => status switch
    {
        "Overdue" => "badge text-bg-danger",
        "Due soon" => "badge text-bg-warning",
        _ => "badge text-bg-success"
    };
}
