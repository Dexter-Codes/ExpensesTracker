using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Expenses.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ConfirmEmailModel(UserManager<IdentityUser> users) : PageModel
{
    public bool Confirmed { get; set; }
    public string ReturnUrl { get; set; } = "/";
    public async Task OnGetAsync(string? userId, string? code, string? returnUrl)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        if (userId is null || code is null) return;
        var user = await users.FindByIdAsync(userId);
        if (user is null) return;
        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            Confirmed = (await users.ConfirmEmailAsync(user, token)).Succeeded;
            if (Confirmed && ReturnUrl.StartsWith("/Invitations?", StringComparison.OrdinalIgnoreCase))
                TempData["InvitationReturnUrl"] = ReturnUrl;
        }
        catch (FormatException) { }
    }
}
