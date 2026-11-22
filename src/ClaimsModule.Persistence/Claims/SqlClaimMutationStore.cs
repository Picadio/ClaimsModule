using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Claims;

internal sealed class SqlClaimMutationStore(ClaimsDbContext db) : IClaimMutationStore
{
    public Task<StoredClaimMutation?> FindAsync(Guid claimId, Guid actorId, string keyHash, CancellationToken cancellationToken) =>
        db.Set<ClaimMutationSubmission>().Where(row => row.ClaimId == claimId &&
                row.CreatedByUserId == actorId && row.KeyHash == keyHash)
            .Select(row => new StoredClaimMutation(claimId, actorId, row.CreatedAt, row.KeyHash, row.RequestHash, row.ResponseJson))
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(StoredClaimMutation mutation) => db.Add(new ClaimMutationSubmission(db.OrganizationId, mutation));
}

internal sealed class ClaimMutationSubmission : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public string KeyHash { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;

    private ClaimMutationSubmission()
    {
    }

    public ClaimMutationSubmission(Guid organizationId, StoredClaimMutation mutation)
        : base(organizationId, mutation.ActorId, mutation.OccurredAt)
    {
        ClaimId = mutation.ClaimId;
        KeyHash = mutation.KeyHash;
        RequestHash = mutation.RequestHash;
        ResponseJson = mutation.ResponseJson;
    }
}

internal sealed class ClaimMutationConfiguration : IEntityTypeConfiguration<ClaimMutationSubmission>
{
    public void Configure(EntityTypeBuilder<ClaimMutationSubmission> submission)
    {
        submission.ToTable("ClaimMutationSubmissions");
        submission.Property(row => row.CreatedByUserId).IsRequired();
        submission.Property(row => row.KeyHash).HasMaxLength(64).IsUnicode(false);
        submission.Property(row => row.RequestHash).HasMaxLength(64).IsUnicode(false);
        submission.HasIndex(row => new { row.OrganizationEntityId, row.ClaimId, row.CreatedByUserId, row.KeyHash }).IsUnique();
        submission.HasOne<Claim>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
