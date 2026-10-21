namespace KCAS.Admin.Data;

public sealed record FeeReviewSummaryLine(string Account, int? AccountId, int? ProfileId, int? ClaimId,
    string Reference, DateOnly? From, DateOnly? To, string Status, decimal? FeeExVat, decimal? Vat,
    decimal? Total, bool ReadyForApproval, bool Approved, int IssueCount)
{
    public static FeeReviewSummaryLine Create(FeeClientAccount account, PerformanceFeeClaim? claim)
    {
        var result = claim is null ? null : FeeJson.Read<FeeCalculationResult>(claim.ResultJson);
        var stale = claim?.Status == FeeClaimStages.Draft && account.RequiresStatementRebuild;
        var ready = !stale && account.Exclusion is null && claim?.Status == FeeClaimStages.Draft && result!.IsReady;
        var approved = claim?.ApprovedAtUtc is not null && result!.IsReady &&
            (claim.Status == FeeClaimStages.NoFee || FeeClaimStages.Exportable(claim.Status));
        var amounts = !stale && result?.CanShowCalculation == true;
        var vat = amounts && (result!.IsReady || result.HasVatCalculation);
        var status = account.Exclusion is not null ? "Excluded" : stale ? "Refresh required" : claim is null ? "Not prepared" :
            approved ? claim.Status == FeeClaimStages.NoFee ? "Approved - no fee" : claim.Status :
            ready ? result!.Total > 0 ? "Ready for KI approval" : "Ready - no fee" :
            claim.Status != FeeClaimStages.Draft ? claim.Status : $"Review required ({result!.Issues.Count})";
        return new(account.Number, account.Account?.Id, account.Profile?.Id, claim?.Id, claim?.Reference ?? "",
            claim?.From, claim?.To, status, amounts ? result!.FeeExVat : null,
            vat ? result!.Vat : null, vat ? result!.Total : null, ready, approved, result?.Issues.Count ?? 0);
    }
}
