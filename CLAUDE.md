# CLAUDE.md — DePix BTCPay Plugin

## Project Overview

BTCPay Server plugin that adds **Pix** (Brazilian instant payments) as a payment method. Customers pay via Pix, and merchants receive funds in **DePix** — a Liquid Network BRL stablecoin (1 DEPIX = 1 BRL).

**This is the BTCPay plugin repo.** It calls the DePix API to create Pix checkouts and receives webhook callbacks when payments complete.

### Related repos

| Repo | Description |
|------|-------------|
| `dadafros/depix-backend` | DePix API (Vercel serverless) — the upstream API this plugin calls |
| `dadafros/depix-frontend` | DePix App PWA (vanilla JS, GitHub Pages) |
| `dadafros/depix-dev` | Local dev environment (Docker) for frontend + backend |

## Code Language

All new code must be written in English — variable names, function names, comments, error messages.

## Architecture

- **Runtime**: C# / .NET 10.0, ASP.NET Core
- **Framework**: BTCPay Server plugin system (`BaseBTCPayServerPlugin`)
- **Blockchain**: Liquid Network (Elements sidechain) for DePix asset settlement
- **External API**: DePix API at `https://depix-backend.vercel.app/api/`
- **Tests**: Playwright UI tests (xUnit + Microsoft.Playwright)
- **CI**: GitHub Actions — builds + runs Playwright tests on push/PR to master
- **BTCPay dependency**: >= 2.3.7

### DePix API integration

- `POST /api/checkouts` — create a Pix checkout (amount, description, callback_url)
- `GET /api/me` — validate API key (returns merchant info)
- Webhooks: HMAC-SHA256 signed via `X-DePix-Signature: t={timestamp},v1={hmac}`

### Configuration hierarchy

1. **Store-level config** (API key + webhook secret) takes precedence
2. **Server-level config** is used as fallback for stores without their own config
3. Both API key and webhook secret are stored encrypted via `ISecretProtector` (Data Protection API)

## File Structure

```
BTCPayServer.Plugins.Depix/
├── DePixPlugin.cs                          # Plugin entry point, DI registration, Liquid asset setup
├── Controllers/
│   ├── PixController.cs                    # Store settings + transactions UI
│   ├── PixServerSettingsController.cs      # Server-level settings UI
│   └── DepixWebhookController.cs           # Webhook endpoints (HMAC validation, payload dispatch)
├── PaymentHandlers/
│   ├── PixPaymentMethodHandler.cs          # IPaymentMethodHandler — creates checkouts, records payments
│   ├── PixPaymentMethodConfig.cs           # Store config model (EncryptedApiKey, EncryptedWebhookSecret, IsEnabled)
│   ├── PixCheckoutModelExtension.cs        # Checkout page customization (QR code, copy-paste)
│   └── PixTransactionLinkProvider.cs       # Transaction link formatting
├── Services/
│   ├── DepixService.cs                     # Core service — API calls, webhook processing, config resolution
│   ├── Utils.cs                            # HMAC-SHA256 validation, webhook URL builder
│   ├── ISecretProtector.cs                 # Interface for encrypt/decrypt secrets
│   └── SecretProtector.cs                  # Data Protection API implementation
├── Data/
│   ├── Enums/DePixPaymentStatus.cs         # Checkout statuses: Pending, Processing, Completed, Expired, Cancelled
│   ├── Models/
│   │   ├── DepositWebhookBody.cs           # DepixWebhookPayload, DepixWebhookData, DepixWebhookMetadata
│   │   ├── DepixDepositResponse.cs         # DepixCheckoutResponse (Id, PaymentUrl, PixPayload, ExpiresAt)
│   │   ├── PixServerConfig.cs              # Server-level config model
│   │   └── ViewModels/                     # Razor view models
│   └── DepixDbContext.cs                   # EF Core context (currently minimal)
├── Views/
│   ├── Pix/PixSettings.cshtml              # Store settings page
│   ├── Pix/PixTransactions.cshtml          # Transaction list page
│   ├── PixServerSettings/PixServerSettings.cshtml  # Server settings page
│   └── Shared/PixCheckout.cshtml           # Checkout component (QR code + Pix copy-paste)
├── Resources/img/                          # DePix icon assets
└── Errors/                                 # Custom exception types

BTCPayServer.Plugins.Depix.Tests/
├── PlaywrightBaseTest.cs                   # Base class — server setup, config seeding, navigation helpers
├── PixSettingsTests.cs                     # Store settings UI tests
├── PixServerSettingsTests.cs               # Server settings UI tests
├── DepixPlaywrightTester.cs                # Playwright browser context wrapper
└── SharedPluginTestFixture.cs              # Shared test fixture (single server instance)

submodules/btcpayserver/                    # BTCPay Server (git submodule, pinned version)
```

## Key Patterns

### Payment flow

