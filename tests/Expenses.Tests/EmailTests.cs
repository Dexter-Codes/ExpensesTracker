using Expenses.Functions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Expenses.Tests;
public class EmailTests
{
    [Fact] public async Task DryRunDoesNotRequireAzureCredentials()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Email:DryRun"] = "true" }).Build();
        await new SendEmail(config, NullLogger<SendEmail>.Instance).Run(new(Guid.NewGuid(), "person@example.com", "Expense updated", "Updated"));
    }
    [Fact] public async Task InvalidMessageIsRejectedForQueueRetry()
    {
        var function = new SendEmail(new ConfigurationBuilder().Build(), NullLogger<SendEmail>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => function.Run(new(Guid.Empty, "", "", "")));
    }
}
