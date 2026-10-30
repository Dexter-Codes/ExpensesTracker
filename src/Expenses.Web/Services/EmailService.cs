using Azure.Identity;
using Azure.Storage.Queues;
using Expenses.Web.Data;
using Expenses.Web.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Web.Services;

public class EmailService(ApplicationDbContext db) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        db.EmailNotifications.Add(new EmailNotification { Recipient = email, Subject = subject, Body = htmlMessage });
        await db.SaveChangesAsync();
    }
}

public class EmailDispatcher(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUri = configuration["Notifications:QueueUri"];
        var storageConnection = configuration["Notifications:StorageConnection"];
        // Local development retains emails in the database for the development inbox.
        if (string.IsNullOrEmpty(queueUri) && string.IsNullOrEmpty(storageConnection)) return;
        var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        var queue = !string.IsNullOrEmpty(queueUri)
            ? new QueueClient(new Uri(queueUri), new DefaultAzureCredential(), options)
            : new QueueClient(storageConnection, "expense-emails", options);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Dispatch(queue, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Email queue dispatch failed; pending emails will be retried."); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task Dispatch(QueueClient queue, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var pending = await db.EmailNotifications.AsNoTracking()
            .Where(x => x.QueuedAt == null && (x.LeaseUntil == null || x.LeaseUntil < now))
            .OrderBy(x => x.CreatedAt).Take(20).ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            // An atomic claim allows multiple App Service instances to share this outbox.
            var claimed = await db.EmailNotifications.Where(x => x.Id == notification.Id && x.QueuedAt == null &&
                (x.LeaseUntil == null || x.LeaseUntil < now)).ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.LeaseUntil, now.AddMinutes(5)), cancellationToken);
            if (claimed == 0) continue;
            await queue.SendMessageAsync(BinaryData.FromObjectAsJson(new
            {
                notification.Id, notification.Recipient, notification.Subject, notification.Body
            }).ToString(), cancellationToken: cancellationToken);
            await db.EmailNotifications.Where(x => x.Id == notification.Id).ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.QueuedAt, DateTime.UtcNow), cancellationToken);
        }
    }
}
