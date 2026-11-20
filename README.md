# Claims Platform: Asia Pacific

**One app for motor and property claims across six APAC markets. Claimants report and track claims, handlers work them through to settlement, and managers see outstanding liability in each market's own currency.**

Built as a take-home for the Chubb APAC Young Graduate Program (full-stack): **.NET 10 · PostgreSQL · Kafka · Angular 21.**

| Who | Can |
|---|---|
| **Claimant** | Report an incident, follow the claim's progress, answer the claims team's questions, see the decision |
| **Claims handler** | Pick up claims from the incoming queue, request information, adjust the reserve, approve or reject, settle |
| **Manager** | See outstanding exposure per market (local currency, plus USD), team workload, claim ageing and decisions |

Markets: 🇰🇷 KR · 🇭🇰 HK · 🇹🇼 TW · 🇹🇭 TH · 🇲🇾 MY · 🇸🇬 SG.

---

## Quick start

There are two ways to run it. **Option A is the one I've verified end to end**, with a browser click-through and zero console errors. Option B is a single command, but **it hasn't been run yet**: Docker on my machine broke during the sprint (see [AI journal 4.5](docs/AI_JOURNAL.md#45-environment-issue-disk-full-docker-storage-corrupted)).

### Option A: native (verified)

**Needs:** .NET 10 SDK, Node 22+, Homebrew (macOS).

```bash
brew install postgresql@17 kafka
mkdir -p .local
PG=/opt/homebrew/opt/postgresql@17/bin   # postgresql@17 is keg-only, so call it by path

# 1. Postgres on port 5433 (user/password: claims)
echo claims > .local/pw
$PG/initdb -D .local/pg -U claims --pwfile=.local/pw -A scram-sha-256
$PG/pg_ctl -D .local/pg -o "-p 5433" -l .local/pg.log start
PGPASSWORD=claims $PG/psql -h localhost -p 5433 -U claims -d postgres \
  -c "CREATE DATABASE claims;" -c "CREATE DATABASE insights;"

# 2. Kafka on 127.0.0.1:9092 (single-node KRaft)
cat > .local/kafka.properties <<'EOF'
process.roles=broker,controller
node.id=1
controller.quorum.voters=1@localhost:9093
listeners=PLAINTEXT://localhost:9092,CONTROLLER://localhost:9093
advertised.listeners=PLAINTEXT://localhost:9092
controller.listener.names=CONTROLLER
listener.security.protocol.map=PLAINTEXT:PLAINTEXT,CONTROLLER:PLAINTEXT
log.dirs=.local/kafka-logs
offsets.topic.replication.factor=1
transaction.state.log.replication.factor=1
transaction.state.log.min.isr=1
auto.create.topics.enable=false
EOF
kafka-storage format -t "$(kafka-storage random-uuid)" -c .local/kafka.properties
kafka-server-start .local/kafka.properties          # leave running
```

Then, in three more terminals:

```bash
cd backend/src/Claims.Api   && dotnet run     # http://localhost:5001
cd backend/src/Insights.Api && dotnet run     # http://localhost:5002
cd frontend && npm install && npm start      # http://localhost:4200
```

### Option B: Docker (one command, not yet run)

```bash
docker compose up --build
```

This starts Postgres, Kafka, both services and the frontend (behind nginx) on the same ports as Option A.

### You'll know it worked when

- The Claims log says `Seeded 6 markets and 10 users`, `Seeded demo claims` and `Published … claim event(s) to Kafka`.
- The Insights log shows `Projected …` lines. Open **http://localhost:4200**, choose **Claims manager**, and the dashboard shows exposure for all six markets.

| URL | What |
|---|---|
| http://localhost:4200 | The app. Pick a role on the welcome screen. |
| http://localhost:5001/swagger | Claims API. Use *Authorize* with a user id such as `handler-1`. |
| http://localhost:5002/swagger | Insights API (dashboards, notifications) |

---

## Try it

### In the browser (about 2 minutes)

There's no sign-in. Switch users with the **DEV** selector in the header.

1. **Mei Ling Tan** (claimant): **Report a claim** → you get a reference like `CLM-SG-000005`.
2. **Priya Nair** (handler): **Incoming** → **Pick up** → **Request information**.
3. **Mei Ling Tan**: the claim now asks for information → **Send reply**.
4. **Priya Nair**: **Adjust reserve** → **Approve** → **Settle and close**.
5. **Grace Lim** (manager): exposure and workload update within about 5 seconds. "Updated … ago" shows how fresh the figures are.

### Against the API

These are real responses from the running stack:

```bash
# A claimant reports a claim. The currency comes from the market, and KRW has no decimals.
curl -s -X POST localhost:5001/api/claimant/claims \
  -H 'X-User-Id: claimant-kr' -H 'Content-Type: application/json' \
  -d '{"marketCode":"KR","type":"Motor","policyNumber":"POL-KR-204518","incidentDate":"2026-11-19",
       "incidentLocation":"Mapo-gu, Seoul","description":"Rear bumper crushed by a reversing van in a car park.",
       "claimedAmount":1850000,"vehicleRegistration":"12가3456"}'
# → 201  {"reference":"CLM-KR-000012","status":"Submitted","currencyCode":"KRW","claimedAmount":1850000, ...}

# Two handlers pick up the same claim: exactly one wins.
curl -s -X POST localhost:5001/api/staff/claims/<id>/pick-up -H 'X-User-Id: handler-1'   # → 200
curl -s -X POST localhost:5001/api/staff/claims/<id>/pick-up -H 'X-User-Id: handler-2'
# → 409  {"title":"Action not allowed in the current status.","detail":"Cannot PickUp a claim that is UnderReview.","currentStatus":"UnderReview"}

# Money is validated against each currency's decimal places.
#   "claimedAmount": 1850000.5 for KRW
# → 400  {"errors":{"claimedAmount":["This currency does not allow decimal places."]}}
```

**Seeded users:**

| Role | Users |
|---|---|
| Claimant | `claimant-sg` (Mei Ling Tan), `claimant-kr`, `claimant-hk`, `claimant-tw`, `claimant-th`, `claimant-my` |
| Handler | `handler-1` (Priya Nair), `handler-2` (Daniel Wong), `handler-3` (Siti Rahman) |
| Manager | `manager-1` (Grace Lim) |

---

## How it's built

```
Angular ──REST──▶ Claims API ──(same DB transaction)──▶ claims DB + outbox
   │                                                         │ outbox publisher
   │                                                         ▼
   └────REST──▶ Insights API ◀── idempotent consumer ◀── Kafka: claims.events
```

**Two services, split by what they need:**
- **Claims** must be exactly right, immediately: one handler per claim, no paying twice, illegal moves rejected with 409. It uses REST.
- **Insights** aggregates across every claim and can lag a few seconds. It builds its own read model from Kafka.

Claims never calls Insights. If Insights is down, claims still get processed, and Insights catches up from its Kafka offset.

**What I'd point a reviewer at:**
- **Transactional outbox.** A claim change and its event are saved in one transaction, so the API keeps accepting claims while Kafka is down.
- **Idempotent consumer.** Duplicate or late events can't double-count exposure; this is property-tested.
- **Optimistic concurrency.** Postgres `xmin` gives the "exactly one handler wins" result shown above.
- **One state machine** in the domain. The API returns `availableActions`, so the UI never re-implements the rules.
- **Money.** `decimal` in local currency, checked against ISO 4217 decimal places. USD is a read-time view at static seeded rates, labelled indicative.

The full reasoning, including what uses REST vs Kafka and why, is in **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

---

## Tests

```bash
cd backend && dotnet test                    # 101 tests (integration tests need Docker running)
cd frontend && npx ng test --watch=false     # 8 tests
```

- **Domain:** every legal and illegal status/action pair; money precision; reserve rules. Property-based tests (FsCheck) run random action sequences and check the claim stays consistent. They found a real bug: a claimant could pick up a claim ([journal 3.4](docs/AI_JOURNAL.md#34-property-test-found-a-real-bug)).
- **API, against real Postgres (Testcontainers):** the full lifecycle; concurrent pick-up has exactly one winner; claimants can't see each other's claims or any reserve.
- **Insights:** random duplicate or reordered delivery gives the same projection; the USD headline always equals the sum of the market rows.

**Status today:** the unit and property tests run anywhere. The integration tests passed earlier in the sprint, but can't run while Docker is broken on my machine.

---

## Scope and known limitations

The brief puts the backend first, so the depth is there. The frontend covers every role's core flow without edge-case polish.

**Not built (on purpose):** real authentication, document upload, policy and coverage checks, payments, live FX rates, email and SMS, reassigning claims, claimant withdrawal. Each has a production approach in [ACCEPTANCE_CRITERIA §8](docs/ACCEPTANCE_CRITERIA.md) and [ARCHITECTURE: Production next steps](docs/ARCHITECTURE.md#production-next-steps).

**Known gaps worth knowing before you probe:**
- **Dev identity only.** `X-User-Id` picks a seeded user. Claims enforces roles per route, but **the Insights API has no auth yet**, so anyone who can reach it can read the dashboard.
- **FX rates are seeded separately in Insights.** A new market without a rate still shows in local currency, flagged "No rate", and is left out of the USD totals until its rate is added.
- **Kafka topic uses default retention (7 days).** Rebuilding Insights from scratch after that would be incomplete. A compacted topic is the fix.
- **The UI polls** every 5–30 seconds instead of receiving pushed updates.
- **Docker path not yet run**, as noted above.

---

## Repository

```
backend/src/Claims.Api        claim lifecycle API, domain, EF Core, outbox publisher
backend/src/Insights.Api      Kafka consumer, read model, dashboard API
backend/src/Claims.Contracts  the event contract (the only code the services share)
backend/tests/                xUnit, FsCheck, Testcontainers
frontend/                     Angular 21 (standalone, signals)
docs/                         architecture, acceptance criteria, design, AI journal
```

| Doc | Read it for |
|---|---|
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Service boundaries, REST vs Kafka, reliability, domain model |
| [ACCEPTANCE_CRITERIA.md](docs/ACCEPTANCE_CRITERIA.md) | Definition of done; IDs are referenced in commits and tests |
| [DESIGN.md](docs/DESIGN.md) | Frontend design direction |
| [AI_JOURNAL.md](docs/AI_JOURNAL.md) | What I asked the AI, and what I accepted, challenged and overrode |

## License

No license. This is a private assessment submission and isn't offered for reuse.
