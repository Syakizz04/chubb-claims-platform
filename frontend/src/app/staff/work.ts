import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { Session } from '../core/session';
import { live } from '../shared/live';
import { ClaimTable } from './claim-table';

/** H-3: the handler's own open claims, oldest first, split into "your move" and "waiting on claimant". */
@Component({
  selector: 'app-work',
  imports: [ClaimTable, RouterLink],
  template: `
    <div class="page">
      <div class="page-head">
        <div>
          <h1>My work</h1>
          <p>{{ session.user()?.displayName }}: your open claims, oldest first.</p>
        </div>
        <a class="btn" routerLink="/queue">Incoming{{ queueCount() !== null ? ' (' + queueCount() + ')' : '' }}</a>
      </div>

      <dl class="summary surface">
        <div><dt>Needs your action</dt><dd class="num">{{ mine().length }}</dd></div>
        <div><dt>Waiting on claimant</dt><dd class="num">{{ waiting().length }}</dd></div>
        <div><dt>Older than 30 days</dt><dd class="num" [class.alert]="overdue() > 0">{{ overdue() }}</dd></div>
      </dl>

      @if (claims.error(); as err) {
        <div class="notice notice-danger">{{ err.title }}</div>
      } @else if (claims.loading()) {
        <div class="surface section"><span class="skeleton"></span><br /><span class="skeleton"></span></div>
      } @else {
        <section class="surface block" aria-labelledby="mine-h">
          <div class="section head"><h2 id="mine-h" class="h3">Needs your action</h2></div>
          @if (mine().length) {
            <app-claim-table [claims]="mine()" />
          } @else {
            <div class="empty"><p>Nothing waiting on you. <a routerLink="/queue">Pick up a claim from the queue.</a></p></div>
          }
        </section>

        <section class="surface block" aria-labelledby="wait-h">
          <div class="section head">
            <h2 id="wait-h" class="h3">Waiting on claimant</h2>
            <p class="small muted">You've asked for information; these come back to you when the claimant replies.</p>
          </div>
          @if (waiting().length) {
            <app-claim-table [claims]="waiting()" />
          } @else {
            <div class="empty"><p class="small">No outstanding questions.</p></div>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .summary { display: grid; grid-template-columns: repeat(3, 1fr); margin: 0 0 1.5rem; }
    .summary div { padding: 1rem 1.5rem; }
    .summary div + div { border-left: 1px solid var(--line); }
    .summary dt { font-size: var(--step--1); color: var(--ink-3); }
    .summary dd { margin: 0.25rem 0 0; font-size: var(--step-2); font-weight: 600; }
    .alert { color: var(--danger); }
    .block { margin-bottom: 1.5rem; }
    .head { padding-bottom: 0.75rem; }
    .head h2 { margin-bottom: 0.25rem; }
    .h3 { font-size: var(--step-1); }
    @media (max-width: 640px) { .summary { grid-template-columns: 1fr; } .summary div + div { border-left: 0; border-top: 1px solid var(--line); } }
  `,
})
export class Work {
  private api = inject(Api);
  readonly session = inject(Session);

  readonly claims = live(() => this.api.assignedToMe(), 15_000);
  private readonly queue = live(() => this.api.queue(), 30_000);

  readonly mine = computed(() => (this.claims.value() ?? []).filter(c => c.status !== 'AwaitingInfo'));
  readonly waiting = computed(() => (this.claims.value() ?? []).filter(c => c.status === 'AwaitingInfo'));
  readonly overdue = computed(() => (this.claims.value() ?? []).filter(c => c.ageDays > 30).length);
  readonly queueCount = computed(() => this.queue.value()?.length ?? null);
}
