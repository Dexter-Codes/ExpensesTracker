using System.Net;
using System.Text.RegularExpressions;
using Expenses.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Expenses.Tests;

public class IdentityEmailTests
{
    [Fact]
    public async Task ConfirmationResendQueuesEmailForAnAccountCreatedThroughMicrosoft()
    {
        await using var app = new TestApp(testAuthentication: false);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = "external@example.com", Email = "external@example.com" };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            Assert.True((await users.AddLoginAsync(user, new("Microsoft", "test-microsoft-account", "Microsoft"))).Succeeded);
            Assert.False(await users.HasPasswordAsync(user));
        }
        var form = await client.GetStringAsync("/Identity/Account/ResendEmailConfirmation");
        var token = WebUtility.HtmlDecode(Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var response = await client.PostAsync("/Identity/Account/ResendEmailConfirmation", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Input.Email"] = "external@example.com", ["__RequestVerificationToken"] = token
        }));
        response.EnsureSuccessStatusCode();
        using var check = app.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = Assert.Single(await db.EmailNotifications.ToListAsync());
        Assert.Equal("external@example.com", email.Recipient);
        Assert.Contains("/Identity/Account/ConfirmEmail", email.Body);
        Assert.Contains("code=", email.Body);
    }

    [Fact]
    public async Task FrameworkEmailSenderUsesTheSameOutboxForConfirmationAndPasswordRecovery()
    {
        await using var app = new TestApp(testAuthentication: false);
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender<IdentityUser>>();
        var user = new IdentityUser { Email = "external@example.com" };
        await sender.SendConfirmationLinkAsync(user, user.Email, "https://localhost/Identity/Account/ConfirmEmail?code=test&amp;userId=example");
        await sender.SendPasswordResetLinkAsync(user, user.Email, "https://localhost/Identity/Account/ResetPassword?code=test&amp;userId=example");
        await sender.SendPasswordResetCodeAsync(user, user.Email, "test-code");
        var messages = await db.EmailNotifications.ToListAsync();
        Assert.Equal(3, messages.Count);
        Assert.All(messages, message => Assert.Equal(user.Email, message.Recipient));
        Assert.DoesNotContain(messages, message => message.Body.Contains("&amp;amp;"));
    }
}
