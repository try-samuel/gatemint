#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f .env ]]; then
  db_password="$(openssl rand -hex 18)"
  demo_password="GateMint-$(openssl rand -hex 8)!"
  ticket_key="$(openssl rand -base64 32 | tr -d '\n')"
  {
    printf 'POSTGRES_PASSWORD=%s\n' "${db_password}"
    printf 'DEMO_PASSWORD=%s\n' "${demo_password}"
    printf 'TICKET_SIGNING_KEY=%s\n' "${ticket_key}"
    printf 'PAYMENTS_PROVIDER=Simulator\n'
    printf 'PAYSTACK_SECRET_KEY=\n'
    printf 'PAYSTACK_WEBHOOK_SECRET=\n'
  } > .env
  printf 'Created .env with generated local secrets.\nDemo users share this local password: %s\n' "${demo_password}"
fi
docker compose up --build
