# Claims Platform: Asia Pacific

A claims platform for motor and property claims across six APAC markets: South Korea, Hong Kong, Taiwan, Thailand, Malaysia and Singapore.

- **Claimants** report an incident, track their claim, answer questions from the claims team and receive decisions.
- **Claims handlers** pick up incoming claims, assess them, and take them through to settlement or rejection.
- **Managers** see team workload and outstanding liability exposure, per market currency and in USD.

| Doc | What's in it |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Services, REST vs Kafka, outbox and idempotency, domain model, trade-offs |
| [docs/ACCEPTANCE_CRITERIA.md](docs/ACCEPTANCE_CRITERIA.md) | Definition of done, with IDs referenced by commits and tests |
| [docs/DESIGN.md](docs/DESIGN.md) | Frontend design direction and tokens |
| [docs/AI_JOURNAL.md](docs/AI_JOURNAL.md) | What I asked the AI, what I accepted, challenged and overrode |

## Run it

**Prerequisites:** Docker Desktop.

```bash
docker compose up --build
```

| URL | What |
|---|---|
| http://localhost:4200 | The app. Pick a role on the welcome screen. |
| http://localhost:5001/swagger | Claims API (Swagger; use the Authorize box with a user id such as `handler-1`) |
| http://localhost:5002/swagger | Insights API |

On first start, the Claims service migrates its database and seeds 6 markets, 10 users and 12 demo claims spread across every status and age bucket. These flow through Kafka to Insights, so the dashboard is populated within seconds.

### Seeded users (dev identity, no passwords)

| Role | Users |
|---|---|
| Claimant | `claimant-sg` Mei Ling Tan · `claimant-kr` Kim Min-jun · `claimant-hk` · `claimant-tw` · `claimant-th` · `claimant-my` |
| Handler | `handler-1` Priya Nair · `handler-2` Daniel Wong · `handler-3` Siti Rahman |
| Manager | `manager-1` Grace Lim |

Switch users with the **DEV** selector in the header. Each request carries `X-User-Id`. This is a development stand-in, **not** authentication (see the architecture doc).

### A two-minute tour
1. **Claimant** (Mei Ling Tan): *Report a claim* → you get a `CLM-SG-…` reference.
2. **Handler** (Priya Nair): *Incoming* → **Pick up** → *Request information*.
3. **Claimant**: the claim now asks for information → reply.
4. **Handler**: *Adjust reserve* → *Approve* → *Settle and close*.
5. **Manager** (Grace Lim): watch exposure and workload update within a few seconds. "Updated X ago" shows how fresh the read model is.

### Resilience demos
- **Kafka down:** `docker compose stop kafka`, then submit a claim. It still succeeds, and the event waits in the outbox. `docker compose start kafka` and it's delivered.
- **Insights down:** `docker compose stop insights-api`. Claims keep working, and the dashboard shows a clear message. Start it again and it catches up from its Kafka offset.

## Run for development

**Prerequisites:** .NET 10 SDK, Node 22+, plus Postgres and Kafka from Docker or Homebrew.

```bash
docker compose up -d postgres kafka              # infrastructure only (or the Homebrew option below)
cd backend/src/Claims.Api   && dotnet run          # http://localhost:5001
cd backend/src/Insights.Api && dotnet run          # http://localhost:5002
cd frontend && npm install && npm start           # http://localhost:4200 (proxies /api to both)
```

<details>
<summary>No Docker? Run Postgres and Kafka natively with Homebrew (macOS)</summary>

The services expect Postgres on **port 5433** (user and password `claims`, databases `claims` and `insights`) and Kafka on **127.0.0.1:9092**.

```bash
brew install postgresql@17 kafka

# Postgres on 5433
initdb -D ./.local/pg -U claims --pwfile=<(echo claims) -A scram-sha-256
pg_ctl -D ./.local/pg -o "-p 5433" -l ./.local/pg.log start
psql -h localhost -p 5433 -U claims -d postgres -c "CREATE DATABASE claims;" -c "CREATE DATABASE insights;"

# Kafka (single-node KRaft), using the same settings as docker-compose.yml
cat > ./.local/kafka.properties <<'EOF'
process.roles=broker,controller
node.id=1
controller.quorum.voters=1@localhost:9093
listeners=PLAINTEXT://localhost:9092,CONTROLLER://localhost:9093
advertised.listeners=PLAINTEXT://localhost:9092
controller.listener.names=CONTROLLER
listener.security.protocol.map=PLAINTEXT:PLAINTEXT,CONTROLLER:PLAINTEXT
log.dirs=./.local/kafka-logs
offsets.topic.replication.factor=1
transaction.state.log.replication.factor=1
transaction.state.log.min.isr=1
auto.create.topics.enable=false
EOF
kafka-storage format -t "$(kafka-storage random-uuid)" -c ./.local/kafka.properties
kafka-server-start ./.local/kafka.properties
```
</details>

## Tests

```bash
cd backend && dotnet test        # 101 tests: unit, property-based (FsCheck), integration (Testcontainers; needs Docker)
cd frontend && npx ng test --watch=false   # 8 tests (vitest)
```

What they cover:
- **Domain:** every legal and illegal (status, action) pair; money precision per currency; reserve rules; and **property-based** random action sequences that check the claim stays consistent after every step. This found a real bug, recorded in the journal (3.4).
- **API, against real Postgres:** the full lifecycle; **concurrent pick-up has exactly one winner**; claimants can't see each other's claims or any reserve; role protection; ProblemDetails status codes.
- **Insights:** random duplicate or reordered delivery always gives the same projection; the USD headline always equals the sum of the market rows; redelivering a whole stream to a real database is a no-op.

## Layout

```
backend/
  src/Claims.Api        claim lifecycle REST API, domain, EF Core, outbox publisher
  src/Insights.Api      Kafka consumer, read model, dashboard API
  src/Claims.Contracts  the event contract (the only shared code)
  tests/                xUnit + FsCheck + Testcontainers
frontend/               Angular 21 (standalone, signals), nginx in Docker
infra/                  Postgres init (one database per service)
docs/                   architecture, acceptance criteria, design, AI journal
```

## What I prioritised and what I left out

The brief asks for **backend first**, so the backend is where the depth is: a correct state machine, transactional outbox, idempotent consumer, real concurrency handling and over a hundred tests. The frontend covers every role's core flows, but it is deliberately plain on edge cases.

**Left out on purpose:**
- real authentication
- document upload
- policy and coverage checks
- payments
- live FX rates
- email and SMS delivery
- reassigning claims (M-7)
- claimant withdrawal (C-8)

Each is listed with its production approach in [ACCEPTANCE_CRITERIA.md §8](docs/ACCEPTANCE_CRITERIA.md) and [ARCHITECTURE.md](docs/ARCHITECTURE.md#production-next-steps).
