using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

internal static class ReferenceSeeds
{
    private static readonly Guid DemoOrganizationId = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
    private static readonly DateTimeOffset SeededAt = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Policy>().HasData(
            Policy(1, "POL-2024-001001", "Meridian Transport LLC", new DateOnly(2024, 1, 1),
                new DateOnly(2026, 12, 31), "Active", "Vehicle,Cargo"),
            Policy(2, "POL-2024-001002", "Harborview Properties Inc", new DateOnly(2024, 6, 1),
                new DateOnly(2026, 5, 31), "Expired", "Property,Liability"),
            Policy(3, "POL-2025-002001", "Coastal Builders Group", new DateOnly(2025, 3, 1),
                new DateOnly(2027, 2, 28), "Active", "Property,Equipment"),
            Policy(4, "POL-2025-002002", "Stanton Medical Group", new DateOnly(2025, 1, 1),
                new DateOnly(2026, 12, 31), "Active", "Liability,Vehicle"),
            Policy(5, "POL-2023-000099", "Archived Corp", new DateOnly(2020, 1, 1),
                new DateOnly(2021, 12, 31), "Expired", "Property"));

        modelBuilder.Entity<CauseOfLoss>().HasData(
            Cause(1, "COL-FIRE", "Fire", "Property"),
            Cause(2, "COL-FLOOD", "Flood", "Weather"),
            Cause(3, "COL-THEFT", "Theft", "Crime"),
            Cause(4, "COL-VEH-COL", "Vehicle Collision", "Auto"),
            Cause(5, "COL-VEH-COMP", "Vehicle Comprehensive", "Auto"),
            Cause(6, "COL-LIAB", "Third Party Liability", "Liability"),
            Cause(7, "COL-EQUIP", "Equipment Breakdown", "Equipment"),
            Cause(8, "COL-WIND", "Wind / Storm", "Weather"),
            Cause(9, "COL-INJURY", "Bodily Injury", "Liability"),
            Cause(10, "COL-OTHER", "Other / Unknown", "General"));

        modelBuilder.Entity<ClaimTransitionRule>().HasData(new ClaimTransitionRule[]
        {
            new(ClaimStatus.Draft, ClaimStatus.Open, UserRoles.Handler),
            new(ClaimStatus.Open, ClaimStatus.UnderInvestigation, UserRoles.Handler),
            new(ClaimStatus.Open, ClaimStatus.PendingPayment, UserRoles.Handler),
            new(ClaimStatus.Open, ClaimStatus.Closed, UserRoles.Handler),
            new(ClaimStatus.Open, ClaimStatus.Withdrawn, UserRoles.Handler),
            new(ClaimStatus.UnderInvestigation, ClaimStatus.Open, UserRoles.Handler),
            new(ClaimStatus.UnderInvestigation, ClaimStatus.PendingPayment, UserRoles.Handler),
            new(ClaimStatus.UnderInvestigation, ClaimStatus.Closed, UserRoles.Handler),
            new(ClaimStatus.UnderInvestigation, ClaimStatus.Withdrawn, UserRoles.Handler),
            new(ClaimStatus.PendingPayment, ClaimStatus.Closed, UserRoles.Handler),
            new(ClaimStatus.Closed, ClaimStatus.Reopened, UserRoles.Supervisor),
            new(ClaimStatus.Reopened, ClaimStatus.Open, ClaimTransitionRule.SystemOnly)
        });
    }

    private static object Policy(int source, string number, string client, DateOnly effective, DateOnly expiration,
        string status, string coverages) => new
    {
        Id = Guid.Parse(FormattableString.Invariant($"1a46cfff-f909-44dc-ad15-{source:D12}")),
        OrganizationEntityId = DemoOrganizationId,
        CreatedAt = SeededAt,
        IsDeleted = false,
        PolicyNumber = number,
        ClientName = client,
        EffectiveDate = effective,
        ExpirationDate = expiration,
        Status = status,
        CoverageTypes = coverages
    };

    private static object Cause(int source, string code, string name, string category) => new
    {
        Id = Guid.Parse(FormattableString.Invariant($"235ba2a1-dbc7-456a-8da0-{source:D12}")),
        OrganizationEntityId = DemoOrganizationId,
        CreatedAt = SeededAt,
        IsDeleted = false,
        Code = code,
        Name = name,
        PerilCategory = category,
        IsActive = true
    };
}
