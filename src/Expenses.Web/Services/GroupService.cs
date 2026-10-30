using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Expenses.Web.Data;
using Expenses.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Web.Services;

public record ExpenseInput(string Description, string PaidById, long Amount, DateOnly Date,
    string[] Participants, Dictionary<string, long>? CustomShares, int Version);
public record SettlementInput(string ToId, long Amount, int Version);
public record InvitationInput(string Email, int Version);

public class GroupService(ApplicationDbContext db, IConfiguration configuration)
{
    public async Task<Group> Get(int id, string userId)
    {
        var group = await db.Groups.Include(x => x.Members).Include(x => x.Expenses)
            .ThenInclude(x => x.Shares).Include(x => x.Settlements).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id && x.Members.Any(m => m.UserId == userId));
        return group ?? throw new KeyNotFoundException("Group not found.");
    }

    public async Task<Group> Create(string name, IdentityUser user)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 80) throw new ArgumentException("Use a group name of 1–80 characters.");
        var group = new Group { Name = name, OwnerId = user.Id };
        group.Members.Add(new Member { UserId = user.Id, Email = user.Email! });
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    public async Task Invite(int id, IdentityUser actor, InvitationInput input)
    {
        var group = await Get(id, actor.Id);
        CheckVersion(group, input.Version);
        var email = (input.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new ArgumentException("Enter a valid email address.");
        if (group.Members.Count >= 50) throw new ArgumentException("This group has reached its 50-member limit.");
        if (group.Members.Any(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("That person is already a member.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.Invitations.Add(new Invitation
        {
            GroupId = id, Email = email, TokenHash = Hash(token), ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        var url = BaseUrl() + "/Invitations?token=" + token;
        Queue(email, $"Invitation to {group.Name}", $"{Encode(actor.Email!)} invited you to {Encode(group.Name)}. " +
            $"<a href=\"{Encode(url)}\">Accept invitation</a>. This link expires in seven days. Sign in using this email address.");
        await db.SaveChangesAsync();
    }

    public async Task<int> Accept(string token, IdentityUser user)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64) throw new ArgumentException("Invalid invitation.");
        var hash = Hash(token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (invitation is null || invitation.ExpiresAt < DateTime.UtcNow)
            throw new ArgumentException("This invitation is invalid or expired.");
        if (!user.EmailConfirmed || !string.Equals(invitation.Email, user.Email, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Sign in with the verified email address that received this invitation.");
        if (invitation.AcceptedAt is not null) return invitation.GroupId;
        var group = await db.Groups.Include(x => x.Members).SingleAsync(x => x.Id == invitation.GroupId);
        if (group.Members.Count >= 50) throw new ArgumentException("This group has reached its 50-member limit.");
        if (group.Members.All(x => x.UserId != user.Id))
            group.Members.Add(new Member { UserId = user.Id, Email = user.Email! });
        group.Version++;
        invitation.AcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return group.Id;
    }

    public async Task SaveExpense(int id, int? expenseId, IdentityUser actor, ExpenseInput input)
    {
        var group = await Get(id, actor.Id);
        CheckVersion(group, input.Version);
        var description = (input.Description ?? "").Trim();
        if (description.Length is < 1 or > 160) throw new ArgumentException("Use a description of 1–160 characters.");
        if (input.Date < new DateOnly(2000, 1, 1) || input.Date > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            throw new ArgumentException("Choose an expense date between 2000 and today.");
        var participants = input.Participants ?? [];
        var ids = group.Members.Select(x => x.UserId).ToHashSet();
        if (!ids.Contains(input.PaidById) || participants.Any(x => !ids.Contains(x)))
            throw new ArgumentException("The payer and all participants must belong to this group.");
        var shares = Balances.Split(input.Amount, participants, input.CustomShares);
        var expense = expenseId is null ? new Expense { GroupId = id, CreatedById = actor.Id } :
            group.Expenses.SingleOrDefault(x => x.Id == expenseId) ?? throw new KeyNotFoundException("Expense not found.");
        if (expenseId is not null && expense.CreatedById != actor.Id && group.OwnerId != actor.Id)
            throw new UnauthorizedAccessException("Only the expense creator or group owner can change this expense.");
        var affected = expense.Shares.Select(x => x.UserId).Append(expense.PaidById)
            .Concat(participants).Append(input.PaidById).ToHashSet();
        expense.Description = description;
        expense.PaidById = input.PaidById;
        expense.Amount = input.Amount;
        expense.Date = input.Date;
        // Update existing rows in place so editing does not track duplicate composite keys.
        foreach (var existing in expense.Shares.ToList())
        {
            var replacement = shares.SingleOrDefault(x => x.UserId == existing.UserId);
            if (replacement is null) expense.Shares.Remove(existing);
            else existing.Amount = replacement.Amount;
        }
        foreach (var share in shares.Where(x => expense.Shares.All(s => s.UserId != x.UserId))) expense.Shares.Add(share);
        if (expenseId is null) group.Expenses.Add(expense);
        Notify(group, actor, affected, $"Expense {(expenseId is null ? "added" : "updated")}: {description}");
        await db.SaveChangesAsync();
    }

    public async Task DeleteExpense(int id, int expenseId, IdentityUser actor, int version)
    {
        var group = await Get(id, actor.Id);
        CheckVersion(group, version);
        var expense = group.Expenses.SingleOrDefault(x => x.Id == expenseId) ?? throw new KeyNotFoundException("Expense not found.");
        if (expense.CreatedById != actor.Id && group.OwnerId != actor.Id)
            throw new UnauthorizedAccessException("Only the expense creator or group owner can delete this expense.");
        Notify(group, actor, expense.Shares.Select(x => x.UserId).Append(expense.PaidById).ToHashSet(),
            $"Expense deleted: {expense.Description}");
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
    }

    public async Task Settle(int id, IdentityUser actor, SettlementInput input)
    {
        var group = await Get(id, actor.Id);
        CheckVersion(group, input.Version);
        var balances = Balances.Calculate(group);
        if (input.ToId == actor.Id || !balances.TryGetValue(input.ToId, out var owed) ||
            input.Amount <= 0 || balances[actor.Id] >= 0 || owed <= 0 ||
            input.Amount > Math.Min(-balances[actor.Id], owed))
            throw new ArgumentException("The payment must be to a member who is owed money and cannot exceed your outstanding balance or theirs.");
        group.Settlements.Add(new Settlement { FromId = actor.Id, ToId = input.ToId, Amount = input.Amount });
        Notify(group, actor, [input.ToId], $"Payment recorded: ₹{input.Amount / 100m:0.00}");
        await db.SaveChangesAsync();
    }

    private void Notify(Group group, IdentityUser actor, HashSet<string> recipients, string subject)
    {
        foreach (var member in group.Members.Where(x => recipients.Contains(x.UserId) && x.UserId != actor.Id))
            Queue(member.Email, subject, $"{Encode(actor.Email!)} made an update in {Encode(group.Name)}: {Encode(subject)}. " +
                $"<a href=\"{Encode(BaseUrl())}\">View your group</a>.");
    }

    private void Queue(string recipient, string subject, string body) => db.EmailNotifications.Add(new()
    {
        Recipient = recipient, Subject = subject.Length > 200 ? subject[..200] : subject, Body = body
    });

    private string BaseUrl() => configuration["App:BaseUrl"]?.TrimEnd('/') ?? "http://localhost:5080";
    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void CheckVersion(Group group, int version)
    {
        if (group.Version != version) throw new DbUpdateConcurrencyException();
        group.Version++;
    }
}
