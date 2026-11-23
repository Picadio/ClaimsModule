using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClaimsModule.API.Security;
using ClaimsModule.Application.Security;
using ClaimsModule.IntegrationTests.Claims;
using ClaimsModule.Persistence.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace ClaimsModule.IntegrationTests.Security;

public sealed class SeededUserSqlTests
{
    [Fact]
    public void SeededUsersHaveDistinctValidPasswordHashes()
    {
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer("Server=localhost;Database=unused;Integrated Security=True").Options;
        using var db = new ClaimsDbContext(options);
        var entity = Assert.Single(db.GetService<IDesignTimeModel>().Model.GetEntityTypes(), item => item.GetTableName() == "Users");
        var users = entity.GetSeedData().ToArray();
        Assert.Equal(3, users.Length);
        Assert.Equal(3, users.Select(user => user["PasswordHash"]).Distinct().Count());
        var hasher = new PasswordHasher<string>();
        foreach (var user in users)
        {
            var name = Assert.IsType<string>(user["UserName"]);
            var hash = Assert.IsType<string>(user["PasswordHash"]);
            Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(name, hash, "1"));
            Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(name, hash, "wrong"));
        }
    }

    [SqlServerFact]
    public async Task MigrationSeedsThreeUsersWithWorkingPasswordsAndTenantIsolation()
    {
        var database = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(FnolApiTests.SqlServerVariable))
        {
            InitialCatalog = $"ClaimsUsers_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ClaimsDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            var count = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.Users").SingleAsync();
            Assert.Equal(3, count);
            var organization = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
            await using var api = new ClaimsApiFactory(true, claimsConnection: database.ConnectionString,
                organizationId: organization, useDatabaseUsers: true);
            using var client = api.CreateApiClient();
            foreach (var role in new[] { UserRoles.Handler, UserRoles.Supervisor, UserRoles.Manager })
            {
                using var login = await client.PostAsJsonAsync("/api/auth/demo-token", new { username = role, password = "1" });
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            }

            using var wrong = await client.PostAsJsonAsync("/api/auth/demo-token", new { username = "handler", password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            using var directoryLogin = await client.PostAsJsonAsync("/api/auth/demo-token", new { username = "supervisor", password = "1" });
            var token = await directoryLogin.Content.ReadFromJsonAsync<DemoTokenResponse>();
            Assert.NotNull(token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            var foreignOrganization = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.Users (OrganizationEntityId, UserName, NormalizedUserName, DisplayName, Role, PasswordHash, CreatedAt)
                SELECT {foreignOrganization}, UserName, NormalizedUserName, DisplayName, Role, PasswordHash, CreatedAt
                FROM dbo.Users WHERE Role = {UserRoles.Handler};
                UPDATE dbo.Users SET IsDeleted = 1 WHERE OrganizationEntityId = {organization} AND Role = {UserRoles.Manager};
                """);
            var users = await client.GetFromJsonAsync<UserReference[]>("/api/reference/users");
            Assert.NotNull(users);
            Assert.Equal(new[] { UserRoles.Handler, UserRoles.Supervisor }, users.Select(user => user.Role));

            await using var other = new ClaimsApiFactory(true, claimsConnection: database.ConnectionString, useDatabaseUsers: true);
            using var otherClient = other.CreateApiClient();
            using var hidden = await otherClient.PostAsJsonAsync("/api/auth/demo-token", new { username = "handler", password = "1" });
            Assert.Equal(HttpStatusCode.Unauthorized, hidden.StatusCode);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
