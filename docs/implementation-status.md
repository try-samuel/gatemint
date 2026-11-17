# Implementation status

Updated 26 October 2026 (repository history date).

## Milestone evidence

- [x] Foundation — .NET 10 solution, React/Vite/Tailwind client, PostgreSQL EF migration, multi-stage container, Compose stack, health checks, Mailpit, CI.
- [x] Identity and events — secure cookie Identity, antiforgery, registration/login/reset request, organizer-owned event creation, tier creation, publish/unpublish, staff assignment, public discovery/detail.
- [x] Reservations — row-locked PostgreSQL holds, ten-minute expiry, server-side money calculation, quantity bounds, request fingerprints, attendee-scoped idempotency keys, release worker.
- [x] Payments — development-only simulator, Paystack hosted-checkout adapter, current timestamped webhook signature validation plus transition header, durable inbox/outbox, common verification/fulfillment path, late-payment exception and refund workflow.
- [x] Tickets and gate — one signed opaque QR per unit, owner-only pass/print view, event-scoped staff validation, atomic duplicate protection, camera permission on action and manual entry.
- [x] Product completion — organizer metrics/attendees, order polling, exception API, responsive empty/error/loading states, keyboard focus and reduced-motion support.
- [x] Verification and delivery — unit and PostgreSQL concurrency suites, checked-in migration, container build, deployment and demo documentation.

## Automated evidence

- `dotnet build GateMint.slnx`: succeeds.
- `dotnet test GateMint.slnx`: 15 tests pass (6 unit, 9 PostgreSQL integration).
- Integration evidence includes 50 competing buyers for 10 units, concurrent idempotent requests, mismatched payment rejection, duplicate fulfillment, late payment, staff scoping, inactive tickets, and simultaneous scans.
- `npm run lint` and `npm run build`: pass.
- Compose build, one-shot migration, PostgreSQL/API/Mailpit health smoke, and both health endpoints: pass.
- Playwright purchase-to-entry scenario: 1 pass in Chromium, including duplicate-scan rejection.

## Deliberate first-release limits

- General admission, NGN, one tier per order, quantity 1–6, online scanning only.
- No reserved seating, payouts, transfers/resale, native app, affiliate system, or bulk event cancellation.
- Password-reset delivery is privacy-safe but requires a production reset-link mail template before public launch.
- Paystack live mode remains disabled by local defaults and requires external test credentials and a public webhook URL.
