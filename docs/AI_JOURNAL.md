# AI Working Journal

A running log of what I asked the AI, what I accepted, what I challenged and what I overrode, each with brief reasoning. Not polished on purpose.

**Tools:** Claude (claude.ai chat) for early exploration; Claude Code (CLI) for planning and code generation.

**Format:** `Asked` → `AI said` → **Decision** (Accepted / Challenged / Overrode / Deferred) → `Why`.

---

## Session 1: Understanding the brief and early decisions (claude.ai, on another machine)

> That machine has since broken. These entries are reconstructed from the chat history I still have, so they are summaries, not verbatim prompts.

### 1.1 What do I actually need to submit?
- **Asked:** for a breakdown of the deliverables.
- **AI said:** a git repo with incremental commits, an app that runs locally (ideally one `docker compose up`), this journal, and supporting docs (architecture, prioritisation, how to run the tests).
- **Decision:** Accepted.
- **Why:** it matches the brief. The useful takeaway was to commit as I go, not in one dump at the end.

### 1.2 Learning the domain
- **Asked:** what "service boundaries", "liability exposure" and "APAC" mean, in plain terms.
- **AI said:**
  - **Liability exposure:** how much the insurer might still have to pay on claims it hasn't finished dealing with, tracked as the sum of reserves on open claims.
  - **Service boundary:** the line that decides which service owns which data and rules; other services must go through its API or events.
- **Decision:** Accepted.
- **Why:** these definitions shaped the data model directly. Exposure = sum of reserves on open claims became RS-1 to RS-3 and M-1.

### 1.3 .NET vs Spring Boot
- **Asked:** compare the two for this brief.
- **AI said:** both are fine. Spring Kafka is more mature, .NET has less boilerplate. Pick the one I can defend line by line.
- **Decision:** Chose **.NET**.
- **Why:** lighter and quicker to work with (minimal APIs, records, EF Core migrations), which matters in a 3-hour sprint. I accept the trade-off that Confluent.Kafka is more manual than Spring Kafka, because we only need one producer and one consumer.

### 1.4 One service or two?
- **AI said (claude.ai, first):** a modular monolith, meaning one service with internal modules, and Kafka used internally.
- **AI said (Claude Code, later):** two services. The Claims service is correctness-critical (REST); the Insights service is lag-tolerant reporting built from Kafka events.
- **Decision:** Accepted **two services**, over the first suggestion.
- **Why:** with one service Kafka is just sending messages to itself, and the panel could fairly ask "why not a method call?" Splitting where the needs differ (exact and immediate vs aggregated and can lag) gives Kafka a real job, and gives loose coupling, fault isolation and temporal decoupling. Insights can be down and catch up later.
  - **Costs I accept:** about 30–45 minutes of setup, eventually consistent dashboards, and needing an outbox and an idempotent consumer.
  - **Why not more than two:** each extra service adds failure points with no matching benefit at this scale.
- **One-liner for the panel:** "Kafka decouples consistent claims processing from lag-tolerant reporting."

### 1.5 Multi-currency exposure
- **AI said (Claude Code):** report exposure per currency, plus a USD total at fixed seeded rates, with the rates documented as static.
- **Asked (claude.ai):** given what kind of company Chubb is, is that right?
- **Decision:** Accepted, and **extended** it.
- **Why:** liabilities are actually paid in local currency, so per-currency figures are the truth. Chubb reports group results in USD, so managers also want one headline USD number.
  - **Rules I added:** store every amount in its original currency with its ISO code; use `decimal`, never float; calculate the USD total only when reading and never store it; keep the rates in a table with an "as of" date; label the total "indicative" in the UI.

### 1.6 Which six markets?
- **AI said, first:** SG, HK, MY, TH, ID, AU.
- **Challenged:** I asked for the markets where Chubb is most significant, not just a reasonable spread.
- **AI said, second:** Chubb doesn't publish market share by country, but its annual report names its core Asian markets, with Korea as its second-largest country after the US. Suggested **KR, HK, TW, TH, ID, SG**: five core markets plus Singapore as the regional base.
- **Decision:** Accepted the second list.
- **Why:** based on Chubb's own reporting rather than a guess. Markets are **seeded data, not an enum**, so adding one is a data change.

---

## Session 2: Planning and acceptance criteria (Claude Code, this machine), 2026-11-20

> The original requirements file was lost with the broken machine, so we restarted from a clean slate.

### 2.1 Reading the brief and planning
- **Asked:** read the brief, plan, and run everything by me before doing anything.
- **AI said:** a phased plan (skeleton → Claims API → outbox and Kafka → Insights → tests → Angular → docs), with a check-in after each phase.
- **Decision:** Accepted the phases, with the rule that **nothing is written or committed without my approval**.
- **Why:** I sign off every line, so I want to review at every step, not at the end.

