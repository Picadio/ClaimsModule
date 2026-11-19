using ClaimsModule.Application.Reference;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

internal sealed class SqlReferenceDataStore(ClaimsDbContext db) : IReferenceDataStore
{
    private const int PolicySearchLimit = 50;

    public async Task<IReadOnlyList<PolicySearchResult>> SearchPoliciesAsync(string? search,
        CancellationToken cancellationToken)
    {
        var policies = db.Set<Policy>().AsNoTracking();
        if (search?.Trim() is { Length: > 0 } term)
        {
            policies = policies.Where(row => row.PolicyNumber.Contains(term) || row.ClientName.Contains(term));
        }

        var rows = await policies.OrderBy(row => row.PolicyNumber)
            .Take(PolicySearchLimit)
            .Select(row => new
            {
                row.Id, row.PolicyNumber, row.ClientName, row.EffectiveDate, row.ExpirationDate,
                row.Status, row.CoverageTypes
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new PolicySearchResult(row.Id, row.PolicyNumber, row.ClientName,
            row.EffectiveDate, row.ExpirationDate, row.Status, ParseCoverageTypes(row.CoverageTypes))).ToArray();
    }

    public async Task<PolicyCoverage?> FindPolicyCoverageAsync(Guid policyId, CancellationToken cancellationToken)
    {
        var coverageTypes = await db.Set<Policy>().AsNoTracking()
            .Where(row => row.Id == policyId)
            .Select(row => row.CoverageTypes)
            .SingleOrDefaultAsync(cancellationToken);
        return coverageTypes is null ? null : new PolicyCoverage(policyId, ParseCoverageTypes(coverageTypes));
    }

    public async Task<IReadOnlyList<CauseOfLossCode>> ListCauseOfLossCodesAsync(string? perilCategory,
        CancellationToken cancellationToken)
    {
        var causes = db.Set<CauseOfLoss>().AsNoTracking().Where(row => row.IsActive);
        if (perilCategory?.Trim() is { Length: > 0 } category)
        {
            causes = causes.Where(row => row.PerilCategory == category);
        }

        return await causes.OrderBy(row => row.Code)
            .Select(row => new CauseOfLossCode(row.Code, row.Name, row.PerilCategory))
            .ToListAsync(cancellationToken);
    }

    private static string[] ParseCoverageTypes(string values) =>
        values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
