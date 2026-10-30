namespace Expenses.Web.Models;

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public int Version { get; set; }
    public List<Member> Members { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
    public List<Settlement> Settlements { get; set; } = [];
}

public class Member
{
    public int GroupId { get; set; }
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
}

public class Expense
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string Description { get; set; } = "";
    public string PaidById { get; set; } = "";
    public string CreatedById { get; set; } = "";
    public long Amount { get; set; }
    public DateOnly Date { get; set; }
    public List<ExpenseShare> Shares { get; set; } = [];
}

public class ExpenseShare
{
    public int ExpenseId { get; set; }
    public string UserId { get; set; } = "";
    public long Amount { get; set; }
}

public class Settlement
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
    public long Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Invitation
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string Email { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}

public class EmailNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? QueuedAt { get; set; }
    public DateTime? LeaseUntil { get; set; }
}
