using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Expenses.Functions;

public record EmailNotification(Guid Id, string Recipient, string Subject, string Body);

public class SendEmail(IConfiguration configuration, ILogger<SendEmail> logger)
{
    [Function("SendEmail")]
    public async Task Run([QueueTrigger("expense-emails", Connection = "EmailQueue")] EmailNotification notification)
    {
        if (notification.Id == Guid.Empty || string.IsNullOrWhiteSpace(notification.Recipient) ||
            string.IsNullOrWhiteSpace(notification.Subject) || string.IsNullOrWhiteSpace(notification.Body))
            throw new ArgumentException("Incomplete email notification.");
        // Explicit dry-run mode lets the whole queue flow be tested without sending mail.
        if (configuration.GetValue<bool>("Email:DryRun"))
        {
            logger.LogInformation("Email {NotificationId} processed in dry-run mode; no email sent.", notification.Id);
            return;
        }
        var endpoint = configuration["Email:Endpoint"] ?? throw new InvalidOperationException("Configure Email:Endpoint.");
        var sender = configuration["Email:Sender"] ?? throw new InvalidOperationException("Configure Email:Sender.");
        var client = new EmailClient(new Uri(endpoint), new DefaultAzureCredential());
        var message = new EmailMessage(sender, notification.Recipient, new EmailContent(notification.Subject)
        {
            Html = notification.Body
        });
        await client.SendAsync(WaitUntil.Completed, message);
        logger.LogInformation("Email {NotificationId} sent.", notification.Id);
    }
}
