using ClaimsModule.Application.Claims.Documents;
using ClaimsModule.Domain.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Claims.Stores;

internal sealed class SqlClaimDocumentStore(ClaimsDbContext db) : IClaimDocumentStore
{
    public async Task<IReadOnlyList<ClaimDocument>?> ListAsync(Guid claimId, CancellationToken cancellationToken)
    {
        if (!await db.Set<Claim>().AnyAsync(claim => claim.Id == claimId, cancellationToken))
        {
            return null;
        }

        return await VisibleDocuments(claimId).OrderByDescending(document => document.CreatedAt)
            .ThenByDescending(document => document.Id).ToListAsync(cancellationToken);
    }

    public Task<ClaimDocument?> FindAsync(Guid claimId, Guid documentId, CancellationToken cancellationToken) =>
        VisibleDocuments(claimId).SingleOrDefaultAsync(document => document.Id == documentId, cancellationToken);

    public void Add(ClaimDocument document) => db.Add(document);

    private IQueryable<ClaimDocument> VisibleDocuments(Guid claimId) => db.Set<ClaimDocument>().AsNoTracking()
        .Where(document => document.ClaimId == claimId && db.Set<Claim>().Any(claim => claim.Id == document.ClaimId));
}

internal sealed class ClaimDocumentConfiguration : IEntityTypeConfiguration<ClaimDocument>
{
    public void Configure(EntityTypeBuilder<ClaimDocument> document)
    {
        document.ToTable("ClaimDocuments");
        document.Property(row => row.FileName).HasMaxLength(120);
        document.Property(row => row.StorageKey).HasMaxLength(255).IsUnicode(false);
        document.Property(row => row.StorageProvider).HasConversion<string>().HasMaxLength(30);
        document.Property(row => row.ContentType).HasMaxLength(120);
        document.Property(row => row.DocumentType).HasMaxLength(100);
        document.Property(row => row.Notes).HasMaxLength(2000);
        document.HasIndex(row => new { row.OrganizationEntityId, row.StorageKey }).IsUnique();
        document.HasIndex(row => new { row.OrganizationEntityId, row.ClaimId, row.CreatedAt });
        document.HasOne<Claim>().WithMany()
            .HasForeignKey(row => new { row.OrganizationEntityId, row.ClaimId })
            .HasPrincipalKey(row => new { row.OrganizationEntityId, row.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
