# Deployment — Fly.io

## Prerequisites

```bash
brew install flyctl
fly auth login
```

## First deploy

```bash
fly launch --no-deploy   # import fly.toml, skip auto-deploy
fly secrets set ...      # see Secrets section below
fly deploy
```

## Secrets

Set all secrets before the first deploy. Replace `<value>` with the real value.
**Never commit secret values to git.**

```bash
fly secrets set \
  ConnectionStrings__DefaultConnection="<neon-postgres-connection-string>" \
  Anthropic__ApiKey="<anthropic-api-key>" \
  WhatsApp__PhoneNumberId="<meta-phone-number-id>" \
  WhatsApp__WabaId="<meta-waba-id>" \
  WhatsApp__AccessToken="<meta-access-token>" \
  WhatsApp__WebhookVerifyToken="<your-webhook-verify-token>" \
  WhatsApp__AppSecret="<meta-app-secret>" \
  TelegramBot__Token="<telegram-bot-token>" \
  Auth__BootstrapManagerIdentifier="<your-telegram-user-id>" \
  Auth__BootstrapManagerPhone="+905550001234" \
  Embeddings__ModelPath="/app/models/multilingual-e5-small/model.onnx" \
  --app hanwas-ai
```

> `Embeddings__ModelPath` is set to the path where `scripts/download-models.sh`
> downloads the ONNX model inside the Docker image (baked at build time — ADR-0008).

### Secret keys reference

| Key | Description |
|-----|-------------|
| `ConnectionStrings__DefaultConnection` | Neon PostgreSQL connection string |
| `Anthropic__ApiKey` | Anthropic API key |
| `WhatsApp__PhoneNumberId` | Meta Phone Number ID |
| `WhatsApp__WabaId` | Meta WhatsApp Business Account ID |
| `WhatsApp__AccessToken` | Meta permanent access token |
| `WhatsApp__WebhookVerifyToken` | Webhook verification token (self-chosen) |
| `WhatsApp__AppSecret` | Meta App Secret (HMAC signature verification) |
| `TelegramBot__Token` | Telegram Bot token (test phase: **Uygulama Test Bot** `@apartman_triage_bot`) |
| `Auth__BootstrapManagerIdentifier` | Your Telegram numeric user ID — promoted to Manager on boot |
| `Auth__BootstrapManagerPhone` | Manager WhatsApp E.164 (e.g. `+905550001234`) — links the WhatsApp dashboard row to your Telegram ID when phone merge left split records |
| `Embeddings__ModelPath` | Path to ONNX model inside container |

> **Test bot auth:** `TelegramBot__Token` must match the bot users message (`@apartman_triage_bot` during test).
> `TelegramBot:Username` in `appsettings.json` drives the login page hint (default `apartman_triage_bot`).
> Set both bootstrap secrets, redeploy. `/login` also creates a Telegram resident row if needed. If Manager was assigned only on the WhatsApp resident, `Auth__BootstrapManagerPhone` is required.

## Canonical domain

Public URL: **https://hanwas.digital**. Requests to `hanwas-ai.fly.dev` receive a **308** redirect to the same path on `hanwas.digital` (configured in `Hosting` section of `appsettings.json`).

## Database migrations

Run migrations against Neon **before** deploying a new version:

```bash
cd src/ApartmentTriage.Infrastructure
dotnet ef database update \
  --connection "<neon-postgres-connection-string>"
```

## Health check

```
GET https://hanwas.digital/health
→ { "status": "healthy", "timestamp": "..." }
```

Fly.io polls `/health` every 30 s (see `fly.toml` `[[checks]]`).

## Hangfire dashboard

Available at `/hangfire`. Auth: Day 14 scope (C1).
