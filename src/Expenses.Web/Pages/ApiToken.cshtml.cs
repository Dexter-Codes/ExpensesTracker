using Expenses.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Expenses.Web.Pages;

[Authorize(Policy = "BrowserSession")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ApiTokenModel(ApiTokenService tokens, SignInManager<IdentityUser> signIn) : PageModel
{
    public Services.ApiToken? Token { get; private set; }
    public IActionResult OnGet() => tokens.Enabled ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!tokens.Enabled) return NotFound();
        var user = await signIn.ValidateSecurityStampAsync(User);
        if (user is null || !user.EmailConfirmed || await signIn.UserManager.IsLockedOutAsync(user)) return Unauthorized();
        Token = tokens.Create(user);
        return Page();
    }
}
