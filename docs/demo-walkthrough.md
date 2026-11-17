# Five-minute demo

1. Run `./scripts/setup-local.sh`, note the generated demo password, and open <http://localhost:5080>. Mailpit is at <http://localhost:8025>.
2. Sign in as `organizer@gatemint.local`. Open **Organize**, review the seeded event, create a draft, and use **Add tier & publish**. Sign out.
3. Sign in as `attendee@gatemint.local`, open **Lagos Night Market**, reserve 1–6 passes, and choose **Pay securely**.
4. The labelled local simulator shows that no real charge occurs. Approve the test payment, return to **My tickets**, and open the issued pass. Mailpit shows the durable notification.
5. Copy the event ID from the organizer screen or API, sign in as `staff@gatemint.local`, open **Scan**, paste the pass's manual code, and validate. Repeat it to see the original admission reported as already used.
6. To demonstrate late payment, reserve another pass, move the injected clock in the integration test past ten minutes, and run `LateSuccessfulPayment_GoesToRefundWithoutTickets`; it proves the order enters the exception workflow without issuing inventory.

The simulator route is unavailable unless both the environment is Development and `Payments__AllowSimulator=true`.
