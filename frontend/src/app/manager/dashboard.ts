import { DatePipe, PercentPipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { AgoPipe, MoneyPipe, UsdPipe } from '../core/format';
import { live } from '../shared/live';
import { MarketTag } from '../shared/status';

/**
 * M-1 .. M-6, served by the Insights service from its Kafka-fed read model. Polls every 5 s, and
 * "Updated …" shows how fresh the read model is (eventual consistency, made visible).
 */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DatePipe, PercentPipe, MoneyPipe, UsdPipe, AgoPipe, MarketTag],
  template: `
    <div class="page">
      <div class="page-head">
        <div>
          <h1>Claims overview</h1>
          <p>Outstanding liability and team workload across Asia Pacific.</p>
        </div>
        @if (report.value(); as r) {
          <p class="fresh small muted" [title]="'Read model last updated ' + (r.lastProcessedAt | date: 'medium')">
            <span class="live" aria-hidden="true"></span> Updated {{ r.lastProcessedAt | ago }}
          </p>
        }
      </div>

      @if (report.error(); as err) {
        <div class="notice notice-danger">
          The Insights service isn't responding ({{ err.title }}). Claim processing is unaffected; this view catches up when it's back.
        </div>
      } @else if (report.value(); as r) {

        <!-- Signature: the market ledger. Local currency is the liability; USD is the lens. -->
        <section class="surface ledger" aria-labelledby="exp-h">
          <div class="section">
            <div class="ledger-head">
              <h2 id="exp-h">Outstanding exposure <span class="num total">{{ r.exposure.totalUsd | usd }}</span></h2>
              <p class="small muted">{{ r.exposure.openClaims }} open claims · reserves on claims not yet settled or rejected</p>
            </div>

            <div class="bar" role="img" [attr.aria-label]="barLabel()">
              @for (m of r.exposure.markets; track m.marketCode) {
                <span class="seg" [style.flex-grow]="m.amountUsd" [style.background]="'var(--m-' + m.marketCode + ')'"
                      [title]="api.marketName(m.marketCode) + ': ' + (m.amountUsd | usd)"></span>
              }
            </div>

            <div class="table-wrap">
              <table class="data">
                <thead>
                  <tr><th>Market</th><th class="right">Exposure (local)</th><th class="right">USD equivalent</th><th class="right">Share</th>
                      <th class="right">Motor</th><th class="right">Property</th><th class="right">Open</th></tr>
                </thead>
                <tbody>
                  @for (m of r.exposure.markets; track m.marketCode) {
                    <tr>
                      <td class="nowrap"><app-market [code]="m.marketCode" /> <span class="mname">{{ api.marketName(m.marketCode) }}</span></td>
                      <td class="right num strong">{{ m.amount | money: m.currencyCode }}</td>
                      <td class="right num muted">{{ m.amountUsd | usd }}</td>
                      <td class="right num">{{ share(m.amountUsd) | percent: '1.0-0' }}</td>
                      <td class="right num">{{ typeAmount(m.byType, 'Motor') ? (typeAmount(m.byType, 'Motor') | money: m.currencyCode) : '—' }}</td>
                      <td class="right num">{{ typeAmount(m.byType, 'Property') ? (typeAmount(m.byType, 'Property') | money: m.currencyCode) : '—' }}</td>
                      <td class="right num">{{ m.openClaims }}</td>
                    </tr>
                  } @empty {
                    <tr><td colspan="7" class="muted">No open claims, so there is no outstanding exposure.</td></tr>
                  }
                </tbody>
              </table>
            </div>
            <p class="small muted rates">{{ r.exposure.ratesNote }}</p>
          </div>
        </section>

        <div class="grid">
          <section class="surface" aria-labelledby="wl-h">
            <div class="section">
              <h2 id="wl-h" class="h3">Team workload</h2>
              <p class="small queue">
                <a routerLink="/queue"><strong class="num">{{ r.workload.unassignedCount }}</strong> unassigned</a>
                @if (r.workload.oldestUnassignedDays !== null) {
                  <span class="muted"> · oldest waiting {{ r.workload.oldestUnassignedDays }} days</span>
                }
              </p>
            </div>
            <div class="table-wrap">
              <table class="data">
                <thead><tr><th>Handler</th><th class="right">Open</th><th class="right">Awaiting info</th><th class="right">Oldest</th><th class="right">Reserves (USD)</th></tr></thead>
                <tbody>
                  @for (h of r.workload.handlers; track h.handlerId) {
                    <tr>
                      <td>{{ h.handlerName }}</td>
                      <td class="right num">{{ h.openClaims }}</td>
                      <td class="right num">{{ h.awaitingInfo }}</td>
                      <td class="right num" [class.alert]="(h.oldestOpenDays ?? 0) > 30">{{ h.oldestOpenDays }}d</td>
                      <td class="right num">{{ h.reserveUsd | usd }}</td>
                    </tr>
                  } @empty { <tr><td colspan="5" class="muted">No claims assigned yet.</td></tr> }
                </tbody>
              </table>
            </div>
          </section>

          <section class="surface" aria-labelledby="age-h">
            <div class="section">
              <h2 id="age-h" class="h3">Ageing of open claims</h2>
              <ul class="ageing">
                @for (b of r.ageing; track b.label) {
                  <li>
                    <span class="lbl">{{ b.label }}</span>
                    <span class="track"><span class="fill" [class.late]="b.minDays > 30" [style.width.%]="ageShare(b.count)"></span></span>
                    <span class="num cnt">{{ b.count }}</span>
                    <span class="num muted usd">{{ b.reserveUsd | usd }}</span>
                  </li>
                }
              </ul>
            </div>
          </section>
        </div>

        <section class="surface perf" aria-labelledby="perf-h">
          <div class="section"><h2 id="perf-h" class="h3">Decisions in the last 30 days</h2></div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Handler</th><th class="right">Decided</th><th class="right">Approved</th><th class="right">Rejected</th><th class="right">Avg. days to decision</th></tr></thead>
              <tbody>
                @for (p of r.performance; track p.handlerId) {
                  <tr>
                    <td>{{ p.handlerName }}</td>
                    <td class="right num">{{ p.decided30d }}</td>
                    <td class="right num">{{ p.approved30d }}</td>
                    <td class="right num">{{ p.rejected30d }}</td>
                    <td class="right num">{{ p.avgDaysToDecision ?? '—' }}</td>
                  </tr>
                } @empty { <tr><td colspan="5" class="muted">No decisions in the last 30 days.</td></tr> }
              </tbody>
            </table>
          </div>
        </section>
      } @else {
        <div class="surface section"><span class="skeleton"></span><br /><span class="skeleton"></span><br /><span class="skeleton"></span></div>
      }
    </div>
  `,
  styles: `
    .fresh { display: inline-flex; align-items: center; gap: 0.5rem; }
    .live { width: 8px; height: 8px; border-radius: 50%; background: var(--success); box-shadow: 0 0 0 3px var(--success-tint); }
    .ledger { margin-bottom: 1.5rem; }
    .ledger-head { margin-bottom: 1rem; }
    .ledger-head h2 { display: flex; align-items: baseline; gap: 0.75rem; flex-wrap: wrap; }
    .total { font-size: var(--step-3); letter-spacing: -0.02em; }
    .ledger-head p { margin-top: 0.25rem; }
    .bar { display: flex; height: 14px; gap: 2px; margin-bottom: 1rem; border-radius: var(--radius); overflow: hidden; background: var(--panel); }
    .seg { flex-basis: 0; min-width: 3px; transition: flex-grow 250ms var(--ease); }
    .mname { margin-left: 0.25rem; }
    .nowrap { white-space: nowrap; }
    .strong { font-weight: 600; }
    .rates { margin-top: 0.75rem; }
    .grid { display: grid; grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); gap: 1.5rem; margin-bottom: 1.5rem; }
    @media (max-width: 960px) { .grid { grid-template-columns: minmax(0, 1fr); } }
    @media (max-width: 560px) { .ageing li { grid-template-columns: 5.5rem 1fr 1.5rem; } .usd { display: none; } }
    .h3 { font-size: var(--step-1); }
    .queue { margin-top: -0.25rem; }
    .alert { color: var(--danger); font-weight: 600; }
    .ageing { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.875rem; }
    .ageing li { display: grid; grid-template-columns: 6rem 1fr 2rem 6.5rem; align-items: center; gap: 0.75rem; }
    .lbl { font-size: var(--step--1); color: var(--ink-2); }
    .track { height: 10px; background: var(--panel); border-radius: var(--radius); overflow: hidden; }
    .fill { display: block; height: 100%; background: var(--ink-2); transition: width 250ms var(--ease); }
    .fill.late { background: var(--danger); }
    .cnt { text-align: right; font-weight: 600; }
    .usd { text-align: right; font-size: var(--step--1); }
    .perf .section { padding-bottom: 0.25rem; }
  `,
})
export class Dashboard {
  readonly api = inject(Api);
  readonly report = live(() => this.api.dashboard(), 5_000);

  private readonly total = computed(() => this.report.value()?.exposure.totalUsd ?? 0);
  private readonly maxAge = computed(() => Math.max(1, ...(this.report.value()?.ageing ?? []).map(b => b.count)));

  share(usd: number) { return this.total() ? usd / this.total() : 0; }
  ageShare(count: number) { return (count / this.maxAge()) * 100; }
  typeAmount(types: { claimType: string; amount?: number }[], t: string) {
    return types.find(x => x.claimType === t)?.amount ?? 0;
  }

  barLabel() {
    const r = this.report.value();
    if (!r) return '';
    return 'Exposure by market: ' + r.exposure.markets
      .map(m => `${this.api.marketName(m.marketCode)} ${Math.round(this.share(m.amountUsd) * 100)}%`).join(', ');
  }
}
