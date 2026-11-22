namespace ClaimsModule.Domain.Claims;

public static class FnolRules
{
    public const int MinLossDescriptionLength = 20;
    public const int MaxClaimSequence = 9_999_999;

    public static bool IsOutsidePolicyPeriod(DateTimeOffset lossDate, LinkedPolicy policy)
    {
        var date = DateOnly.FromDateTime(lossDate.UtcDateTime);
        return date < policy.EffectiveDate || date > policy.ExpirationDate;
    }

    public static string FormatClaimNumber(DateTimeOffset reportedAt, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxClaimSequence);
        return FormattableString.Invariant($"CLM-{reportedAt.UtcDateTime.Year:D4}-{sequence:D7}");
    }
}
