using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Expenses.Web.Data;
using Expenses.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
var sqlServer = builder.Configuration["Database:Provider"] == "SqlServer";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection.");
    if (sqlServer) options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure());
    else options.UseSqlite(connection);
});
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<ApplicationDbContext>();
var apiTokens = new ApiTokenService(builder.Configuration);
builder.Services.AddSingleton(apiTokens);
var auth = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Expenses";
    options.DefaultChallengeScheme = "Expenses";
}).AddPolicyScheme("Expenses", null, options =>
{
    // An invalid Authorization header must never fall back to a valid browser cookie.
    options.ForwardDefaultSelector = context =>
        context.Request.Path.StartsWithSegments("/api") && context.Request.Headers.ContainsKey("Authorization")
            ? JwtBearerDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme;
}).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = false;
    options.TokenValidationParameters = apiTokens.ValidationParameters;
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var principal = context.Principal!;
            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
            var user = userId is null ? null : await users.FindByIdAsync(userId);
            if (user is null || !user.EmailConfirmed || await users.IsLockedOutAsync(user) ||
                principal.FindFirstValue("scope") != "expenses" ||
                principal.FindFirstValue("ver") != ApiTokenService.UserVersion(user))
            {
                context.Fail("This API token is no longer valid.");
                return;
            }
            ((ClaimsIdentity)principal.Identity!).AddClaim(new(ClaimTypes.NameIdentifier, user.Id));
        }
    };
});
if (!string.IsNullOrEmpty(builder.Configuration["Authentication:Google:ClientId"]))
    auth.AddGoogle(options => builder.Configuration.GetSection("Authentication:Google").Bind(options));
if (!string.IsNullOrEmpty(builder.Configuration["Authentication:Microsoft:ClientId"]))
    auth.AddMicrosoftAccount(options => builder.Configuration.GetSection("Authentication:Microsoft").Bind(options));
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 403;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddAuthorization(options => options.AddPolicy("BrowserSession", policy =>
    policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser()));
builder.Services.AddRazorPages();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<IEmailSender, EmailService>();
builder.Services.AddHostedService<EmailDispatcher>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();
var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
    builder.Services.AddDataProtection().SetApplicationName("Expenses")
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

var app = builder.Build();
// Schema creation is explicit. Azure startup never creates or changes a database.
if (args.Contains("--init-db"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
    Console.WriteLine("Database initialized. Existing databases are not upgraded by this command.");
    return;
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api") &&
        ex is ArgumentException or KeyNotFoundException or UnauthorizedAccessException or DbUpdateConcurrencyException or AntiforgeryValidationException)
    {
        context.Response.StatusCode = ex switch
        {
            KeyNotFoundException => 404,
            UnauthorizedAccessException => 403,
            DbUpdateConcurrencyException => 409,
            _ => 400
        };
        await context.Response.WriteAsJsonAsync(new { error = ex is DbUpdateConcurrencyException
            ? "This group changed. Refresh it and try again." : ex.Message });
    }
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

var api = app.MapGroup("/api").RequireAuthorization();
api.AddEndpointFilter(async (context, next) =>
{
    var bearer = context.HttpContext.User.Identities.Any(identity =>
        identity.IsAuthenticated && identity.AuthenticationType == JwtBearerDefaults.AuthenticationScheme);
    if (!HttpMethods.IsGet(context.HttpContext.Request.Method) && !bearer)
        await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
    return await next(context);
});
api.MapGet("/session", (HttpContext context, IAntiforgery antiforgery) => new
{
    userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier), email = context.User.Identity!.Name,
    csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken
});
api.MapGet("/groups", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
{
    var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
    return Results.Ok(await db.Groups.Where(x => x.Members.Any(m => m.UserId == id))
        .Select(x => new { x.Id, x.Name, MemberCount = x.Members.Count }).ToListAsync());
});
api.MapPost("/groups", async (CreateGroup input, ClaimsPrincipal principal, UserManager<IdentityUser> users, GroupService groups) =>
{
    var group = await groups.Create(input.Name, (await users.GetUserAsync(principal))!);
    return Results.Ok(new { group.Id, group.Name });
});
api.MapGet("/groups/{id:int}", async (int id, ClaimsPrincipal principal, GroupService groups) =>
{
    var group = await groups.Get(id, principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var balances = Balances.Calculate(group);
    return Results.Ok(new { group.Id, group.Name, group.OwnerId, group.Version, group.Members,
        expenses = group.Expenses.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id),
        settlements = group.Settlements.OrderByDescending(x => x.CreatedAt), balances, debts = Balances.Simplify(balances) });
});
api.MapPost("/groups/{id:int}/invitations", async (int id, InvitationInput input, ClaimsPrincipal principal,
    UserManager<IdentityUser> users, GroupService groups) =>
{
    await groups.Invite(id, (await users.GetUserAsync(principal))!, input);
    return Results.NoContent();
});
api.MapPost("/groups/{id:int}/expenses", async (int id, ExpenseInput input, ClaimsPrincipal principal,
    UserManager<IdentityUser> users, GroupService groups) =>
{
    await groups.SaveExpense(id, null, (await users.GetUserAsync(principal))!, input);
    return Results.NoContent();
});
api.MapPut("/groups/{id:int}/expenses/{expenseId:int}", async (int id, int expenseId, ExpenseInput input, ClaimsPrincipal principal,
    UserManager<IdentityUser> users, GroupService groups) =>
{
    await groups.SaveExpense(id, expenseId, (await users.GetUserAsync(principal))!, input);
    return Results.NoContent();
});
api.MapDelete("/groups/{id:int}/expenses/{expenseId:int}", async (int id, int expenseId, int version, ClaimsPrincipal principal,
    UserManager<IdentityUser> users, GroupService groups) =>
{
    await groups.DeleteExpense(id, expenseId, (await users.GetUserAsync(principal))!, version);
    return Results.NoContent();
});
api.MapPost("/groups/{id:int}/settlements", async (int id, SettlementInput input, ClaimsPrincipal principal,
    UserManager<IdentityUser> users, GroupService groups) =>
{
    await groups.Settle(id, (await users.GetUserAsync(principal))!, input);
    return Results.NoContent();
});
app.Run();

public record CreateGroup(string Name);
public partial class Program;
