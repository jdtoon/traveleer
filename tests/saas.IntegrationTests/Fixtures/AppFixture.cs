using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using saas.Modules.Auth;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Fixtures;

/// <summary>
/// Shared test fixture that boots the app with a seeded demo tenant.
/// Uses Development environment so DevSeed provisions `demo` tenant automatically.
/// Provides authenticated clients for tenant (Admin/Member) and SuperAdmin routes.
/// </summary>
public class AppFixture : IDisposable
{
    public WebApplicationFactory<Program> Factory { get; }

    private TestUserData? _adminUser;
    private TestUserData? _memberUser;

    private readonly Lazy<WebApplicationFactory<Program>> _adminTenantFactory;
    private readonly Lazy<WebApplicationFactory<Program>> _memberTenantFactory;
    private readonly Lazy<WebApplicationFactory<Program>> _superAdminFactory;

    public AppFixture()
    {
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.UseSetting("DevSeed:Enabled", "true");
                builder.UseSetting("DevSeed:TenantSlug", "demo");
                builder.UseSetting("DevSeed:AdminEmail", "admin@demo.local");
                builder.UseSetting("DevSeed:MemberEmail", "member@demo.local");
                builder.UseSetting("DevSeed:PlanSlug", "starter");
                builder.UseSetting("Billing:Provider", "Mock");
                builder.UseSetting("Turnstile:Provider", "Mock");
                builder.UseSetting("Email:Provider", "Console");
                builder.UseSetting("Storage:Provider", "Local");

                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Litestream:AutoRestoreEnabled"] = "false",
                        ["Litestream:KeyBackupEnabled"] = "false"
                    });
                });
            });

        // Force the server to start (and run all initialization including seeding)
        _ = Factory.Server;

        LoadTestUserData();

        _adminTenantFactory = new(() => CreateDerivedFactory(
            AuthSchemes.Tenant, BuildTenantClaims(_adminUser!, "demo")));
        _memberTenantFactory = new(() => CreateDerivedFactory(
            AuthSchemes.Tenant, BuildTenantClaims(_memberUser!, "demo")));
        _superAdminFactory = new(() => CreateDerivedFactory(
            AuthSchemes.SuperAdmin, BuildSuperAdminClaims(_adminUser!)));
    }

    private void LoadTestUserData()
    {
        using var connection = OpenTenantConnection("demo");

        _adminUser = LoadUserData(connection, "ADMIN@DEMO.LOCAL");
        _memberUser = LoadUserData(connection, "MEMBER@DEMO.LOCAL");
    }

    private static TestUserData LoadUserData(SqliteConnection connection, string normalizedEmail)
    {
        string userId, email, name;
        using (var userCmd = connection.CreateCommand())
        {
            userCmd.CommandText = "SELECT Id, Email, COALESCE(UserName, DisplayName, Email) FROM AspNetUsers WHERE NormalizedEmail = @email";
            userCmd.Parameters.AddWithValue("@email", normalizedEmail);
            using var reader = userCmd.ExecuteReader();
            reader.Read();
            userId = reader.GetString(0);
            email = reader.GetString(1);
            name = reader.GetString(2);
        }

        var roles = new List<string>();
        using (var roleCmd = connection.CreateCommand())
        {
            roleCmd.CommandText = """
                SELECT r.Name FROM AspNetRoles r
                INNER JOIN AspNetUserRoles ur ON r.Id = ur.RoleId
                WHERE ur.UserId = @userId
                """;
            roleCmd.Parameters.AddWithValue("@userId", userId);
            using var reader = roleCmd.ExecuteReader();
            while (reader.Read()) roles.Add(reader.GetString(0));
        }

        var permissions = new List<string>();
        using (var permCmd = connection.CreateCommand())
        {
            permCmd.CommandText = """
                SELECT DISTINCT p.Key FROM Permissions p
                INNER JOIN RolePermissions rp ON p.Id = rp.PermissionId
                INNER JOIN AspNetUserRoles ur ON rp.RoleId = ur.RoleId
                WHERE ur.UserId = @userId
                """;
            permCmd.Parameters.AddWithValue("@userId", userId);
            using var reader = permCmd.ExecuteReader();
            while (reader.Read()) permissions.Add(reader.GetString(0));
        }

        return new TestUserData(userId, email, name, roles, permissions);
    }

    private static List<Claim> BuildTenantClaims(TestUserData user, string slug)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(AuthClaims.TenantSlug, slug),
        };
        foreach (var role in user.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        foreach (var permission in user.Permissions)
            claims.Add(new Claim(AuthClaims.Permission, permission));
        return claims;
    }

    private static List<Claim> BuildSuperAdminClaims(TestUserData user)
    {
        return
        [
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(AuthClaims.IsSuperAdmin, "true"),
        ];
    }

    private WebApplicationFactory<Program> CreateDerivedFactory(string scheme, List<Claim> claims)
    {
        return Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddTransient<TestAuthHandler>();

                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    if (options.SchemeMap.TryGetValue(scheme, out var existing))
                    {
                        existing.HandlerType = typeof(TestAuthHandler);
                    }
                });

                services.Configure<TestAuthHandlerOptions>(scheme, opt =>
                {
                    opt.Claims = claims;
                });
            });
        });
    }

    public string GetTenantDbPath(string slug = "demo")
    {
        var config = Factory.Services.GetRequiredService<IConfiguration>();
        var tenantPath = config["Tenancy:DatabasePath"] ?? Path.Combine("db", "tenants");
        var basePath = Path.IsPathRooted(tenantPath)
            ? tenantPath
            : Path.Combine(Directory.GetCurrentDirectory(), tenantPath);

        return Path.Combine(basePath, $"{slug}.db");
    }

    public SqliteConnection OpenTenantConnection(string slug = "demo")
    {
        var connection = new SqliteConnection($"Data Source={GetTenantDbPath(slug)}");
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Create an unauthenticated client for public routes (marketing, auth, registration).</summary>
    public HtmxTestClient<Program> CreateClient() => new(Factory);

    /// <summary>Create an authenticated client with Admin role for tenant routes.</summary>
    public HtmxTestClient<Program> CreateTenantClient(string slug = "demo") => new(_adminTenantFactory.Value);

    /// <summary>Create an authenticated client with Member role for tenant routes.</summary>
    public HtmxTestClient<Program> CreateTenantMemberClient(string slug = "demo") => new(_memberTenantFactory.Value);

    /// <summary>Create an authenticated client with SuperAdmin claims.</summary>
    public HtmxTestClient<Program> CreateSuperAdminClient() => new(_superAdminFactory.Value);
}

public record TestUserData(string UserId, string Email, string Name, List<string> Roles, List<string> Permissions);
