using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Expenses.Web.Data;
using Expenses.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Expenses.Tests;

public class ApiTokenTests
{
    // Test fixture only; deployment uses a separately generated Key Vault secret.
    private const string TestKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Fact]
    public async Task CookieIssuanceRequiresCsrfAndBearerWritesDoNot()
    {
        await using var app = new TokenApp();
        using var browser = Client(app);
        await SignIn(app, browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/groups", new { name = "Home" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/ApiToken", new FormUrlEncodedContent([]))).StatusCode);

        var page = await browser.GetStringAsync("/ApiToken");
        var created = await browser.PostAsync("/ApiToken", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = FormToken(page)
        }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.True(created.Headers.CacheControl!.NoStore);
        var html = await created.Content.ReadAsStringAsync();
        var token = WebUtility.HtmlDecode(Regex.Match(html, "<textarea[^>]*id=\"access-token\"[^>]*>([^<]+)</textarea>").Groups[1].Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("alice", jwt.Subject);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.ValidFrom);

        using var api = Client(app, cookies: false);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var group = await api.PostAsJsonAsync("/api/groups", new { name = "API home" });
        Assert.Equal(HttpStatusCode.OK, group.StatusCode);
        var groups = await api.GetFromJsonAsync<JsonElement>("/api/groups");
        Assert.Equal("API home", groups[0].GetProperty("name").GetString());
        // A bearer token cannot be used to mint another token through the browser page.
        Assert.Equal(HttpStatusCode.Redirect, (await api.GetAsync("/ApiToken")).StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("algorithm")]
    [InlineData("scope")]
    [InlineData("subject")]
    [InlineData("version")]
    public async Task InvalidTokensAreRejectedEvenWithAValidCookie(string problem)
    {
        await using var app = new TokenApp();
        using var browser = Client(app);
        var user = await SignIn(app, browser);
        var token = InvalidToken(app, user, problem);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await browser.GetAsync("/api/groups");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/groups", new { name = "Rejected" })).StatusCode);
    }

    [Fact]
    public async Task BearerTokensKeepMembershipChecksAndCannotBypassCsrfWithAnArbitraryHeader()
    {
        await using var app = new TokenApp();
        using var browser = Client(app);
        var user = await SignIn(app, browser);
        var session = await browser.GetFromJsonAsync<JsonElement>("/api/session");
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        var response = await browser.PostAsJsonAsync("/api/groups", new { name = "Private" });
        var group = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = group.GetProperty("id").GetInt32();

        IdentityUser outsider;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            outsider = new() { Id = "outsider", UserName = "outsider@example.com", Email = "outsider@example.com", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(outsider)).Succeeded);
        }
        using var api = Client(app, cookies: false);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Services.GetRequiredService<ApiTokenService>().Create(outsider).AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/groups/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsJsonAsync($"/api/groups/{id}/expenses", new
        {
            description = "Unauthorized", paidById = outsider.Id, amount = 10000,
            date = "2026-01-01", participants = new[] { outsider.Id }, version = 0
        })).StatusCode);

        browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        browser.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/groups", new { name = "No bypass" })).StatusCode);
    }

    [Theory]
    [InlineData("stamp")]
    [InlineData("unconfirmed")]
    [InlineData("locked")]
    [InlineData("deleted")]
    public async Task AccountChangesInvalidateTokens(string change)
    {
        await using var app = new TokenApp();
        using var browser = Client(app);
        var user = await SignIn(app, browser);
        var token = app.Services.GetRequiredService<ApiTokenService>().Create(user).AccessToken;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var current = (await users.FindByIdAsync(user.Id))!;
            if (change == "unconfirmed") current.EmailConfirmed = false;
            var result = change switch
            {
                "stamp" => await users.UpdateSecurityStampAsync(current),
                "unconfirmed" => await users.UpdateAsync(current),
                "locked" => await users.SetLockoutEndDateAsync(current, DateTimeOffset.UtcNow.AddHours(1)),
                _ => await users.DeleteAsync(current)
            };
            Assert.True(result.Succeeded);
        }
        using var api = Client(app, cookies: false);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/groups")).StatusCode);
        if (change == "stamp")
        {
            var page = await browser.GetStringAsync("/ApiToken");
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync("/ApiToken",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = FormToken(page)
                }))).StatusCode);
        }
    }

    [Fact]
    public async Task TokenFeatureStaysDisabledWithoutAKey()
    {
        await using var app = new TestApp(testAuthentication: false);
        using var browser = Client(app);
        await SignIn(app, browser);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/ApiToken")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/groups")).StatusCode);
    }

    private static HttpClient Client(TestApp app, bool cookies = true) => app.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = cookies
    });

    private static async Task<IdentityUser> SignIn(TestApp app, HttpClient browser)
    {
        IdentityUser user;
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            user = new() { Id = "alice", UserName = "alice@example.com", Email = "alice@example.com", EmailConfirmed = true, LockoutEnabled = true };
            Assert.True((await users.CreateAsync(user, "LocalTest-123!")).Succeeded);
        }
        var page = await browser.GetStringAsync("/Identity/Account/Login");
        var response = await browser.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = user.Email!, ["Password"] = "LocalTest-123!", ["__RequestVerificationToken"] = FormToken(page)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return user;
    }

    private static string InvalidToken(TokenApp app, IdentityUser user, string problem)
    {
        var tokens = app.Services.GetRequiredService<ApiTokenService>();
        var valid = new JwtSecurityTokenHandler().ReadJwtToken(tokens.Create(user).AccessToken);
        var claims = valid.Claims.Where(claim => claim.Type is not ("iss" or "aud" or "nbf" or "exp") &&
            !(problem == "scope" && claim.Type == "scope") &&
            !(problem == "subject" && claim.Type == "sub") &&
            !(problem == "version" && claim.Type == "ver"));
        var keyBytes = Convert.FromBase64String(TestKey);
        if (problem == "signature") keyBytes[0] ^= 255;
        if (problem == "algorithm") keyBytes = [.. keyBytes, .. keyBytes];
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(problem == "issuer" ? "other" : tokens.Issuer,
            problem == "audience" ? "other" : tokens.Audience, claims,
            problem == "expired" ? now.AddHours(-1) : problem == "future" ? now.AddMinutes(5) : now.AddMinutes(-1),
            problem == "expired" ? now.AddMinutes(-5) : now.AddMinutes(15),
            new SigningCredentials(new SymmetricSecurityKey(keyBytes),
                problem == "algorithm" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string FormToken(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private class TokenApp : TestApp
    {
        public TokenApp() : base(testAuthentication: false) { }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ApiTokens:SigningKey", TestKey);
        }
    }
}
