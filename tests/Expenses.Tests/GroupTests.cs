using Expenses.Web.Data;
using Expenses.Web.Models;
using Expenses.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Expenses.Tests;

public class GroupTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly ApplicationDbContext db;
    private readonly GroupService service;
    private readonly IdentityUser alice = new() { Id = "alice", Email = "alice@example.com", EmailConfirmed = true };
    private readonly IdentityUser bob = new() { Id = "bob", Email = "bob@example.com", EmailConfirmed = true };
    private readonly IdentityUser cara = new() { Id = "cara", Email = "cara@example.com", EmailConfirmed = true };
    public GroupTests()
    {
        connection.Open();
        db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new(db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["App:BaseUrl"] = "http://localhost:5080" }).Build());
    }
    private async Task<Group> Group()
    {
        var group = await service.Create("Weekend", alice);
        group.Members.Add(new() { UserId = bob.Id, Email = bob.Email! });
        group.Members.Add(new() { UserId = cara.Id, Email = cara.Email! });
        await db.SaveChangesAsync();
        return group;
    }
    private static ExpenseInput Input(int version, long amount = 10000) => new("Dinner", "alice", amount,
        DateOnly.FromDateTime(DateTime.UtcNow), ["alice", "bob"], null, version);

    [Fact] public void EqualSplitPreservesEveryPaise()
    {
        var shares = Balances.Split(100, ["c", "a", "b"], null);
        Assert.Equal(100, shares.Sum(s => s.Amount));
        Assert.Equal(34, shares.Single(s => s.UserId == "a").Amount);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(10000000001)]
    public void RejectsInvalidAmounts(long amount) => Assert.Throws<ArgumentException>(() => Balances.Split(amount, ["a"], null));
    [Fact] public void RejectsIncorrectCustomTotal() => Assert.Throws<ArgumentException>(() => Balances.Split(100, ["a", "b"], new() { ["a"] = 40, ["b"] = 50 }));
    [Fact] public void RejectsNegativeCustomShare() => Assert.Throws<ArgumentException>(() => Balances.Split(100, ["a", "b"], new() { ["a"] = -10, ["b"] = 110 }));
    [Fact] public void RejectsDuplicateParticipants() => Assert.Throws<ArgumentException>(() => Balances.Split(100, ["a", "a"], null));
    [Fact] public void RejectsUnknownCustomParticipant() => Assert.Throws<ArgumentException>(() => Balances.Split(100, ["a"], new() { ["b"] = 100 }));
    [Fact] public async Task ExpenseAndPartialPaymentUpdateBalances()
    {
        var group = await Group();
        await service.SaveExpense(group.Id, null, alice, Input(group.Version));
        Assert.Equal(-5000, Balances.Calculate(group)["bob"]);
        await service.Settle(group.Id, bob, new("alice", 2000, group.Version));
        Assert.Equal(-3000, Balances.Calculate(group)["bob"]);
        Assert.Equal(3000, Balances.Calculate(group)["alice"]);
        Assert.Equal(0, Balances.Calculate(group).Values.Sum());
    }
    [Fact] public async Task EditUpdatesExistingSharesWithoutDuplicateKeys()
    {
        var group = await Group();
        await service.SaveExpense(group.Id, null, alice, Input(group.Version));
        await service.SaveExpense(group.Id, group.Expenses.Single().Id, alice, Input(group.Version, 20000));
        Assert.Equal(10000, Balances.Calculate(group)["alice"]);
        Assert.Equal(2, await db.Set<ExpenseShare>().CountAsync());
        await service.SaveExpense(group.Id, group.Expenses.Single().Id, alice,
            Input(group.Version) with { Participants = ["bob", "cara"] });
        Assert.Equal(2, await db.Set<ExpenseShare>().CountAsync());
        Assert.Equal(10000, Balances.Calculate(group)["alice"]);
    }
    [Fact] public async Task DeleteRecalculatesBalances()
    {
        var group = await Group();
        await service.SaveExpense(group.Id, null, alice, Input(group.Version));
        await service.DeleteExpense(group.Id, group.Expenses.Single().Id, alice, group.Version);
        db.ChangeTracker.Clear();
        Assert.All(Balances.Calculate(await service.Get(group.Id, alice.Id)).Values, v => Assert.Equal(0, v));
    }
    [Fact] public async Task OutsiderCannotReadOrChangeGroup()
    {
        var group = await Group();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Get(group.Id, "outsider"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SaveExpense(group.Id, null, new() { Id = "outsider" }, Input(group.Version)));
    }
    [Fact] public async Task UnrelatedMemberCannotEditExpense()
    {
        var group = await Group();
        await service.SaveExpense(group.Id, null, alice, Input(group.Version));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveExpense(group.Id, group.Expenses.Single().Id, bob, Input(group.Version)));
    }
    [Fact] public async Task RejectsPayerOutsideGroup()
    {
        var group = await Group();
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveExpense(group.Id, null, alice, Input(group.Version) with { PaidById = "outsider" }));
        Assert.Empty(await db.Expenses.ToListAsync());
    }
    [Fact] public async Task RejectsParticipantOutsideGroup()
    {
        var group = await Group();
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveExpense(group.Id, null, alice, Input(group.Version) with { Participants = ["outsider"] }));
    }
    [Fact] public async Task RejectsOverpaymentAndSelfPayment()
    {
        var group = await Group();
        await service.SaveExpense(group.Id, null, alice, Input(group.Version));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Settle(group.Id, bob, new("alice", 5001, group.Version)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Settle(group.Id, bob, new("bob", 1, group.Version)));
    }
    [Fact] public async Task StaleVersionCannotDuplicatePayment()
    {
        var group = await Group();
        await service.SaveExpense(group.Id, null, alice, Input(group.Version));
        var version = group.Version;
        await service.Settle(group.Id, bob, new("alice", 1000, version));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.Settle(group.Id, bob, new("alice", 1000, version)));
        Assert.Single(await db.Settlements.ToListAsync());
    }
    [Fact] public async Task ExpenseEmailsExcludeActorAndUnaffectedMembers()
    {
        var group = await Group();
        await service.SaveExpense(group.Id, null, alice, Input(group.Version));
        var email = Assert.Single(await db.EmailNotifications.ToListAsync());
        Assert.Equal(bob.Email, email.Recipient);
        Assert.Contains("Expense added", email.Subject);
    }
    [Fact] public async Task EditingNotifiesOldAndNewParticipants()
    {
        var group = await Group();
        await service.SaveExpense(group.Id, null, alice, Input(group.Version));
        await service.SaveExpense(group.Id, group.Expenses.Single().Id, alice,
            Input(group.Version) with { Participants = ["alice", "cara"] });
        var emails = await db.EmailNotifications.Where(e => e.Subject.StartsWith("Expense updated")).ToListAsync();
        Assert.Equal(new[] { bob.Email, cara.Email }, emails.Select(e => e.Recipient).Order().ToArray());
    }
    [Fact] public async Task InvitationRequiresMatchingVerifiedEmail()
    {
        var group = await service.Create("Home", alice);
        await service.Invite(group.Id, alice, new("bob@example.com", group.Version));
        var body = (await db.EmailNotifications.SingleAsync()).Body;
        var token = System.Text.RegularExpressions.Regex.Match(body, @"token=([A-F0-9]+)").Groups[1].Value;
        await Assert.ThrowsAsync<ArgumentException>(() => service.Accept(token, cara));
        bob.EmailConfirmed = false;
        await Assert.ThrowsAsync<ArgumentException>(() => service.Accept(token, bob));
        bob.EmailConfirmed = true;
        Assert.Equal(group.Id, await service.Accept(token, bob));
        Assert.Equal(group.Id, await service.Accept(token, bob));
        Assert.Equal(2, group.Members.Count);
    }
    [Fact] public void SimplifiedPaymentsPreserveNetBalances()
    {
        var balances = new Dictionary<string,long> { ["a"] = -5001, ["b"] = -1000, ["c"] = 4000, ["d"] = 2001 };
        foreach (var debt in Balances.Simplify(balances)) { balances[debt.FromId] += debt.Amount; balances[debt.ToId] -= debt.Amount; }
        Assert.All(balances.Values, value => Assert.Equal(0, value));
    }
    public void Dispose() { db.Dispose(); connection.Dispose(); }
}
