# Chubb APAC Claims Platform

A claims platform for motor and property claims across six APAC markets (KR, HK, TW, TH, MY, SG).
Claimants report and track claims; claims staff pick up, assess and settle them; managers see
workload and outstanding liability exposure.

> Work in progress. See [`docs/ACCEPTANCE_CRITERIA.md`](docs/ACCEPTANCE_CRITERIA.md) for the
> definition of done and [`docs/AI_JOURNAL.md`](docs/AI_JOURNAL.md) for how AI was used.

## Layout

```
backend/
  src/Claims.Api        REST API: claim lifecycle, owns the claims database, publishes events (outbox -> Kafka)
  src/Insights.Api      Kafka consumer: builds exposure/workload read models, serves dashboard endpoints
  src/Claims.Contracts  The event contract shared by both services (the only thing they share)
  tests/                xUnit unit and integration tests (Testcontainers Postgres)
infra/                  Postgres init script
docker-compose.yml      Postgres + Kafka (KRaft), plus the services
```

## Running locally

Prerequisites: Docker, .NET 10 SDK.

```bash
docker compose up -d     # infrastructure
```

(Full one-command start-up is added as the services land.)
