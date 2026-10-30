using Expenses.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<EmailNotification> EmailNotifications => Set<EmailNotification>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<Group>().Property(x => x.Name).HasMaxLength(80);
        model.Entity<Group>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<Member>().HasKey(x => new { x.GroupId, x.UserId });
        model.Entity<Member>().Property(x => x.Email).HasMaxLength(256);
        model.Entity<Expense>().Property(x => x.Description).HasMaxLength(160);
        model.Entity<ExpenseShare>().HasKey(x => new { x.ExpenseId, x.UserId });
        model.Entity<Invitation>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<Invitation>().Property(x => x.Email).HasMaxLength(256);
        model.Entity<Invitation>().Property(x => x.TokenHash).HasMaxLength(64);
        model.Entity<EmailNotification>().Property(x => x.Recipient).HasMaxLength(256);
        model.Entity<EmailNotification>().Property(x => x.Subject).HasMaxLength(200);
        model.Entity<EmailNotification>().HasIndex(x => new { x.QueuedAt, x.LeaseUntil });
    }
}
