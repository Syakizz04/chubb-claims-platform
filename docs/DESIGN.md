# Design

The visual decisions for the Angular app. The direction follows the **Impeccable** design skill's *Operate* mode (people come to this app to get a task done) and the public principles of Chubb's identity. It does **not** reproduce Chubb's logo or brand assets.

## What we took from Chubb's design language

What public sources say about Chubb's identity:
- The rebrand by the agency COLLINS aimed for **"simplicity, clarity and certainty"**.
- It pairs **a plain wordmark** with **"bold, color-forward"** accents.
- Chubb.com puts **Claims as a top-level destination**.
- The tone is confident, personal and plain-spoken.

Sources: [COLLINS case study](https://wearecollins.com/case-studies/chubb) · [Chubb: Our Brand](https://www.chubb.com/id-en/media-centre/our-brand.html)

Neither source publishes hex values or a typeface, so the specifics below are **our own choices made in that spirit**, not Chubb's official palette.

Chubb's usage guide says the logo must not be re-created or re-set in another typeface, so the app uses a **plain-text product name** and no wordmark.

## Direction

| Principle | How it shows up |
|---|---|
| **Simplicity** | One sans family. White work surfaces on a light grey canvas. No decorative cards, gradients or glass. |
| **Clarity** | A dense, scannable table for queues; a status pill on every claim; actions that say exactly what they do ("Pick up claim", "Request information"). |
| **Certainty** | Square-ish 2px corners. Tabular figures for every amount. Money always shown with its **ISO currency code** and the correct number of decimals. |
| **Colour-forward, with a purpose** | A near-black shell header, plus **one vibrant colour per market** used *only* for data (the exposure ledger and market tags). Colour carries information, never decoration. |

### Signature move: the market ledger
Exposure is drawn as a single full-width bar split into segments by market, each segment proportional to its USD value and in that market's colour. Under the bar, each market's row shows **local currency first** (the real liability) and its USD equivalent beside it. This puts the platform's main idea on one line: *the liability is local; USD is the lens*.

### Secondary: the status track
On a claim's detail page, a horizontal track shows the claim's journey (Submitted → Under review → Awaiting info → Decision → Settled), with the current step solid. Claimants and handlers can both see at a glance where the claim stands.

## Tokens

| Token | Value | Use |
|---|---|---|
| `--ink` | `#101318` | Header shell, primary text |
| `--ink-2` | `#3c4350` | Secondary text (10:1 on white) |
| `--ink-3` | `#5d6471` | Tertiary text and labels (6:1 on white) |
| `--line` | `#dde1e6` | Borders and dividers |
| `--canvas` | `#f4f5f7` | Page background |
| `--surface` | `#ffffff` | Work surfaces |
| `--accent` | `#00736d` | Primary actions, focus, current selection (5.6:1 on white) |
| `--danger` / `--warning` / `--success` / `--info` | `#b42318` / `#8a5300` / `#1c7438` / `#1f5fa6` | State only |
| Market series | KR `#d63a72` · HK `#e07a12` · TW `#7353cf` · TH `#13917f` · MY `#2a72d6` · SG `#c08f06` | Data only |

- **Type:** IBM Plex Sans (self-hosted via `@fontsource`), on a fixed rem scale with a ratio of about 1.2. `font-variant-numeric: tabular-nums` on all figures.
- **Motion:** 150–200 ms, used only for state changes.
- **Theme:** light. The setting is office staff working in daylight.

## Decisions worth defending

- **ISO codes instead of currency symbols.** `$` would be ambiguous across HKD, TWD and SGD, so amounts read `SGD 4,200.00` and `KRW 3,500,000`.
- **No modals.** Handler actions open inline in the action panel beside the claim, so the claim stays in view while you act on it.
- **The backend decides which actions appear.** The UI renders `availableActions` from the API instead of re-implementing the state machine.
