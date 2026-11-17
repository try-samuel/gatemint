# Architecture and state model

GateMint is a modular monolith: one ASP.NET Core deployment, one PostgreSQL database, and a same-origin React application. `GateMint.Core` owns domain records and ports, `GateMint.Infrastructure` owns EF Core and external adapters, and `GateMint.Api` owns HTTP/auth composition. There is no generic repository layer; workflows use EF transactions directly.

## Inventory

Checkout starts a `READ COMMITTED` transaction and locks the selected `TicketTypes` row with `FOR UPDATE`. It validates both event and tier sales windows against the injected UTC clock, then increments `ReservedCount` and creates the order, immutable item snapshot, and reservation atomically. A database check enforces:

`SoldCount >= 0; ReservedCount >= 0; SoldCount + ReservedCount <= Capacity`

The `(AttendeeId, IdempotencyKey)` unique index is the final arbiter for concurrent retries. A matching fingerprint replays the original order; different parameters return a conflict. Expiry, cancellation, and fulfillment lock the same inventory row and transition a held reservation exactly once.

## Payment and fulfillment

`AwaitingPayment → Confirmed` only follows server-side gateway verification of reference, test domain, amount, currency, and order association. An active hold becomes consumed, reserved inventory becomes sold inventory, unit tickets are inserted, and notification work is written to the outbox in the same transaction.

If verified money arrives after release, the order becomes `NeedsRefund`; no ticket is issued. Refund requests are unique per payment attempt and remain `RefundPending` until reconciliation confirms them. Simulator refunds are confirmed by the reconciliation worker. Real Paystack initiation/verification calls are separated behind `IPaymentGateway`.

Webhook requests are validated over the original bytes, durably deduplicated in `WebhookInbox`, acknowledged, and processed by a restart-safe worker. The current `Paystack-Signature` format enforces a five-minute timestamp window and constant-time HMAC-SHA512 comparison. The deprecated `X-Paystack-Signature` is supported only for Paystack's documented transition period.

## Tickets and access

QR payloads contain only ticket ID, event ID, random nonce, and key ID. HMAC-SHA256 authenticates the opaque payload; no attendee details are included. Production signing keys come from the environment and rotation uses the embedded key identifier. Gate validation requires both a valid code and a persisted event/staff assignment. A row lock plus unique `CheckIns.TicketId` constraint admits once.

## Security boundaries

- Identity cookies are HttpOnly and SameSite; no tokens enter browser storage.
- Authenticated mutations use ASP.NET antiforgery tokens and origin cookies.
- Organizer and attendee queries include resource ownership predicates.
- Event staff can call only assigned event check-in routes; dashboards remain organizer-only.
- Simulator and demo seed both require Development plus explicit configuration. Production defaults select Paystack, disable simulator/seed, and require injected secrets.
- Auth, checkout, and scan routes have independent rate limits.