### 2.2 Correcting decimal places per currency
- **AI said:** a correction to Session 1, where the earlier chat said IDR has no decimal places. Under ISO 4217, **IDR and TWD have 2 decimal places; only KRW has 0.** It suggested storing the ISO value and treating "show no decimals" as a separate display concern.
- **Decision:** Accepted.
- **Why:** if the stored precision is wrong, amounts the standard allows could be silently rounded away. The AI caught a mistake from the earlier session. That's a reminder to verify AI facts rather than carry them forward.

### 2.3 Acceptance criteria
- **Asked:** write acceptance criteria for the whole project (`docs/ACCEPTANCE_CRITERIA.md`), each with an ID and a priority (P0/P1/P2).
- **AI assumptions for me to check:**
  - Initial reserve = claimed amount on submission.
  - Rejection is only allowed from UNDER_REVIEW.
  - Approved amount must be ≤ claimed amount.
  - Someone else's claim returns 404, not 403.
  - Claimants can't see reserves or internal notes.
- **Decision:** Accepted all five as proposed.
- **Why:** each one is defensible and keeps scope tight. Initial reserve = claimed amount means exposure appears from the first claim. 404 for other people's claims avoids leaking that a claim exists. Hiding reserves from claimants matches how reserves really work: they are an internal estimate, not a promise. I'll revisit these if they cause problems during the build.

### 2.4 Commit attribution
- **Decision:** commits go under my own git identity with no AI co-author trailer. AI use is disclosed in full in this journal instead.

### 2.5 Overrode: Malaysia replaces Indonesia
- **Context:** in session 1 I had already decided to swap Indonesia for Malaysia, but that wasn't in the reconstructed notes, so the acceptance criteria went out with ID/IDR.
- **Decision:** Overrode. The markets are now **KR, HK, TW, TH, MY, SG**.
- **Why:** Malaysia (MYR) is a Chubb APAC market and replaces Indonesia. Because markets are seeded data, this is a one-row change, which also shows the "config, not code" design works. The currency spread is still good: KRW has 0 decimals, everything else has 2.

### 2.6 How currency is displayed
- **Decision:** each market's exposure is shown **in its own currency**, formatted with its symbol and decimal places. **USD is the main headline total**, and each market also shows its USD equivalent.
- **Why:** local currency is what is actually owed; USD is how Chubb reports at group level. Managers need both, with USD as the figure they read first. This tightens M-1 and M-2.

### 2.7 Letting the AI execute
- **Decision:** once the acceptance criteria were agreed and nothing else needed clarifying, I told the AI to build through the phases with a commit per logical step. I review each commit afterwards and log challenges and overrides here.
- **Why:** the planning gave the AI enough constraints. Approving every file individually would burn the time box.
- **Not committed:** the brief's `.docx` is kept out of the repo. It's the company's document, not my work.

---

## Session 3: Building the Claims domain (Claude Code)

### 3.1 AI revised its own assumption: reject from AWAITING_INFO
- **AI said:** while coding the state machine, it found that the agreed rule ("reject only from UNDER_REVIEW, the handler moves it back first") had no action that moves a claim from AWAITING_INFO back to UNDER_REVIEW. A claim whose claimant never replies would be **stuck forever**.
  - **Options:** add a "withdraw information request" action, or allow rejecting from AWAITING_INFO.
- **Decision:** allow reject from AWAITING_INFO, closing the open information request. The acceptance criteria were updated to match. _[review this, since it changes something I signed off on]_
- **Why:** real claims are closed for non-response all the time, and it's one table row instead of a new action, a new endpoint and new tests.
- **Lesson:** the assumption looked fine on paper and only failed when someone tried to implement it.

### 3.2 Domain design choices worth defending
- **All rules live on the `Claim` aggregate**, and the state machine is a single lookup table. Unit tests generate *every* (status, action) pair and assert that each one is either in the table or rejected. A guard test fails if someone adds a transition without updating the tests.
- **Each action emits exactly one `ClaimEvent`, numbered 1, 2, 3… per claim.** That one stream drives the audit trail, the timelines and the Kafka events. The numbering lets Insights ignore duplicates and stale events.
- **An APPROVED claim still counts as open**, so its reserve stays in exposure until it's settled. The money is owed but not yet paid.
- **Claimant mismatch returns NotFound, handler mismatch returns Forbidden.** Claimants must not learn that other people's claims exist (C-3). Staff can see all claims but can only act on their own (H-4).
