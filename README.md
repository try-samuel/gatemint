# GateMint

**From booking to the gate.** GateMint is an independent event ticketing and online check-in platform built as a .NET 10 modular monolith with a React interface. Organizers publish events and review sales, attendees reserve and buy test tickets, and assigned staff admit each signed QR ticket once.

## Start locally

Requirements: Docker/Compose. The setup script generates unique local database, demo, and ticket-signing secrets, then starts PostgreSQL, the API/web app, Mailpit, and a one-shot migration job.

```bash
chmod +x scripts/setup-local.sh
./scripts/setup-local.sh
```

Open <http://localhost:5080>. Mailpit is <http://localhost:8025>. The script prints the generated demo password; use it with:

- `organizer@gatemint.local`
- `attendee@gatemint.local`
- `staff@gatemint.local`

The checked-in defaults are deliberately safe: production selects Paystack, disables simulator and demo seeding, and contains no shared passwords or signing keys. Local simulator screens are clearly labelled and cannot run outside Development plus explicit enablement.

## Development

```bash
dotnet tool restore
dotnet restore GateMint.slnx
dotnet build GateMint.slnx
dotnet test GateMint.slnx
npm ci --prefix src/GateMint.Web
npm run dev --prefix src/GateMint.Web
```

Run the API separately with the environment variables from `.env.example` and a PostgreSQL instance:

```bash
dotnet run --project src/GateMint.Api --urls http://localhost:5080
```

Vite proxies `/api` and `/health` to port 5080. Production builds the React client into the API's `wwwroot` so cookies and APIs share one origin. Swagger UI is available at `/swagger` in Development.

## Verification

```bash
dotnet build GateMint.slnx --no-restore
dotnet test GateMint.slnx --no-restore
npm run lint --prefix src/GateMint.Web
npm run build --prefix src/GateMint.Web
docker compose config
docker build -t gatemint:local .
npm ci --prefix tests/GateMint.E2E
npm exec --prefix tests/GateMint.E2E -- playwright install chromium --with-deps
GATEMINT_DEMO_PASSWORD='<password printed by setup>' npm test --prefix tests/GateMint.E2E
```

PostgreSQL integration tests use Testcontainers and require a running Docker daemon. They create real competing transactions; no EF in-memory provider is used.
The Playwright scenario expects the Compose stack and exercises organizer publication, attendee simulator payment, ticket issuance, staff admission, and duplicate-scan rejection.

## Repository map

- `src/GateMint.Api` — controllers, cookies/antiforgery, rate limits, health, OpenAPI, static client hosting.
- `src/GateMint.Core` — domain state, DTOs, and payment/ticket ports.
- `src/GateMint.Infrastructure` — EF Core, checked-in migration, row-locked workflows, Paystack/simulator, QR, SMTP, durable workers.
- `src/GateMint.Web` — React, TypeScript, Router, TanStack Query, Tailwind-powered responsive UI.
- `tests/GateMint.UnitTests` — signature and QR security tests.
- `tests/GateMint.IntegrationTests` — PostgreSQL inventory, idempotency, payment, refund, and check-in concurrency.
- `tests/GateMint.E2E` — Playwright purchase-to-entry flow.
- `docs` — [architecture](docs/architecture.md), [status](docs/implementation-status.md), [deployment](docs/deployment.md), and [demo](docs/demo-walkthrough.md).

## Important behavior

- Amounts are integer kobo and recomputed server-side.
- Holds last ten minutes and use `SELECT … FOR UPDATE` plus database constraints.
- Payment redirects never confirm orders; both returns and webhooks use server verification.
- Late captured payments issue no tickets and enter an organizer refund queue.
- QR values contain no contact details and are signed with an environment-held key.
- Organizer, attendee, and event-staff access is scoped to each resource.

See [docs/implementation-status.md](docs/implementation-status.md) for completion evidence and explicit first-release limitations.
