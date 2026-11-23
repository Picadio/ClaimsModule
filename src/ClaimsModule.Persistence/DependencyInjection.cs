using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Claims.Documents;
using ClaimsModule.Application.Reference;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Application.Security;
using ClaimsModule.Persistence.Claims;
using ClaimsModule.Persistence.Claims.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddClaimsPersistence(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<ClaimsDbContext>(options => options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(15),
                errorNumbersToAdd: null
            );
        }));
        services.AddScoped<IClaimStore, SqlClaimStore>();
        services.AddScoped<IUserStore, SqlUserStore>();
        services.AddScoped<IClaimHandlerDirectory, SqlUserStore>();
        services.AddScoped<IClaimMutationStore, SqlClaimMutationStore>();
        services.AddScoped<IClaimDocumentStore, SqlClaimDocumentStore>();
        services.AddScoped<IClaimLifecycleStore, SqlClaimLifecycleStore>();
        services.AddScoped<IReserveStore, SqlReserveStore>();
        services.AddScoped<IReferenceDataStore, SqlReferenceDataStore>();
        services.AddScoped<IClaimUnitOfWork, SqlClaimUnitOfWork>();
        services.AddScoped<IAuditLogService, SqlClaimAuditLog>();
        services.AddScoped<IClaimAuditReader, SqlClaimAuditLog>();
        return services;
    }
}
