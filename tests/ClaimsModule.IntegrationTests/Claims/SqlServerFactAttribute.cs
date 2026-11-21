using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        const string variable = FnolApiTests.SqlServerVariable;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
        {
            Skip = $"Set {variable} to a SQL Server connection with test database creation rights.";
        }
    }
}