1. Customer creates invoice in BTCPay
2. `PixPaymentMethodHandler.ConfigurePrompt` calls `POST /api/checkouts` to create a Pix checkout
3. Checkout page shows QR code (generated client-side from `pix_payload`) and copy-paste field
4. Customer pays via Pix
5. DePix API sends webhook to `/depix/webhooks/deposit/{storeId}`
6. `DepixWebhookController` validates HMAC-SHA256 signature, dispatches to `DepixService.ProcessWebhookAsync`
7. On `Completed` status: records payment on the invoice, settles to DePix wallet

### Webhook HMAC validation (`Utils.ValidateHmacSignature`)

Header format: `X-DePix-Signature: t={timestamp},v1={hmac}`
Signed payload: `"{timestamp}.{body}"` with HMAC-SHA256 using the webhook secret.
Comparison uses `CryptographicOperations.FixedTimeEquals` (timing-safe).

### Secret storage

API keys and webhook secrets are encrypted (not hashed) via `ISecretProtector` (ASP.NET Data Protection API). This is necessary because HMAC-SHA256 requires the raw secret for signing — unlike the old Basic auth approach which could compare hashes.

### Tests (Playwright UI)

Tests use reflection to access the plugin assembly loaded at runtime by BTCPay Server (the plugin loads in a separate assembly context). See `PlaywrightBaseTest.GetPluginRuntimeAssembly()` and `ProtectSecret()`.

## Commands

```bash
dotnet build                                                    # Build everything
dotnet build BTCPayServer.Plugins.Depix/                       # Build plugin only
dotnet test BTCPayServer.Plugins.Depix.Tests/ --filter "Category=PlaywrightUITest"  # Run Playwright tests
```

Note: `dotnet` is not installed on the local dev machine. Use CI (GitHub Actions) for build verification, or install .NET 10.0 SDK locally.

## Releasing a new version

1. **Bump the version** in `BTCPayServer.Plugins.Depix/BTCPayServer.Plugins.Depix.csproj`:
   ```xml
   <Version>1.0.6</Version>
   ```

2. **Commit and push to master**:
   ```bash
   git add BTCPayServer.Plugins.Depix/BTCPayServer.Plugins.Depix.csproj
   git commit -m "chore: bump version to 1.0.6"
   git push git@github-personal:dadafros/depix-btcpay.git HEAD:master
   ```

3. **Tag the release** (must be on the correct commit after push):
   ```bash
   git tag v1.0.6
   git push git@github-personal:dadafros/depix-btcpay.git v1.0.6
   ```

4. **CI does the rest**: `.github/workflows/release.yml` triggers on `v*` tags, builds the plugin with `dotnet publish`, packages it as `BTCPayServer.Plugins.Depix.btcpay`, and creates a GitHub Release at `github.com/dadafros/depix-btcpay/releases/tag/v1.0.6` with the `.btcpay` file attached.

If the tag needs to be moved after a fix (e.g. CI failed and you pushed another commit):
```bash
git tag -d v1.0.6
git push git@github-personal:dadafros/depix-btcpay.git :refs/tags/v1.0.6
git tag v1.0.6
git push git@github-personal:dadafros/depix-btcpay.git v1.0.6
```

## Git

- Remote: `git@github-personal:dadafros/depix-btcpay.git`
- SSH key alias `github-personal` maps to `~/.ssh/id_ed25519_outlook`
- Commit as: `dadafros <davi_bf@outlook.com>` — set via `git config user.name "dadafros"` and `git config user.email "davi_bf@outlook.com"`
- **IMPORTANT**: The machine's global git config uses a different identity (`davifros`). Always verify the local repo config is set correctly before committing.
- Push command: `git push git@github-personal:dadafros/depix-btcpay.git <branch>:<target>`
- Main branch: `master`
- Branch naming: `feat/*` for features
- CI: GitHub Actions runs Playwright UI tests on push to `master` and on PRs to `master`
- **This is a fork of `thgO-O/btcpayserver-plugin-depix`. NEVER open PRs or push to the upstream repo. All work stays on `dadafros/depix-btcpay` only.**

### Push examples

```bash
# Push current branch to master
git push git@github-personal:dadafros/depix-btcpay.git HEAD:master

# Push a feature branch
git push git@github-personal:dadafros/depix-btcpay.git feat/my-feature

# HTTPS origin will NOT work (permission denied) — always use SSH with github-personal
```

## Workflow Rules

- **Always start from latest master**: Before starting any task, pull the latest `master` from remote.
- **Default for simple or urgent fixes**: Small fixes and hotfixes should be committed and pushed directly to `master`.
- **Use PRs for large or complex work**: Large refactors or high-risk changes should go on a separate branch and be opened as a PR for review.
- **User instruction wins**: If the user explicitly asks for a different flow, follow the user's instruction.
- **Sync before branching**: If the work should go through a PR, always sync with `master` first before creating or updating the branch.
