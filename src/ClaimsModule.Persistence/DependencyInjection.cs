using ClaimsModule.Application.Claims;
using ClaimsModule.Persistence.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddClaimsPersistence(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<ClaimsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IClaimStore, SqlClaimStore>();
        services.AddScoped<IClaimUnitOfWork, SqlClaimUnitOfWork>();
        services.AddScoped<IClaimAuditLog, SqlClaimAuditLog>();
        return services;
    }
}
