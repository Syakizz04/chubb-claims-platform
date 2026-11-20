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

### 3.3 Hypothesis replaced with FsCheck
- **Asked:** write unit tests, and use Hypothesis wherever property-based testing would improve them.
- **AI said:** Hypothesis is Python-only and can't run in a .NET test suite. It proposed **FsCheck**, the .NET counterpart: random input generation plus shrinking failures to a minimal example.
- **Decision:** Accepted the substitution.
- **Why:** same technique, right ecosystem. Properties chosen where hand-picked examples are weakest:
  - **Money precision:** any amount within the currency's decimal places is valid; one extra decimal place is invalid.
  - **Lifecycle:** 300 random sequences of actions by random users, with invariants checked after every step:
    - reserve ≥ 0, and 0 once the claim is closed
    - approved ≤ claimed
    - an open question exists exactly when the claim is AWAITING_INFO
    - the audit trail has no gaps and replays to the current status
    - **a rejected action leaves the claim completely unchanged**

### 3.4 Property test found a real bug
- **What happened:** on its 6th random run, FsCheck found that a **claimant could pick up a claim and become its handler**. The `Claim` aggregate trusted whoever called it; only the HTTP `Handler` policy blocked this.
- **Decision:** fixed it in the domain. `PickUp` now takes a `User` and requires the Handler role.
- **Why:** business rules shouldn't depend on which entry point is used. A background job, script or future endpoint would have bypassed the HTTP policy. The example-based tests all used valid handlers, so they never could have caught this. That's the argument for property-based testing.

---

## Session 4: Frontend (Claude Code)

### 4.1 Impeccable design skill: installed, binary not run
- **Asked:** download the Impeccable UI design skill and follow it for the frontend.
- **AI said:** it cloned the skill (v4.10, Apache 2.0) and reviewed it before installing. The guides are Markdown, but the `scripts/impeccable` launcher **downloads and runs a compiled binary**. It installed the skill locally (gitignored, not part of the submission) and **didn't run the binary**, using the skill's documented fallback of reading the guides directly.
- **Decision:** Accepted.
- **Why:** running an unreviewed downloaded executable just for design guidance isn't worth the supply-chain risk, and the guidance itself is in plain text.
- **Applied from the skill:** "Operate" mode (familiar controls, restrained colour, density allowed); every state covered (loading skeletons, empty states that explain what to do, errors, disabled and busy); no modals; no eyebrow labels, gradient text or hero-metric tiles; a self-hosted typeface; tabular figures; themed focus rings and selection.

### 4.2 Chubb design language: researched, not guessed
- **Asked:** follow the general UI design rules Chubb uses.
- **AI said:** it researched public sources. The COLLINS rebrand case study gives "simplicity, clarity and certainty", a plain wordmark balanced by "bold, color-forward" accents, and Claims as a top-level destination. **No public source gave hex values or a typeface**, so the AI said plainly that the palette and font are our own choices in that spirit, not Chubb's official ones.
- **Decision:** Accepted.
- **Why:** inventing "official" brand colours would be a claim the panel could easily disprove.
  - Chubb's usage guide says **the logo must not be re-created or re-set**, so the app uses a plain-text product name and no wordmark.
  - "Colour-forward" became **one colour per market, used only for data**. That gives Chubb's colour-forward feel without decorating an operational tool.

### 4.3 ISO currency codes, not symbols
- **AI said:** `$` is ambiguous across HKD, TWD and SGD, so amounts display as `SGD 4,200.00` and `KRW 3,500,000`. This changes the acceptance criteria wording, which had said "symbol".
- **Decision:** Accepted.
- **Why:** in a multi-market financial tool, an ambiguous figure is worse than a slightly less friendly one.

### 4.4 Frontend architecture choices
- **The backend decides the buttons.** The staff claim page renders `availableActions` from the API instead of re-implementing the state machine in TypeScript, so the rules live in one place.
- **Polling for async updates** (claimant every 10s, dashboard every 5s, queue every 10s), behind one `live()` helper. It's simple and good enough at this scale; server-sent events fed from Kafka are the next step. The dashboard shows "Updated X ago" so eventual consistency is visible, not hidden.
- **409 handling:** if another handler picks up a claim first, the queue says so plainly and refreshes. Any other conflict refreshes the claim to show its real current state.
- **Hypothesis-style tests on the frontend** weren't worth it here: the logic worth testing (money formatting, status track) is small and fully enumerable, so it has plain unit tests.

