using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Expenses.Web.Data;
using Expenses.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Expenses.Tests;

public class InvitationFlowTests
{
    [Fact]
    public async Task NewUserCanRegisterConfirmAndReturnToInvitationWithoutReopeningEmail()
    {
        await using var app = new TestApp(testAuthentication: false);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        string invitationUrl;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var owner = new IdentityUser { UserName = "owner@example.com", Email = "owner@example.com", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(owner, "LocalTest-123!")).Succeeded);
            var groups = scope.ServiceProvider.GetRequiredService<GroupService>();
            var group = await groups.Create("Trip", owner);
            await groups.Invite(group.Id, owner, new("guest@example.com", group.Version));
            invitationUrl = Link((await db.EmailNotifications.SingleAsync()).Body, "href");
        }
        // Follow the actual links rendered to an invited visitor who has no account.
        var invitation = await client.GetAsync(invitationUrl);
        Assert.Equal(HttpStatusCode.Redirect, invitation.StatusCode);
        var login = await client.GetStringAsync(invitation.Headers.Location);
        var registerLink = WebUtility.HtmlDecode(Regex.Match(login, "href=\"([^\"]*/Account/Register[^\"]*)\"").Groups[1].Value);
        Assert.NotEmpty(registerLink);
        var registration = await client.GetStringAsync(registerLink);
        var returnUrl = Field(registration, "ReturnUrl");
        Assert.StartsWith("/Invitations?", returnUrl);
        var registered = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Email"] = "guest@example.com", ["Password"] = "LocalTest-123!", ["ConfirmPassword"] = "LocalTest-123!",
            ["ReturnUrl"] = returnUrl, ["__RequestVerificationToken"] = Field(registration, "__RequestVerificationToken")
        }));
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        var checkEmail = await client.GetStringAsync(registered.Headers.Location);
        Assert.Contains("Sign in after confirming", checkEmail);
        string confirmationUrl;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            confirmationUrl = Link((await db.EmailNotifications.SingleAsync(x => x.Subject == "Confirm your Expenses account")).Body, "href");
        }
        var confirmed = await client.GetStringAsync(confirmationUrl);
        Assert.Contains("Email confirmed.", confirmed);
        var continueUrl = Link(confirmed, "href", "Sign in and continue");
        var signIn = await client.GetStringAsync(continueUrl);
        var signedIn = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Email"] = "guest@example.com", ["Password"] = "LocalTest-123!", ["ReturnUrl"] = Field(signIn, "ReturnUrl"),
            ["__RequestVerificationToken"] = Field(signIn, "__RequestVerificationToken")
        }));
        Assert.Equal(returnUrl, signedIn.Headers.Location?.OriginalString);
        var acceptPage = await client.GetStringAsync(signedIn.Headers.Location);
        var accepted = await client.PostAsync("/Invitations", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Token"] = Field(acceptPage, "Token"), ["__RequestVerificationToken"] = Field(acceptPage, "__RequestVerificationToken")
        }));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.StartsWith("/?group=", accepted.Headers.Location?.OriginalString);
        var visibleGroups = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/groups");
        Assert.Single(visibleGroups.EnumerateArray());
        Assert.Equal("Trip", visibleGroups[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task FreshFormTokenHandlesAnAccountCookieChangedInAnotherTab()
    {
        await using var app = new TestApp(testAuthentication: false);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.CreateAsync(new() { UserName = "guest@example.com", Email = "guest@example.com", EmailConfirmed = true }, "LocalTest-123!")).Succeeded);
        }
        var html = await client.GetStringAsync("/Identity/Account/Login");
        var oldToken = Field(html, "__RequestVerificationToken");
        Task<HttpResponseMessage> Submit(string token) => client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Email"] = "guest@example.com", ["Password"] = "LocalTest-123!", ["ReturnUrl"] = "https://untrusted.example/",
            ["__RequestVerificationToken"] = token
        }));
        var first = await Submit(oldToken);
        Assert.Equal("/", first.Headers.Location?.OriginalString);
        // The old anonymous form is now stale, just as a form left open in another tab is.
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(oldToken)).StatusCode);
        var refresh = await client.GetAsync("/Identity/Account/Login?handler=Token");
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.True(refresh.Headers.CacheControl?.NoStore);
        var body = await refresh.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(HttpStatusCode.Redirect, (await Submit(body.GetProperty("token").GetString()!)).StatusCode);
    }

    private static string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html,
        $"name=\"{name}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static string Link(string html, string attribute, string? text = null)
    {
        var pattern = text is null ? $"{attribute}=\"([^\"]+)\"" : $"<a[^>]*{attribute}=\"([^\"]+)\"[^>]*>{Regex.Escape(text)}</a>";
        return WebUtility.HtmlDecode(Regex.Match(html, pattern).Groups[1].Value);
    }
}
