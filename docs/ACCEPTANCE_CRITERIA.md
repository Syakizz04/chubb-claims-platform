# Acceptance Criteria

The brief is deliberately underspecified. These criteria are my interpretation of it. They are the definition of done for this sprint, and tests and commits reference their IDs.

**Priority:** **P0** = must have (core of the submission) · **P1** = should have if time allows · **P2** = stretch, documented but probably not built.

**Roles**
- **Claimant**: a policyholder reporting a loss.
- **Handler**: a claims officer who assesses and progresses claims.
- **Manager**: a team lead who oversees workload and exposure.

---

## 1. Domain rules (apply everywhere)

| ID | Priority | Criterion |
|---|---|---|
| DR-1 | P0 | Markets are **seeded data, not an enum**: KR/KRW, HK/HKD, TW/TWD, TH/THB, MY/MYR, SG/SGD. Each market stores its ISO 4217 currency and its number of decimal places (KRW = 0, all others = 2). Adding a market is a data change, not a code change. |
| DR-2 | P0 | A claim's currency is **derived from its market** and is never supplied by the client. |
| DR-3 | P0 | Money is stored as `decimal`, never float or double, always in the claim's original currency. An amount with more decimal places than its currency allows is rejected (e.g. `1000.5` KRW → 400). |
| DR-4 | P0 | Every claim gets a unique, human-readable reference `CLM-{market}-{6-digit sequence}`, e.g. `CLM-SG-000123`. |
| DR-5 | P0 | Claim status follows the state machine below. **An illegal transition returns 409 Conflict** with the current status, and nothing changes. |
| DR-6 | P0 | Every state change and reserve change writes an **immutable claim event**: who, when, from status, to status, and a note. Events are never edited or deleted. |
| DR-7 | P0 | Concurrent updates to the same claim are detected (optimistic concurrency). The losing writer gets **409 ProblemDetails, never a 5xx**, and nothing it sent is saved. |
| DR-8 | P0 | Type-specific details: a **motor** claim requires a vehicle registration; a **property** claim requires a property address. |

### Claim state machine

```
SUBMITTED ──pick up──▶ UNDER_REVIEW ──approve──▶ APPROVED ──settle──▶ SETTLED
                           │   ▲
              request info │   │ claimant responds
                           ▼   │
                       AWAITING_INFO

UNDER_REVIEW  ──reject──▶ REJECTED
AWAITING_INFO ──reject──▶ REJECTED
```

- Terminal states: **SETTLED**, **REJECTED**.
- "Open" means any non-terminal state. Only open claims count towards exposure. An APPROVED claim is still open, because the money hasn't been paid yet.
- A claim can be rejected from UNDER_REVIEW or AWAITING_INFO. The second covers a claimant who never responds; without it the claim would be stuck. Any open information request is closed when the claim is rejected.

### Reserves (drives liability exposure)

| ID | Priority | Criterion |
|---|---|---|
| RS-1 | P0 | When a claim is submitted, its **initial reserve equals the claimed amount**, so exposure shows up immediately. Assumption: this simplifies the formula-based initial reserving real insurers use. |
| RS-2 | P0 | The assigned handler can adjust the reserve to any amount ≥ 0 (it may exceed the claimed amount: the reserve is the insurer's estimate, not the claimant's) while the claim is `UNDER_REVIEW` or `AWAITING_INFO`. In any other status it returns 409; after approval the reserve is fixed at the approved amount (RS-3). Each change records the old value, the new value and a reason. |
| RS-3 | P0 | Approving a claim sets the reserve to the approved amount. Settling or rejecting it releases the reserve to 0. |

---

## 2. Claimant

| ID | Priority | Criterion |
|---|---|---|
| C-1 | P0 | **Report an incident.** The claimant provides market, claim type (MOTOR / PROPERTY), policy number, incident date, incident location, description and claimed amount. The response is 201 with the claim reference and status `SUBMITTED`. |
| C-2 | P0 | **Validation.** Required: market, type (Motor or Property), policy number, incident date, location, description, amount, plus the type-specific field (DR-8). Incident date not after **today in the market's own time zone** (not UTC). Description 20–4000 characters; location ≤ 200; vehicle registration ≤ 20; property address ≤ 300; any other free text (questions, replies, reasons, notes) ≤ 1000. Amount > 0 and ≤ 999,999,999,999,999. All failures come back **together** as one 400 RFC 7807 response with errors keyed by the request field name, and oversized input is never a 5xx. The policy number is format-checked only, not verified against a policy system (out of scope). |
| C-3 | P0 | **Track my claims.** The claimant sees a list of **only their own claims**, newest first, with reference, type, status, claimed amount and last updated. Asking for someone else's claim returns 404, so it doesn't leak whether the claim exists. |
| C-4 | P0 | **Claim detail and timeline.** The claimant sees claim details and a timeline of status changes. Internal handler notes and reserve figures are **not** visible to claimants. |
| C-5 | P0 | **Provide information when asked.** When the claim is `AWAITING_INFO`, the claimant sees the handler's question and can submit a text response. The claim moves back to `UNDER_REVIEW`. Responding when no question is open returns 409. |
| C-6 | P0 | **Receive decisions.** For an approved claim the claimant sees the approved amount. For a rejected claim they see the rejection reason. For a settled claim they see the settled amount and date. |
| C-7 | P1 | **Status updates appear without a manual refresh.** The claimant's open claim refreshes at least every 15 seconds. |
| C-8 | P2 | The claimant can withdraw a claim before it is decided. |
| C-9 | P2 | The claimant can attach photos or documents. |
| C-10 | P2 | **Idempotent submit.** A retried submission with the same `Idempotency-Key` header returns the original claim instead of creating a duplicate. Not built: today a double-submit creates two claims. |

