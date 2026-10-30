using Expenses.Web.Models;

namespace Expenses.Web.Services;

public record Debt(string FromId, string ToId, long Amount);

public static class Balances
{
    // Amounts are integer paise throughout storage and calculations.
    public static Dictionary<string, long> Calculate(Group group)
    {
        var balances = group.Members.ToDictionary(x => x.UserId, _ => 0L);
        foreach (var expense in group.Expenses)
        {
            balances[expense.PaidById] += expense.Amount;
            foreach (var share in expense.Shares) balances[share.UserId] -= share.Amount;
        }
        foreach (var settlement in group.Settlements)
        {
            balances[settlement.FromId] += settlement.Amount;
            balances[settlement.ToId] -= settlement.Amount;
        }
        return balances;
    }

    public static List<Debt> Simplify(Dictionary<string, long> balances)
    {
        var debtors = balances.Where(x => x.Value < 0).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => (Id: x.Key, Amount: -x.Value)).ToArray();
        var creditors = balances.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => (Id: x.Key, Amount: x.Value)).ToArray();
        List<Debt> debts = [];
        int i = 0, j = 0;
        while (i < debtors.Length && j < creditors.Length)
        {
            var amount = Math.Min(debtors[i].Amount, creditors[j].Amount);
            debts.Add(new(debtors[i].Id, creditors[j].Id, amount));
            debtors[i].Amount -= amount;
            creditors[j].Amount -= amount;
            if (debtors[i].Amount == 0) i++;
            if (creditors[j].Amount == 0) j++;
        }
        return debts;
    }

    public static List<ExpenseShare> Split(long amount, string[] members, Dictionary<string, long>? custom)
    {
        if (amount <= 0 || amount > 100_000_000_00L) throw new ArgumentException("Enter an amount between ₹0.01 and ₹10 crore.");
        if (members.Length == 0 || members.Distinct().Count() != members.Length)
            throw new ArgumentException("Choose at least one participant, without duplicates.");
        var ordered = members.Order(StringComparer.Ordinal).ToArray();
        if (custom is not null)
        {
            if (custom.Count != ordered.Length || ordered.Any(x => !custom.ContainsKey(x)) ||
                custom.Values.Any(x => x < 0 || x > amount) || custom.Values.Sum() != amount)
                throw new ArgumentException("Custom shares must be non-negative and add up to the expense total.");
            return ordered.Select(x => new ExpenseShare { UserId = x, Amount = custom[x] }).ToList();
        }
        return ordered.Select((x, i) => new ExpenseShare
        {
            UserId = x, Amount = amount / ordered.Length + (i < amount % ordered.Length ? 1 : 0)
        }).ToList();
    }
}
