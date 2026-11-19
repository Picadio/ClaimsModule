using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddClaimsApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssemblyContaining<CreateClaimCommand>();
            options.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssemblyContaining<CreateClaimValidator>(includeInternalTypes: true);
        services.AddAutoMapper(_ => { }, typeof(ClaimMappingProfile));
        return services;
    }
}
