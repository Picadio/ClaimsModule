using ClaimsModule.Application.Security;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.Infrastructure.Jobs;

internal sealed class BackgroundCommandRunner(IServiceScopeFactory scopes)
{
    public async Task<T> SendAsync<T>(Guid organizationId, IRequest<T> request, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Initialize(organizationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);
    }
}
