using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Expenses.Web.Areas.Identity.Pages.Account;
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginModel(SignInManager<IdentityUser> signIn) : PageModel
{
    [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [BindProperty] public bool RememberMe { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public IList<AuthenticationScheme> Providers { get; set; } = [];
    public async Task OnGetAsync()
    {
        ReturnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : TempData.Peek("InvitationReturnUrl") as string;
        Providers = (await signIn.GetExternalAuthenticationSchemesAsync()).ToList();
    }
    public IActionResult OnGetToken([FromServices] IAntiforgery antiforgery) =>
        new JsonResult(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    public async Task<IActionResult> OnPostAsync()
    {
        Providers = (await signIn.GetExternalAuthenticationSchemesAsync()).ToList();
        if (!ModelState.IsValid) return Page();
        var result = await signIn.PasswordSignInAsync(Email.Trim(), Password, RememberMe, lockoutOnFailure: true);
        var destination = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
        if (result.Succeeded) return LocalRedirect(destination);
        if (result.RequiresTwoFactor) return RedirectToPage("./LoginWith2fa", new { ReturnUrl = destination, RememberMe });
        ModelState.AddModelError("", result.IsLockedOut ? "Too many attempts. Please try again later." : "Unable to sign in. Check your details and confirm your email address.");
        return Page();
    }
}
