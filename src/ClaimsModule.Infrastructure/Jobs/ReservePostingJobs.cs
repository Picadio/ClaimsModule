using ClaimsModule.Application.Reserves;
using Hangfire;
using Hangfire.SqlServer;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.Infrastructure.Jobs;

public static class ReservePostingJobs
{
    public static IServiceCollection AddReservePostingJobs(this IServiceCollection services, string connectionString,
        IReadOnlyList<Guid> organizations)
    {
        if (organizations.Count == 0 || organizations.Contains(Guid.Empty))
        {
            throw new InvalidOperationException("Configure at least one nonempty organization ID for GL posting recovery.");
        }

        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString));
        services.AddHangfireServer();
        services.AddSingleton(new ReservePostingRecoveryOptions(organizations.Distinct().ToArray()));
        services.AddSingleton<BackgroundCommandRunner>();
        services.AddScoped<IReservePostingQueue, HangfireReservePostingQueue>();
        services.AddTransient<INotificationHandler<ReservePostingReady>, DispatchAfterApproval>();
        services.AddHostedService<ReservePostingRecovery>();
        return services;
    }
}
