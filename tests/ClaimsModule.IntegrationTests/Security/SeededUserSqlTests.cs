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
using Xunit;

namespace ClaimsModule.IntegrationTests.Security;

public sealed class SeededUserSqlTests
{
    [Fact]
    public void CurrentModelDoesNotSeedLoginAccounts()
    {
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer("Server=localhost;Database=unused;Integrated Security=True").Options;
        using var db = new ClaimsDbContext(options);
        var entity = Assert.Single(db.GetService<IDesignTimeModel>().Model.GetEntityTypes(), item => item.GetTableName() == "Users");
        Assert.Empty(entity.GetSeedData());
    }

    [SqlServerFact]
    public async Task MigrationRetiresSeedCredentialsAndRegistrationHandlesConcurrentAttempts()
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
            var organization = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
            var retiredManager = Guid.Parse("a257d183-69ee-494a-84c3-5ce2215a26ab");
            var active = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.Users WHERE IsDeleted = 0").SingleAsync();
            Assert.Equal(0, active);
            var cleared = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.Users WHERE IsDeleted = 1 AND PasswordHash = ''").SingleAsync();
            Assert.Equal(3, cleared);

            await using var api = new ClaimsApiFactory(true, claimsConnection: database.ConnectionString,
                organizationId: organization, useDatabaseUsers: true, allowRegistration: true);
            using var client = api.CreateApiClient();
            using var retired = await client.PostAsJsonAsync("/api/auth/demo-token", new { username = "manager", password = "1" });
            Assert.Equal(HttpStatusCode.Unauthorized, retired.StatusCode);

            var registration = new RegisterDemoUserRequest("Manager", "Example-password", UserRoles.Manager);
            var key = Guid.NewGuid().ToString();
            var responses = await Task.WhenAll(AuthenticationTests.Register(client, registration, key),
                AuthenticationTests.Register(client, registration, key));
            using var first = responses[0];
            using var replay = responses[1];
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            var account = await first.Content.ReadFromJsonAsync<UserReference>();
            Assert.NotNull(account);
            Assert.NotEqual(retiredManager, account.UserId);
            Assert.Equal(account, await replay.Content.ReadFromJsonAsync<UserReference>());
            using var duplicate = await AuthenticationTests.Register(client, registration with { Username = "mANAGER" },
                Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            using var login = await client.PostAsJsonAsync("/api/auth/demo-token",
                new { username = "manager", password = registration.Password });
            var token = await login.Content.ReadFromJsonAsync<DemoTokenResponse>();
            Assert.NotNull(token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            var entries = await client.GetFromJsonAsync<UserReference[]>("/api/reference/users");
            Assert.Equal(account, Assert.Single(entries!));

            using var scope = api.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            var labels = await users.FindDisplayNamesAsync(organization, [retiredManager, account.UserId], default);
            Assert.Equal("manager", labels[retiredManager]);
            Assert.Equal("Manager", labels[account.UserId]);
            Assert.Empty(await users.FindDisplayNamesAsync(Guid.NewGuid(), [retiredManager, account.UserId], default));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
