using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Claims;

internal sealed class ReserveModelConfiguration : IEntityTypeConfiguration<ClaimReserveComponent>,
    IEntityTypeConfiguration<ReserveHistory>, IEntityTypeConfiguration<ReserveSubmission>
{
    public void Configure(EntityTypeBuilder<ClaimReserveComponent> component)
    {
        component.ToTable("ClaimReserveComponents");
        component.Property(row => row.ComponentType).HasConversion<string>().HasMaxLength(50);
        component.Property(row => row.CurrentAmount).HasPrecision(19, 4);
        component.Property<byte[]>("RowVer").IsRequired().IsRowVersion();
        component.HasIndex(row => new { row.OrganizationEntityId, row.ClaimId, row.ComponentType }).IsUnique();
        component.HasAlternateKey(row => new { row.OrganizationEntityId, row.ClaimId, row.Id });
        HasClaim(component, row => new { row.OrganizationEntityId, row.ClaimId });
    }

    public void Configure(EntityTypeBuilder<ReserveHistory> history)
    {
        history.ToTable("ReserveHistory");
        history.Property(row => row.TransactionType).HasConversion<string>().HasMaxLength(50);
        history.Property(row => row.ApprovalStatus).HasConversion<string>().HasMaxLength(50);
        history.Property(row => row.RequiredAuthority).HasConversion<string>().HasMaxLength(50);
        history.Property(row => row.PostingStatus).HasConversion<string>().HasMaxLength(50);
        history.Property(row => row.Amount).HasPrecision(19, 4);
        history.Property(row => row.PreviousBalance).HasPrecision(19, 4);
        history.Property(row => row.NewBalance).HasPrecision(19, 4);
        history.Property(row => row.ChangeReason).HasMaxLength(ClaimReserveComponent.ChangeReasonMaximumLength);
        history.Property(row => row.PostingKey).HasMaxLength(80).IsUnicode(false);
        history.Property(row => row.PostingJobId).HasMaxLength(100);
        history.Property(row => row.ApprovedAt).HasPrecision(7);
        history.Property(row => row.RejectedAt).HasPrecision(7);
        history.HasIndex(row => new { row.OrganizationEntityId, row.ClaimId, row.ReserveId, row.ChangeSequence }).IsUnique();
        history.HasIndex(row => new { row.OrganizationEntityId, row.PostingKey }).IsUnique();
        history.HasOne<ClaimReserveComponent>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId, row.ReserveId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.ClaimId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
        history.HasOne<ReserveHistory>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId, row.ReserveId, row.ReplacesRejectedTransactionId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.ClaimId, row.ReserveId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ReserveSubmission> submission)
    {
        submission.ToTable("ReserveSubmissions");
        submission.Property(row => row.CreatedByUserId).IsRequired();
        submission.Property(row => row.KeyHash).HasMaxLength(64).IsUnicode(false);
        submission.Property(row => row.RequestHash).HasMaxLength(64).IsUnicode(false);
        submission.HasIndex(row => new { row.OrganizationEntityId, row.ClaimId, row.CreatedByUserId, row.KeyHash }).IsUnique();
        HasClaim(submission, row => new { row.OrganizationEntityId, row.ClaimId });
    }

    private static void HasClaim<T>(EntityTypeBuilder<T> entity,
        System.Linq.Expressions.Expression<Func<T, object?>> foreignKey) where T : OrganizationRecord =>
        entity.HasOne<Claim>().WithMany().HasForeignKey(foreignKey)
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
}
