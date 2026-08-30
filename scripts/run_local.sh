#!/usr/bin/env bash
# Runs RabbitMQ in Docker but everything else via `dotnet run` locally --
# faster edit-run loop than a full `docker compose up --build` while
# iterating on the .NET code. Run each service in its own terminal after
# starting RabbitMQ, in this order (Catalog before Ordering -- Ordering's
# cache-miss fallback needs Catalog reachable on first use):
set -euo pipefail
cd "$(dirname "$0")/.."

echo "--- starting RabbitMQ ---"
docker compose up -d rabbitmq
echo "Waiting for RabbitMQ..."
sleep 5

cat <<'EOF'

RabbitMQ is up (management UI: http://localhost:15672, guest/guest).
Now, in separate terminals:

  dotnet run --project src/Catalog.API             # listens on :5081 (see launchSettings / ASPNETCORE_URLS)
  dotnet run --project src/Ordering.API             # listens on :5091
  dotnet run --project src/Notifications.Worker
  dotnet run --project src/ApiGateway               # listens on :5000, fronts both APIs

Then try it end to end:

  curl http://localhost:5000/catalog/products
  curl -X POST http://localhost:5000/orders \
       -H 'Content-Type: application/json' \
       -d '{"productId": 3, "quantity": 2}'

Watch Notifications.Worker's console for the "[order confirmation]" log
line -- that's OrderPlacedIntegrationEvent making it across the bus.
EOF
