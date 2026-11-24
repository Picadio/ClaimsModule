using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Claims.Configurations;

internal sealed class ClaimModelConfiguration :
    IEntityTypeConfiguration<Claim>, IEntityTypeConfiguration<LossEvent>, IEntityTypeConfiguration<ClaimParty>,
    IEntityTypeConfiguration<ClaimRiskObject>, IEntityTypeConfiguration<ClaimWarning>, IEntityTypeConfiguration<Policy>,
    IEntityTypeConfiguration<CauseOfLoss>, IEntityTypeConfiguration<ClaimNumberCounter>,
    IEntityTypeConfiguration<ClaimSubmission>, IEntityTypeConfiguration<ClaimAuditEntry>,
    IEntityTypeConfiguration<ClaimStatusSubmission>, IEntityTypeConfiguration<StoredClaimTransition>
{
    public void Configure(EntityTypeBuilder<Claim> claim)
    {
        claim.ToTable("Claims");
        claim.Property(row => row.ClaimNumber).HasMaxLength(50);
        claim.Property(row => row.ClaimType).HasMaxLength(50);
        claim.Property(row => row.Status).HasConversion<string>().HasMaxLength(50);
        claim.Property(row => row.Severity).HasConversion<string>().HasMaxLength(30).HasDefaultValue(ClaimSeverity.Standard);
        claim.Property(row => row.Notes).HasMaxLength(4000);
        claim.Property(row => row.PolicyNumber).HasMaxLength(50);
        claim.Property(row => row.ClientName).HasMaxLength(255);
        claim.Property(row => row.ClosedAt).HasPrecision(7);
        claim.Property(row => row.LastSlaBreachAt).HasPrecision(7);
        claim.HasIndex(row => new { row.OrganizationEntityId, row.Status, row.Id });
        claim.Property(row => row.ClosureReason).HasMaxLength(2000);
        claim.Property(row => row.ReserveLimitOverrideEnabled).HasDefaultValue(false);
        claim.Property<byte[]>("RowVer").IsRequired().IsRowVersion();
        claim.Ignore(row => row.CreatedEvent);
        claim.HasIndex(row => new { row.OrganizationEntityId, row.ClaimNumber }).IsUnique();
        claim.HasAlternateKey(row => new { row.OrganizationEntityId, row.Id });

        claim.HasOne<Policy>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.PolicyId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
        claim.HasOne(row => row.LossEvent).WithOne()
            .HasForeignKey<LossEvent>(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey<Claim>(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
        claim.Navigation(row => row.LossEvent).IsRequired();
        claim.HasMany(row => row.Parties).WithOne()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
        claim.Navigation(row => row.Parties).HasField("parties").UsePropertyAccessMode(PropertyAccessMode.Field);
        claim.HasMany(row => row.RiskObjects).WithOne()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
        claim.Navigation(row => row.RiskObjects).HasField("riskObjects").UsePropertyAccessMode(PropertyAccessMode.Field);
        claim.HasMany(row => row.Warnings).WithOne()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
        claim.Navigation(row => row.Warnings).HasField("warnings").UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    public void Configure(EntityTypeBuilder<LossEvent> loss)
    {
        loss.ToTable("LossEvents");
        loss.Property(row => row.LossDate).HasPrecision(7);
        loss.Property(row => row.CauseOfLossCode).HasMaxLength(50);
        loss.Property(row => row.LossLocation).HasMaxLength(500);
        loss.Property(row => row.EstimatedLossAmount).HasPrecision(19, 4);
        loss.Property(row => row.PoliceReportNumber).HasMaxLength(100);
        loss.HasOne<CauseOfLoss>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, Code = row.CauseOfLossCode })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Code })
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ClaimParty> party)
    {
        party.ToTable("ClaimParties");
        party.Property(row => row.Role).HasConversion<string>().HasMaxLength(50);
        party.Property(row => row.PartyType).HasConversion<string>().HasMaxLength(20);
        party.Property(row => row.FirstName).HasMaxLength(100);
        party.Property(row => row.LastName).HasMaxLength(100);
        party.Property(row => row.CompanyName).HasMaxLength(255);
        party.Property(row => row.Email).HasMaxLength(255);
        party.Property(row => row.Phone).HasMaxLength(50);
    }

    public void Configure(EntityTypeBuilder<ClaimRiskObject> risk)
    {
        risk.ToTable("ClaimRiskObjects");
        risk.Property(row => row.AssetType).HasConversion<string>().HasMaxLength(50);
        risk.Property(row => row.AssetDescription).HasMaxLength(500);
        risk.Property(row => row.AssetReference).HasMaxLength(255);
    }

    public void Configure(EntityTypeBuilder<ClaimWarning> warning)
    {
        warning.ToTable("ClaimWarnings");
        warning.Property(row => row.Code).HasMaxLength(50);
        warning.Property(row => row.Field).HasMaxLength(100);
    }

    public void Configure(EntityTypeBuilder<Policy> policy)
    {
        policy.ToTable("Policies");
        policy.Property(row => row.PolicyNumber).HasMaxLength(50);
        policy.Property(row => row.ClientName).HasMaxLength(255);
        policy.Property(row => row.Status).HasMaxLength(50);
        policy.HasIndex(row => new { row.OrganizationEntityId, row.PolicyNumber }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<CauseOfLoss> cause)
    {
        cause.ToTable("CauseOfLossCodes");
        cause.Property(row => row.Code).HasMaxLength(50);
        cause.Property(row => row.Name).HasMaxLength(255);
        cause.Property(row => row.PerilCategory).HasMaxLength(50);
    }

    public void Configure(EntityTypeBuilder<ClaimNumberCounter> counter)
    {
        counter.ToTable("ClaimNumberCounters");
        counter.HasIndex(row => new { row.OrganizationEntityId, row.Year }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<ClaimSubmission> submission)
    {
        submission.ToTable("ClaimSubmissions");
        submission.Property(row => row.KeyHash).HasMaxLength(64).IsUnicode(false);
        submission.Property(row => row.RequestHash).HasMaxLength(64).IsUnicode(false);
        submission.Property(row => row.CreatedByUserId).IsRequired();
        submission.HasIndex(row => new { row.OrganizationEntityId, row.CreatedByUserId, row.KeyHash }).IsUnique();
        submission.HasOne<Claim>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ClaimAuditEntry> audit)
    {
        audit.ToTable("ClaimAuditLog", table => table.UseSqlOutputClause(false));
        audit.Property(row => row.EventType).HasMaxLength(100);
        audit.Property(row => row.RelatedEntityType).HasMaxLength(100);
        audit.HasIndex(row => new { row.OrganizationEntityId, row.ClaimId, row.CreatedAt, row.Position, row.Id })
            .IsDescending(false, false, true, true, true);
        audit.HasOne<Claim>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ClaimStatusSubmission> submission)
    {
        submission.ToTable("ClaimStatusSubmissions");
        submission.Property(row => row.KeyHash).HasMaxLength(64).IsUnicode(false);
        submission.Property(row => row.RequestHash).HasMaxLength(64).IsUnicode(false);
        submission.Property(row => row.CreatedByUserId).IsRequired();
        submission.HasIndex(row => new { row.OrganizationEntityId, row.ClaimId, row.CreatedByUserId, row.KeyHash })
            .IsUnique();
        submission.HasOne<Claim>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<StoredClaimTransition> transition)
    {
        transition.ToTable("ClaimStatusTransitions");
        transition.HasIndex(row => new { row.OrganizationEntityId, row.FromStatus, row.ToStatus }).IsUnique()
            .HasFilter("[IsDeleted] = 0");
        transition.Property(row => row.FromStatus).HasConversion<string>().HasMaxLength(50);
        transition.Property(row => row.ToStatus).HasConversion<string>().HasMaxLength(50);
        transition.Property(row => row.RequiredPermission).HasMaxLength(50);
    }
}
