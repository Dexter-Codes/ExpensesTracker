using Expenses.Web.Data;
using Expenses.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace Expenses.Web.Pages.Dev;
public class EmailsModel(ApplicationDbContext db, IWebHostEnvironment environment) : PageModel
{
    public List<EmailNotification> Emails { get; set; } = [];
    public async Task<IActionResult> OnGetAsync()
    {
        if (!environment.IsDevelopment() || HttpContext.Connection.RemoteIpAddress is not { } ip ||
            !System.Net.IPAddress.IsLoopback(ip)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Emails = await db.EmailNotifications.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync();
        return Page();
    }
}
