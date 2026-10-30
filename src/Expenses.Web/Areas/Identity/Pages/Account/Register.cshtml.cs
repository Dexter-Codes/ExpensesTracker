using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
namespace Expenses.Web.Areas.Identity.Pages.Account;
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RegisterModel(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, IEmailSender emailSender) : PageModel
{
    [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
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
        Email = Email.Trim();
        var user = new IdentityUser { UserName = Email, Email = Email };
        var result = await users.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return Page();
        }
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        var destination = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
        var url = Url.Page("/Account/ConfirmEmail", null, new { area = "Identity", userId = user.Id, code, returnUrl = destination }, Request.Scheme)!;
        await emailSender.SendEmailAsync(Email, "Confirm your Expenses account", $"Welcome to Expenses. <a href=\"{HtmlEncoder.Default.Encode(url)}\">Confirm your email address</a> to get started.");
        return RedirectToPage("./RegisterConfirmation", new { email = Email, returnUrl = destination });
    }
}