---

## 3. Handler

| ID | Priority | Criterion |
|---|---|---|
| H-1 | P0 | **Incoming queue.** The handler sees unassigned `SUBMITTED` claims, **oldest first**, and can filter them by market and type. |
| H-2 | P0 | **Pick up a claim.** The claim is assigned to the handler and moves `SUBMITTED → UNDER_REVIEW`. **If two handlers pick up the same claim at once, exactly one succeeds**; every other caller gets 409 ProblemDetails, never a 5xx. |
| H-3 | P0 | **My workload.** The handler sees their open assigned claims with status, age (whole days since submission), claimed amount and current reserve, oldest first. Claims `AWAITING_INFO` are clearly marked (the ball is in the claimant's court). |
| H-4 | P0 | **Only the assigned handler** can act on a claim. Another handler gets 403. |
| H-5 | P0 | **Request information.** The handler asks a question (required text) and the claim moves `UNDER_REVIEW → AWAITING_INFO`. |
| H-6 | P0 | **Adjust reserve**, as in RS-2. |
| H-7 | P0 | **Approve.** The handler enters an approved amount, which must be > 0 and ≤ the claimed amount. The claim moves `UNDER_REVIEW → APPROVED`. |
| H-8 | P0 | **Reject.** A reason is required. The claim moves `UNDER_REVIEW → REJECTED` or `AWAITING_INFO → REJECTED` (e.g. the claimant never replies). Any open information request is closed and the reserve is released. |
| H-9 | P0 | **Settle.** The claim moves `APPROVED → SETTLED`. The settled amount is the approved amount and the reserve is released. Actual payment is out of scope. |
| H-10 | P1 | **Internal notes.** Any staff member (not only the assigned handler) can add a note to any claim, including a closed one. Notes are annotations, not actions, so they don't change status. Other staff can see them; claimants cannot. |
| H-11 | P1 | **Full timeline.** The handler sees every event, including reserve changes and internal notes. |

---

## 4. Manager

All manager views are served by the **Insights service**, built from Kafka events. They may lag a little behind the Claims service; that is eventual consistency, and it is expected.

| ID | Priority | Criterion |
|---|---|---|
| M-1 | P0 | **Outstanding exposure.** The sum of reserves on open claims, broken down by market and claim type. Each market's figure is **displayed in that market's own currency**, formatted with its ISO currency code and decimal places (e.g. KRW 12,500,000 · MYR 8,200.00). Symbols are ambiguous: "$" is used by HKD, TWD and SGD. |
| M-2 | P0 | **USD as the main total.** The headline exposure figure is in **USD** (Chubb's reporting currency), and each market also shows its USD equivalent next to its local figure. Converted at **static seeded FX rates**, clearly labelled "indicative, rates as of {date}". The USD figure is calculated when it is read and is **never stored**. |
| M-3 | P0 | **Team workload.** For each handler: open claims, how many are awaiting info, and the age (whole days since submission) of their oldest open claim. Also the unassigned queue size and the age of the oldest unassigned claim. |
| M-4 | P1 | **Ageing.** Open claims grouped into 0–7, 8–30 and 31+ days since submission. |
| M-5 | P1 | **Performance.** For each handler, claims **decided** (approved or rejected) in the last 30 days, split into approved and rejected, plus the average days from submission to decision. "Decided" rather than "closed", because settlement timing depends on payment, not on the handler. |
| M-6 | P0 | The dashboard shows a "last updated" timestamp, so the lag from eventual consistency is visible, not hidden. |
| M-7 | P2 | The manager can reassign a claim to another handler. |

---

## 5. Events and service boundaries (Claims → Kafka → Insights)

| ID | Priority | Criterion |
|---|---|---|
| E-1 | P0 | Every claim event (DR-6) is published to the Kafka topic `claims.events`, **keyed by claim ID** so events for one claim stay in order. |
| E-2 | P0 | **Transactional outbox.** The state change and its outbox row are committed in one database transaction, and a background publisher sends outbox rows to Kafka with retries. **If Kafka is down, the Claims API still accepts writes**, and the events are delivered once Kafka comes back: the outbox drains within 30 seconds of Kafka becoming reachable. |
| E-3 | P0 | **Idempotent consumer.** Insights records the event IDs it has processed. Receiving the same event twice does not double-count it. |
| E-4 | P0 | **Fault isolation.** Claims never calls Insights. Stopping Insights has no effect on claim processing. When Insights restarts, it catches up from its last committed Kafka offset. |
| E-5 | P1 | **Claimant notifications.** Insights records a notification for the claimant on INFO_REQUESTED, APPROVED, REJECTED and SETTLED. They are stored and logged; email and SMS are out of scope. |
| E-6 | P1 | **Reconciliation.** With zero consumer lag, Insights' exposure for each market equals the sum of open-claim reserves in the Claims database for that market. |

### What uses REST and what uses Kafka

| Interaction | Transport | Why |
|---|---|---|
| Submit, pick up, request info, respond, approve, reject, settle, adjust reserve | **REST**, synchronous | The user needs an immediate, authoritative yes or no, e.g. "you got the claim" or "that transition is illegal". |
| Reading my claims, the queue and claim detail | **REST**, against the Claims service | These must be strongly consistent: a handler must never see a claim as free when it has already been taken. |
| Exposure, workload, ageing, performance | **REST**, against the Insights service, whose data is **built from Kafka** | Aggregates can lag a little, and building them must never slow down or break claim processing. |
| Notifications | **Kafka** | A side effect that doesn't need to block the request. |

---

## 6. Non-functional

| ID | Priority | Criterion |
|---|---|---|
| N-1 | P0 | `docker compose up` starts Postgres, Kafka, the Claims API and the Insights service, plus the Angular app if it's built. Seed data loads automatically. |
| N-2 | P0 | The Claims API publishes an **OpenAPI spec with Swagger UI**. The backend owns the contract. The frontend's TypeScript types are hand-maintained to match it; generating them from the spec is the production step. |
| N-3 | P0 | **Development identity.** Seeded users (claimants, handlers, a manager) are chosen with an `X-User-Id` header and a user switcher in the UI. Both APIs enforce roles per route: Insights dashboards are staff-only, and notifications are the caller's own. Insights has no user store, so it reads the role from an `X-User-Role` header, standing in for a JWT role claim. **This is documented as not production authentication.** |
| N-4 | P0 | **Tests.** Unit tests cover every legal and illegal state transition and the money and decimal-place rules. Integration tests cover the happy path (submit → pick up → request info → respond → approve → settle) and the concurrent pick-up race (H-2). Integration tests use Testcontainers and need Docker; unit and property tests run without it. |
| N-5 | P0 | Errors are returned as RFC 7807 ProblemDetails with consistent status codes: 400 validation, 401 missing or unknown identity, 403 forbidden, 404 not found or not yours, 409 illegal transition or concurrency conflict. Precedence: 401, then 404, then 403 (e.g. acting on a claim assigned to someone else is 403 even if the action would also be illegal), then the claim's state and field checks. **Client input and races never produce a 5xx.** |
| N-6 | P1 | Structured logs carry the claim reference and event ID, so one claim can be traced across both services. |

---

## 7. Frontend (Angular, after the backend)

| ID | Priority | Criterion |
|---|---|---|
| F-1 | P1 | A user switcher (dev identity). The routes and navigation shown depend on the role. |
| F-2 | P1 | Claimant: submit form with validation, my claims list, claim detail with timeline and the response form for information requests. |
| F-3 | P1 | Handler: incoming queue with pick-up, my workload, claim detail with actions (request info, adjust reserve, approve, reject, settle). |
| F-4 | P1 | Manager: dashboard showing exposure per currency plus the USD total, team workload and the "last updated" time. |
| F-5 | P2 | Ageing and performance charts. |

---

## 8. Explicitly out of scope

| Item | Production approach |
|---|---|
| Real authentication | OIDC/SSO for staff, customer identity for claimants |
| Policy verification and coverage checks | A separate Policy service would own this |
| Document upload | Object storage plus virus scanning |
| Payments | Integration with the finance system |
| Live FX rates | A daily rates feed, with period-end rates for financial reporting |
| Email and SMS delivery | A notification provider |
| Fraud scoring | Not built |
| Data residency per market | An important production concern across six jurisdictions |
| Pagination | Lists are capped (staff claim search returns at most 200, newest first). Production uses cursor paging |
| Duplicate-claim detection | Matching on policy, date and location as a fraud and ops signal |