### 4.5 Environment issue: disk full, Docker storage corrupted
- Mid-build, the Mac's disk filled up (173 MB free) and Docker's image store started returning I/O errors. The AI stopped and asked me to free space rather than deleting anything itself. After I freed space, Docker's store stayed corrupted and needed a purge, which is my call because it removes every Docker image on the machine. Frontend work carried on in the meantime because it doesn't need Docker.

## Session 5: Verification and independent review (Claude Code)

### 5.1 Homebrew instead of Docker for verification
- **Context:** Docker stayed broken, and purging it would delete every image on my machine. I asked whether the brief requires Docker. It doesn't: it asks for an app that starts locally.
- **Decision (mine):** run Postgres 17 and Kafka natively with Homebrew. The Docker setup stays in the repo, labelled "not yet run". The README leads with the verified native route.
- **Consequence:** the Testcontainers integration tests can't run until Docker is fixed. Unit and property tests still run.

### 5.2 End-to-end browser test found layout bugs the unit tests couldn't
- The AI drove the README's two-minute tour in headless Chrome against the real stack: zero console errors, and the claimant never sees the reserve.
- **Screenshots exposed:**
  - every page scrolling sideways at phone width
  - amounts wrapping onto two lines
  - the header nav cut off on phones
  - a stretched toggle
- All fixed and rechecked at 390px.

### 5.3 Two independent reviewers (subagents)
- I asked for two subagents with full context and no permission to edit: one to critique the stack, one to test the running app against the acceptance criteria.
- **Stack critique, accepted and fixed:**
  - The Insights API had no auth: a claimant could read the dashboard by calling it directly.
  - A market without an FX rate made the whole dashboard return 500.
  - Kafka's default 7-day retention would make a later rebuild of Insights incomplete.
  - The Postgres healthcheck could pass before the `insights` database existed.
- **Challenged:** for Insights auth, the obvious fix was to look the user up in Claims. Rejected, because it couples the services: a Claims outage would lock managers out of the dashboard. Insights instead trusts a role claim (`X-User-Role`), as it would trust a signed JWT. In dev that header is unsigned, exactly as spoofable as `X-User-Id` already is.
- **Challenged:** for retention, compaction vs infinite. Chose infinite, because notifications and sequences come from every event, not only the latest one per claim.
- **Declined:** rewriting commit history to hide identical timestamps. That would be dishonest; this journal already says the AI executed the plan.
- **QA report:** found that concurrent writes return 500 instead of 409, plus other defects and wording gaps in the criteria. Pending my decision on what to fix.

### 5.4 Acting on the QA report
- **Fixed in code:**
  - Concurrent writes now return 409, not 500. The unique-sequence violation is the same lost race as the xmin check.
  - Oversized input and huge amounts are 400s on the field.
  - A missing claim type is a 400, not a silent "Motor".
  - Error keys match the request fields.
  - Incident dates are checked against each market's local date: markets now carry a time zone.
- **AI suggested, I agreed:** for the "future date" bug, store an IANA time zone per market rather than loosening the check to "UTC+14 today". Markets stay data, and the rule stays exact.
- **Doc-only changes:** the criteria now match what was built where they had drifted:
  - reserve editable only under review or awaiting info
  - reject from awaiting info
  - "decided", not "closed"
  - polling wording
  - error precedence
  - "never 5xx"
  - hand-maintained frontend types
- **Raised:** DR-8 and M-6 to P0.
- **Added:**
  - reconciliation (E-6)
  - idempotent submit as P2, not built
  - pagination and duplicate detection as out of scope
- **Checked at my request:** that the assessment brief never entered git history. It hasn't; the .docx has been gitignored from the first commit, and no committed text quotes it.

### 5.5 UI clean-up, microcharts and a product name
- **My request:**
  - remove the dev note from the front page
  - make the Updates panel scroll and stay in sync with the claims list
  - paginate the list past 10 claims
  - add microcharts, but only where they genuinely help
  - give the platform a name and logo
- **Microcharts:** limited to the dashboard, where rows get compared:
  - a share meter per market
  - handler load
  - an approved/rejected split
  - a 14-day intake sparkline

  No charts on the claimant and handler screens; their key facts are single values.
- **The AI's palette check overruled our earlier design:** the skill's palette validator showed the market colours from session 4 failed. Hong Kong and Singapore were nearly identical even for full colour vision. They were swapped for validated colours, drawn in a fixed market order.
- **Name:** picked from three AI options (Lantern, Meridian, Harbour). I chose **Lantern**, because the brief's core problem is that nobody can see where a claim stands. Tagline: "Every claim visible, from report to payout." The logo is a lantern outline with a teal glow. It doesn't use Chubb's wordmark, which their usage guide forbids.
- **Front page:** chose the sign-in card from three layouts. It stands in for a sign-in screen, so it now looks like one.
