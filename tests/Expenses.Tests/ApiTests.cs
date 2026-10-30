using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Expenses.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
namespace Expenses.Tests;

public class ApiTests
{
    [Fact] public async Task ApiRequiresSignInAndAntiforgeryAndRestrictsGroupReads()
    {
        await using var app = new TestApp();
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/groups")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new IdentityUser { Id = "alice", UserName = "alice@example.com", Email = "alice@example.com", EmailConfirmed = true });
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("TestUser", "alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/groups", new { name = "Home" })).StatusCode);
        var session = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/session");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        var created = await client.PostAsJsonAsync("/api/groups", new { name = "Home" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var group = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = group.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/groups/{id}")).StatusCode);
        client.DefaultRequestHeaders.Remove("TestUser");
        client.DefaultRequestHeaders.Add("TestUser", "outsider");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/groups/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Dev/Emails")).StatusCode);
    }
}

public class TestApp : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly bool testAuthentication;
    public TestApp(bool testAuthentication = true) { this.testAuthentication = testAuthentication; connection.Open(); }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DataProtection:KeyPath", Path.Combine(Path.GetTempPath(), "expenses-test-keys"));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            if (testAuthentication) services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
        });
    }
    public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await connection.DisposeAsync(); }
}
public class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("TestUser", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.ToString()), new(ClaimTypes.Name, user + "@example.com")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
