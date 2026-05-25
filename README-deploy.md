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
  Embeddings__ModelPath="/app/models/multilingual-e5-small/model.onnx" \
  --app apartment-triage
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
| `TelegramBot__Token` | Telegram Bot token |
| `Embeddings__ModelPath` | Path to ONNX model inside container |

## Database migrations

Run migrations against Neon **before** deploying a new version:

```bash
cd src/ApartmentTriage.Infrastructure
dotnet ef database update \
  --connection "<neon-postgres-connection-string>"
```

## Health check

```
GET https://apartment-triage.fly.dev/health
→ { "status": "healthy", "timestamp": "..." }
```

Fly.io polls `/health` every 30 s (see `fly.toml` `[[checks]]`).

## Hangfire dashboard

Available at `/hangfire`. Auth: Day 14 scope (C1).
