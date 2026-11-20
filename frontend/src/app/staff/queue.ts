import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { Api, problemMessage, toProblem } from '../core/api';
import { ClaimType } from '../core/models';
import { Session } from '../core/session';
import { live } from '../shared/live';
import { ClaimTable } from './claim-table';
import { Pager, pageOf } from '../shared/pager';

/** H-1 / H-2: unassigned claims, oldest first, with market and type filters. */
@Component({
  selector: 'app-queue',
  imports: [ClaimTable, Pager],
  template: `
    <div class="page">
      <div class="page-head">
        <div>
          <h1>Incoming claims</h1>
          <p>Unassigned claims, oldest first. Picking one up assigns it to you.</p>
        </div>
        <div class="filters">
          <label class="field">
            <span class="small muted">Market</span>
            <select class="input" [value]="market() ?? ''" (change)="market.set($any($event.target).value || null)">
              <option value="">All markets</option>
              @for (m of api.markets(); track m.code) { <option [value]="m.code">{{ m.name }}</option> }
            </select>
          </label>
          <label class="field">
            <span class="small muted">Type</span>
            <select class="input" [value]="type() ?? ''" (change)="type.set($any($event.target).value || null)">
              <option value="">All types</option>
              <option value="Motor">Motor</option>
              <option value="Property">Property</option>
            </select>
          </label>
        </div>
      </div>

      @if (message(); as m) { <div class="notice notice-danger msg" role="alert">{{ m }}</div> }

      <section class="surface">
        @if (queue.error(); as err) {
          <div class="section"><div class="notice notice-danger">{{ err.title }}</div></div>
        } @else if (queue.loading()) {
          <div class="section"><span class="skeleton"></span><br /><span class="skeleton"></span></div>
        } @else if (!filtered().length) {
          <div class="empty">
            <h3>{{ market() || type() ? 'Nothing matches these filters' : 'The queue is clear' }}</h3>
            <p>{{ market() || type() ? 'Try all markets or both claim types.' : 'New claims appear here as soon as claimants submit them.' }}</p>
          </div>
        } @else {
          <app-claim-table [claims]="pageClaims()" [actionLabel]="isHandler() ? 'Pick up' : null" [busyId]="busyId()" (act)="pickUp($event)" />
          <app-pager [total]="filtered().length" [(page)]="page" label="Incoming claims pages" />
        }
      </section>
      <p class="small muted foot">{{ filtered().length }} waiting · refreshes every 10 seconds</p>
    </div>
  `,
  styles: `
    .filters { display: flex; gap: 0.75rem; }
    .filters .field { gap: 0.25rem; min-width: 10rem; }
    .msg { margin-bottom: 1rem; }
    .foot { margin-top: 0.75rem; }
  `,
})
export class Queue {
  readonly api = inject(Api);
  private session = inject(Session);
  private router = inject(Router);

  readonly market = signal<string | null>(null);
  readonly type = signal<ClaimType | null>(null);
  readonly busyId = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly isHandler = computed(() => this.session.role() === 'Handler');

  // Filter on the client (the queue is small), so filter changes are instant and don't refetch.
  readonly queue = live(() => this.api.queue(), 10_000);
  readonly filtered = computed(() => (this.queue.value() ?? [])
    .filter(c => !this.market() || c.marketCode === this.market())
    .filter(c => !this.type() || c.type === this.type()));

  // 10 per page; the oldest (most urgent) claims stay on page 1.
  readonly page = signal(1);
  readonly pageClaims = computed(() => pageOf(this.filtered(), this.page()));

  constructor() {
    // A new filter starts from page 1.
    effect(() => { this.market(); this.type(); untracked(() => this.page.set(1)); });
  }

  pickUp(id: string) {
    this.busyId.set(id);
    this.message.set(null);
    this.api.pickUp(id).subscribe({
      next: c => this.router.navigate(['/claims', c.id]),
      error: e => {
        const p = toProblem(e);
        // H-2: someone else got there first. Say so plainly and refresh the queue.
        this.message.set(p.status === 409 ? 'Another handler picked up that claim a moment ago. The queue has been refreshed.' : problemMessage(p));
        this.busyId.set(null);
        this.queue.refresh();
      },
    });
  }
}
