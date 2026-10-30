# API gateway exercise

The JSON API is part of the web app. The browser uses its Identity cookie; API clients can optionally use short-lived bearer tokens after the signing key is configured (see below). Sign into the web app first. `GET /api/session` returns an antiforgery token and sets its matching cookie. For cookie-authenticated POST, PUT and DELETE, send the session cookie, antiforgery cookie and `X-CSRF-TOKEN` header. Valid bearer requests do not need CSRF cookies. Monetary values are integer paise, not rupees. Every group change includes its current `version`; stale requests receive 409.

| Method | Path | Purpose |
|---|---|---|
| GET | /health | Process health (does not test database or email delivery) |
| GET | /api/session | Current user and CSRF token |
| GET | /api/groups | Your groups |
| POST | /api/groups | Create `{ "name": "Home" }` |
| GET | /api/groups/{id} | Members, expenses, balances, debts, settlements and version |
| POST | /api/groups/{id}/invitations | Invite `{ "email": "friend@example.com", "version": 0 }` |
| POST | /api/groups/{id}/expenses | Add an expense |
| PUT | /api/groups/{id}/expenses/{expenseId} | Update an expense |
| DELETE | /api/groups/{id}/expenses/{expenseId}?version=0 | Delete an expense |
| POST | /api/groups/{id}/settlements | Record `{ "toId": "member-id", "amount": 5000, "version": 1 }` |

Expense example (replace IDs and version with values from your own group):

```json
{
  "description": "Dinner",
  "paidById": "member-id",
  "amount": 10001,
  "date": "2026-10-29",
  "participants": ["member-id", "other-member-id"],
  "customShares": null,
  "version": 1
}
```

For custom splitting, set `customShares` to an object keyed by the exact participant IDs, with paise values adding up to `amount`. Use the current expense date rather than copying the example indefinitely.

## Configure APIM yourself

1. Create API Management Consumption if your subscription/region supports it.
2. Add an HTTP API with backend URL `https://YOUR-APP.azurewebsites.net`, API URL suffix `expenses`.
3. Add a GET operation `/health`. Require a subscription and create a test subscription.
4. Call the gateway `/expenses/health` with the subscription key. Omit it and observe rejection.
5. Add `set-header` with name `X-Expense-Gateway` and value `apim` in the outbound policy. Observe it on the response.
6. Add a subscription-based `rate-limit` inbound policy. Exceed it and observe HTTP 429. Consumption does not support `rate-limit-by-key`.
7. Add `/api/session`, `/api/groups` and the remaining operations as required. To exercise authenticated operations from an HTTP client, explicitly forward the signed-in app cookie and CSRF cookie/header. Never share these cookies or paste them into a public client.

The UI calls its own same-origin API directly. This exercise does not automatically route the UI through APIM. Putting the entire browser application behind a gateway needs deliberate host, cookie, redirect and CSRF configuration. Subscription keys alone do not identify an application user.

## Short-lived JWT exercise

This app issues lab tokens for existing expense users, including users who signed in with an external provider. It is not a general OAuth/OIDC authorization server. For public third-party integrations, use an established authorization server and its standard authorization-code/PKCE flow.

### Configure and deploy the web app yourself

1. Generate at least 32 random bytes as Base64, for example `openssl rand -base64 32`. Keep the result private and store it in Key Vault as **expenses-api-signing-key**. This must be a new random key, not your Microsoft client secret, a subscription key, or a password.
2. The web app identity already needs Key Vault Secrets User on the vault. Add these App Service settings:

   | Setting | Value |
   |---|---|
   | `ApiTokens__SigningKey` | `@Microsoft.KeyVault(SecretUri=https://YOUR-VAULT.vault.azure.net/secrets/expenses-api-signing-key)` |
   | `ApiTokens__Issuer` | `expenses` |
   | `ApiTokens__Audience` | `expenses-api` |

3. Publish and deploy the updated web app, save settings and restart. There is no database schema change or initialization step. Without a signing key, the existing cookie app keeps working and token issuance is disabled. An invalid or unresolved configured key fails startup with a configuration error; check the Key Vault reference status.
4. Sign into the app, open **API token** in the account navigation, and select **Create token**. Copy the token privately. The page is not cached and does not use browser storage. Tokens expire after 15 minutes, with 30 seconds of validation clock tolerance. Signing out does not revoke a token; a password/security-stamp change does. The backend checks that the account still exists, is confirmed and is not locked out on every bearer request.

### Validate tokens in APIM yourself

1. Enable the API Management resource's system-assigned identity. Give that identity **Key Vault Secrets User**, preferably scoped to the new signing secret.
2. In APIM **Named values**, create a Key Vault-backed named value whose display name is **expenses-api-signing-key**. Select the signing secret and the APIM managed identity. Do not paste the key into policy XML or source files. This symmetric-key lab trusts both the app and APIM with the same key; either holder can sign tokens. A larger system should use an identity provider with asymmetric signing and public-key discovery.
3. On each protected business operation (Get groups, Get group, Add expense, and any others added later), open the operation policy editor. Add the `validate-jwt` element from [apim-jwt-policy.xml](apim-jwt-policy.xml) immediately after the inbound `<base />`. Preserve existing limits and policies. Do not apply it to `/health` or `/api/session` for this exercise, and do not expose the browser token-issuance page through APIM. The XML file is a complete example for a business operation with no other operation-specific policies.
4. Test Get groups with a subscription key and `Authorization: Bearer YOUR-TOKEN`. Remove the old Cookie and X-CSRF-TOKEN headers. Expected: 200 with only that user's groups. Keep token values out of screenshots, traces and shared logs.
5. Repeat with no Authorization header, then a malformed token: APIM should return 401 before forwarding. Use Trace to inspect the failing policy without sharing credentials. A valid token with the wrong issuer/audience or an expired token must also fail. API Management validates the JWT, while the backend independently validates it and enforces account and group permissions.
6. Test Add expense with a valid bearer token, subscription key, JSON content type and the current group version. No Cookie or X-CSRF-TOKEN header is needed. Expect 204; repeating the old version should return 409, not create another expense.

Existing web UI requests remain cookie-authenticated and CSRF-protected. A request carrying an invalid Authorization header never falls back to a browser cookie. Direct App Service API calls still validate bearer tokens, but APIM subscription keys and policies apply only to gateway traffic. Backend access restrictions are a separate exercise; do not block the existing browser sign-in flow while practising.

Rotate the signing key deliberately: this single-key lab immediately invalidates outstanding tokens at the backend on restart/configuration reload. APIM Key Vault refresh can lag; manually refresh the named value after rotation. There is no multi-key rollover or refresh-token service.

References: [APIM policies](https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-policies), [rate-limit policy](https://learn.microsoft.com/en-us/azure/api-management/rate-limit-policy), [JWT validation](https://learn.microsoft.com/en-us/azure/api-management/validate-jwt-policy), [Key Vault-backed named values](https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-properties).
