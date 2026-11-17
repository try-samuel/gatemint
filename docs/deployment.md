# Deployment guide

## Container deployment

Build `Dockerfile` and run the image behind an HTTPS reverse proxy. Supply:

- `ConnectionStrings__Default` for managed PostgreSQL with TLS.
- `Tickets__SigningKey` as 32+ random bytes in base64. Keep prior keys during rotation and extend `QrTicketService`'s key ring before changing `kid`.
- `DataProtection__KeysPath` on encrypted persistent storage shared by all API replicas.
- SMTP host, port, TLS, sender, and credentials.
- Paystack **test** secret and webhook-signing secret. Set `Payments__Provider=Paystack`; never use a live key for the initial deployment.
- `ASPNETCORE_ENVIRONMENT=Production`, `Demo__Seed=false`, and `Payments__AllowSimulator=false`.

Run migration as a separate release job before promoting application replicas:

```bash
docker run --rm --env-file production.env gatemint:release --migrate
```

Do not let every replica migrate on startup. Route `/health/live` for process health and `/health/ready` for database readiness. Configure the public Paystack webhook as `POST /api/v1/payments/paystack/webhook` and verify test events before promotion.

## Operations

- Back up PostgreSQL daily with point-in-time recovery; test restores quarterly.
- Alert on readiness failures, webhook/outbox retry growth, `NeedsRefund` orders, and payment initialization timeouts.
- Keep structured logs free of secrets, raw webhook bodies, and QR values.
- Scale API replicas only with the shared Data Protection key volume.

## Rollback

Deploy the prior immutable image. Database migrations are forward-compatible additions for this release; do not automatically run destructive down migrations. If a future migration is incompatible, restore to a new database from the pre-deploy backup and switch the connection after validation. Payment and audit rows must never be discarded during rollback.

## External configuration still required

A production-like environment needs PostgreSQL, HTTPS/DNS, persistent key storage, SMTP credentials, a Paystack test account, and a publicly reachable webhook URL. No paid infrastructure or live payment capability is provisioned by this repository.
