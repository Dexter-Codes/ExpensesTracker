# Expenses: first version

A single ASP.NET Core Razor Pages app hosts the UI, account pages and JSON API. An Azure Functions project processes email queue messages. No separate SPA, repository wrappers, mediator, or microservice framework.

Confirmed scope:
- Email/password, Google and Microsoft sign-in. Email verification is required. External providers appear when configured.
- Groups, INR only, one payer per expense, selected participants, equal or custom amount splits.
- Expenses can be added, edited or deleted by their creator or the group owner.
- Every member can invite others. Invitations expire in seven days and require the recipient's verified email. The invitation destination is carried through registration, email confirmation and sign-in; the user returns to an explicit acceptance page without reopening the email.
- Group balances are netted across expenses. Suggested payments can differ from the original individual debts; amounts are never combined between groups.
- Members record money they have already paid. A payment is recorded by its sender, must go to someone with a positive group balance, and cannot exceed either outstanding balance. No payment processing or recipient confirmation in this version.
- Invitation emails, expense changes and settlements. Expense notifications go to the old/new participants and payers, excluding the actor. Settlement notifications go to the recipient.
- Integer paise, deterministic equal-split remainder distribution. Fifty-member group limit.

Implementation details:
- ASP.NET Core Identity handles password hashing, verification, reset links and external account association. Never automatically link accounts purely because email strings match.
- Sign-in and registration refresh their antiforgery token before submitting, so account changes in another tab do not leave the form using a stale token. Account pages and token responses are not cached.
- Cookie-authenticated API changes require an antiforgery token. Valid bearer tokens are supplied explicitly through the Authorization header and do not require browser CSRF cookies. Group membership is checked server-side for both; clients cannot select a different actor.
- Optional API tokens are issued only from a cookie-authenticated, CSRF-protected page after checking the current account security stamp. HS256 tokens expire in 15 minutes and are validated for signature, algorithm, issuer, audience, expiry, scope and current account state. Password/security-stamp changes invalidate them immediately; signing out alone does not. There are no refresh tokens. The signing key is a separate random Key Vault secret, not an OAuth client secret. This intentionally small lab issuer is not an OAuth/OIDC authorization server; use an established identity provider for a public integration platform.
- Each group has an optimistic concurrency version. Conflicting writes return HTTP 409; refresh before retrying. UI buttons are disabled while saving.
- The same database transaction stores changes and email outbox rows. A background dispatcher claims rows and publishes to Azure Storage Queues. Queue messages are Base64 JSON, then the Function sends through Azure Communication Services Email using managed identity.
- Delivery is at least once: a crash between publishing/sending and acknowledgement can produce duplicates. Exactly-once email delivery is not claimed. Failed queue messages are retried, then moved to `expense-emails-poison`; monitor and inspect them manually.
- No Azure credentials in code. Key Vault references supply OAuth client secrets. Azure SQL and queue connections use managed identities.
- SQLite is local only. Azure uses SQL Server via the same EF model. Initial schema creation is an explicit command (`--init-db`); this is not a migration system. Add proper migrations before upgrading a deployed database with existing data.

Not yet included: multiple currencies, attachments, member removal, recurring bills, payment integrations, notification preferences, a full audit trail, editable settlements, or a full OAuth/OIDC authorization server. This is a learning application, not a drop-in production Splitwise replacement.
