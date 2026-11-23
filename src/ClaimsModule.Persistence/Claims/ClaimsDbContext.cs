using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Persistence.Claims.Stores;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options, ICurrentUser? currentUser = null,
    BackgroundTenant? backgroundTenant = null)
    : DbContext(options)
{
    internal Guid OrganizationId => backgroundTenant?.OrganizationId ?? currentUser?.Identity.OrganizationEntityId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureRecord<Claim>(modelBuilder);
        ConfigureRecord<LossEvent>(modelBuilder);
        ConfigureRecord<ClaimParty>(modelBuilder);
        ConfigureRecord<ClaimDocument>(modelBuilder);
        ConfigureRecord<ClaimRiskObject>(modelBuilder);
        ConfigureRecord<ClaimWarning>(modelBuilder);
        ConfigureRecord<Policy>(modelBuilder);
        ConfigureRecord<CauseOfLoss>(modelBuilder);
        ConfigureRecord<ClaimNumberCounter>(modelBuilder);
        ConfigureRecord<ClaimSubmission>(modelBuilder);
        ConfigureRecord<ClaimMutationSubmission>(modelBuilder);
        ConfigureRecord<ClaimAuditEntry>(modelBuilder);
        ConfigureRecord<ClaimStatusSubmission>(modelBuilder);
        ConfigureRecord<ClaimReserveComponent>(modelBuilder);
        ConfigureRecord<ReserveHistory>(modelBuilder);
        ConfigureRecord<ReserveSubmission>(modelBuilder);
        ConfigureRecord<ApplicationUser>(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClaimsDbContext).Assembly);
        ReferenceSeeds.Apply(modelBuilder);
    }

    private void ConfigureRecord<T>(ModelBuilder modelBuilder) where T : OrganizationRecord
    {
        var entity = modelBuilder.Entity<T>();
        entity.HasKey(record => record.Id);
        entity.Property(record => record.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        entity.Property(record => record.CreatedAt).HasPrecision(7);
        entity.Property(record => record.UpdatedAt).HasPrecision(7);
        entity.Property(record => record.DeletedAt).HasPrecision(7);
        entity.Property(record => record.IsDeleted).HasDefaultValue(false);
        entity.HasQueryFilter(record => !record.IsDeleted && record.OrganizationEntityId == OrganizationId);
    }
}
