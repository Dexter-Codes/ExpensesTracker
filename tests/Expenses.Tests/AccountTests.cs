using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Expenses.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Expenses.Tests;
public class AccountTests
{
    [Fact] public async Task RegistrationRequiresEmailConfirmationThenCookieSignInWorks()
    {
        await using var app = new TestApp(testAuthentication: false);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
        var register = await client.GetStringAsync("/Identity/Account/Register");
        var token = Token(register);
        var result = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Email"] = "alice@example.com", ["Password"] = "LocalTest-123!", ["ConfirmPassword"] = "LocalTest-123!", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        string link;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False((await db.Users.SingleAsync()).EmailConfirmed);
            var email = await db.EmailNotifications.SingleAsync();
            link = WebUtility.HtmlDecode(Regex.Match(email.Body, "href=\"([^\"]+)\"").Groups[1].Value);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(link)).StatusCode);
        var login = await client.GetStringAsync("/Identity/Account/Login");
        var signedIn = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Email"] = "alice@example.com", ["Password"] = "LocalTest-123!", ["__RequestVerificationToken"] = Token(login)
        }));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/groups")).StatusCode);
        var session = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/session");
        Assert.Equal("alice@example.com", session.GetProperty("email").GetString());
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
}
