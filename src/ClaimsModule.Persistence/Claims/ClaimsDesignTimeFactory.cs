using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimsModule.Persistence.Claims;

public sealed class ClaimsDesignTimeFactory : IDesignTimeDbContextFactory<ClaimsDbContext>
{
    public ClaimsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Claims");
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(connectionString).Options;
        return new ClaimsDbContext(options);
    }
}
