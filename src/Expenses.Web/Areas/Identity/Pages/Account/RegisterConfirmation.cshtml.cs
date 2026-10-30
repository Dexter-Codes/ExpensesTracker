using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Expenses.Web.Areas.Identity.Pages.Account;
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RegisterConfirmationModel : PageModel
{
    public string ReturnUrl { get; set; } = "/";
    public void OnGet(string? returnUrl) => ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
}
