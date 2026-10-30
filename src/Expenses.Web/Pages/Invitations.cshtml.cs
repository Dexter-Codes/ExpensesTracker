using Expenses.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace Expenses.Web.Pages;
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InvitationsModel(GroupService groups, UserManager<IdentityUser> users) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    public string? Error { get; set; }
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return Page();
        TempData["InvitationReturnUrl"] = Url.Page("/Invitations", new { token = Token });
        return Challenge();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try
        {
            var id = await groups.Accept(Token, (await users.GetUserAsync(User))!);
            TempData.Remove("InvitationReturnUrl");
            return Redirect("/?group=" + id);
        }
        catch (Exception ex) when (ex is ArgumentException or DbUpdateConcurrencyException)
        {
            Error = ex is DbUpdateConcurrencyException ? "The group changed. Please try again." : ex.Message;
            return Page();
        }
    }
}
