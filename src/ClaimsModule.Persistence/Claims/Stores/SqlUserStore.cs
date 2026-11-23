using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Claims.Stores;

internal sealed class ApplicationUser : OrganizationRecord
{
    public string UserName { get; private set; } = null!;
    public string NormalizedUserName { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string Role { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> user)
    {
        user.ToTable("Users");
        user.Property(row => row.UserName).HasMaxLength(64).IsRequired();
        user.Property(row => row.NormalizedUserName).HasMaxLength(64).IsRequired();
        user.Property(row => row.DisplayName).HasMaxLength(255).IsRequired();
        user.Property(row => row.Role).HasMaxLength(32).IsRequired();
        user.Property(row => row.PasswordHash).HasMaxLength(512).IsRequired();
        user.HasIndex(row => new { row.OrganizationEntityId, row.NormalizedUserName }).IsUnique();
    }
}

internal sealed class SqlUserStore(ClaimsDbContext db) : IUserStore, IClaimHandlerDirectory
{
    public Task<StoredUser?> FindForLoginAsync(Guid organizationId, string normalizedUserName, CancellationToken cancellationToken) =>
        db.Set<ApplicationUser>().IgnoreQueryFilters().AsNoTracking()
            .Where(user => user.OrganizationEntityId == organizationId && !user.IsDeleted && user.NormalizedUserName == normalizedUserName)
            .Select(user => new StoredUser(user.Id, user.OrganizationEntityId, user.UserName, user.Role, user.PasswordHash))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<UserReference>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Set<ApplicationUser>().AsNoTracking()
            .Where(user => user.OrganizationEntityId == organizationId)
            .OrderBy(user => user.DisplayName).ThenBy(user => user.Id)
            .Select(user => new UserReference(user.Id, user.DisplayName, user.Role))
            .ToListAsync(cancellationToken);

    public Task<bool> IsHandlerAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.Set<ApplicationUser>().AnyAsync(user => user.OrganizationEntityId == organizationId && user.Id == userId &&
            user.Role == UserRoles.Handler, cancellationToken);

    public async Task<IReadOnlyList<Guid>> FindByNameAsync(Guid organizationId, string name, CancellationToken cancellationToken) =>
        await db.Set<ApplicationUser>().Where(user => user.OrganizationEntityId == organizationId &&
                user.Role == UserRoles.Handler && user.DisplayName.ToUpper().Contains(name.ToUpper()))
            .Select(user => user.Id).ToListAsync(cancellationToken);
}
