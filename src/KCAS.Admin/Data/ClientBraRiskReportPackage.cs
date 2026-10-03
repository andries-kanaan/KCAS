namespace KCAS.Admin.Data;

public sealed class ClientBraRiskReportPackage
{
    public string MethodVersion { get; set; } = "";
    public string BraReference { get; set; } = "";
    public string SourceContentHash { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public ClientBraRiskContent Content { get; set; } = new();
    public static ClientBraRiskReportPackage FromReport(ClientBraRiskReport report) => new()
    {
        MethodVersion = report.MethodVersion, BraReference = report.BraReference,
        SourceContentHash = report.SourceContentHash, PerformedBy = report.PerformedBy,
        RecordedBy = report.RecordedBy, RecordedAtUtc = report.RecordedAtUtc, Content = report.ReadContent()
    };
}
