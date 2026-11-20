import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { Api, problemMessage, toProblem } from '../core/api';
import { MoneyPipe, formatMoney } from '../core/format';
import { StaffAction, StaffClaimDetail } from '../core/models';
import { Session } from '../core/session';
import { live } from '../shared/live';
import { MarketTag, StatusPill, StatusTrack } from '../shared/status';
import { Timeline } from '../shared/timeline';

/**
 * H-2 .. H-11: the handler's workspace for one claim. The action panel renders only the actions the
 * API says are available (availableActions). Each one opens inline, never in a modal, so the claim
 * stays in view while you act.
 */
@Component({
  selector: 'app-staff-claim',
  imports: [RouterLink, DatePipe, FormsModule, MoneyPipe, StatusPill, StatusTrack, Timeline, MarketTag],
  template: `
    <div class="page">
      <a [routerLink]="backLink()" class="back small">← {{ backLabel() }}</a>

      @if (claim.error(); as err) {
        <div class="notice notice-danger">{{ err.status === 404 ? 'Claim not found.' : err.title }}</div>
      } @else if (claim.value(); as c) {
        <div class="page-head">
          <div>
            <h1>{{ c.reference }}</h1>
            <p>
              <app-market [code]="c.marketCode" /> {{ api.marketName(c.marketCode) }} · {{ c.type }} ·
              {{ c.claimantName }} · {{ c.ageDays }} {{ c.ageDays === 1 ? 'day' : 'days' }} old
            </p>
          </div>
          <app-status [status]="c.status" />
        </div>

        <div class="surface track-wrap"><app-status-track [status]="c.status" /></div>

        <div class="split">
          <div class="main">
            <section class="surface">
              <div class="section">
                <dl class="facts money">
                  <div><dt>Claimed</dt><dd class="num">{{ c.claimedAmount | money: c.currencyCode : c.minorUnits }}</dd></div>
                  <div><dt>Reserve</dt><dd class="num strong">{{ c.reserveAmount | money: c.currencyCode : c.minorUnits }}</dd></div>
                  @if (c.approvedAmount !== null) { <div><dt>Approved</dt><dd class="num">{{ c.approvedAmount | money: c.currencyCode : c.minorUnits }}</dd></div> }
                  @if (c.settledAmount !== null) { <div><dt>Paid</dt><dd class="num">{{ c.settledAmount | money: c.currencyCode : c.minorUnits }}</dd></div> }
                </dl>
              </div>
              <div class="section">
                <dl class="facts">
                  <div><dt>Policy</dt><dd>{{ c.policyNumber }}</dd></div>
                  <div><dt>Incident date</dt><dd>{{ c.incidentDate | date: 'd MMM y' }}</dd></div>
                  <div><dt>Location</dt><dd>{{ c.incidentLocation }}</dd></div>
                  @if (c.vehicleRegistration) { <div><dt>Vehicle</dt><dd>{{ c.vehicleRegistration }}</dd></div> }
                  @if (c.propertyAddress) { <div><dt>Property</dt><dd>{{ c.propertyAddress }}</dd></div> }
                  <div><dt>Handler</dt><dd>{{ c.assignedHandlerName ?? 'Unassigned' }}</dd></div>
                </dl>
                <h3 class="label small">Claimant's description</h3>
                <p class="desc">{{ c.description }}</p>
              </div>
              @if (c.informationRequests.length) {
                <div class="section">
                  <h3 class="label small">Information requests</h3>
                  @for (r of c.informationRequests; track r.id) {
                    <div class="qa">
                      <p><strong>Q:</strong> {{ r.question }}</p>
                      <p [class.muted]="!r.response"><strong>A:</strong> {{ r.response ?? 'Awaiting reply…' }}</p>
                    </div>
                  }
                </div>
              }
              @if (c.rejectionReason) {
                <div class="section"><h3 class="label small">Rejection reason</h3><p>{{ c.rejectionReason }}</p></div>
              }
            </section>

            <section class="surface">
              <div class="section">
                <h2 class="h3">Timeline</h2>
                <app-timeline [items]="c.timeline" [currency]="c.currencyCode" [minorUnits]="c.minorUnits" />
              </div>
            </section>
          </div>

          <aside class="surface panel" aria-labelledby="act-h">
            <div class="section">
              <h2 id="act-h" class="h3">Actions</h2>

              @if (message(); as m) { <div class="notice notice-{{ m.kind }} msg" role="status">{{ m.text }}</div> }

              @if (has(c, 'PickUp')) {
                <button class="btn btn-primary wide" (click)="run(api.pickUp(c.id), 'Claim assigned to you.')" [disabled]="busy()">Pick up claim</button>
              }

              @if (!open()) {
                <div class="list">
                  @for (a of panelActions(c); track a.key) {
                    <button class="btn wide" [class.btn-danger]="a.key === 'Reject'" (click)="choose(a.key, c)">{{ a.label }}</button>
                  }
                </div>
                @if (!panelActions(c).length && !has(c, 'PickUp')) {
                  <p class="small muted">{{ idleText(c) }}</p>
                }
              } @else {
                <form class="form" (ngSubmit)="submit(c)">
                  @switch (open()) {
                    @case ('RequestInfo') {
                      <div class="field">
                        <label for="q">Question for the claimant</label>
                        <textarea id="q" class="input" maxlength="1000" name="q" [(ngModel)]="text" rows="4" placeholder="e.g. Please send photos of the damage and the repair quote."></textarea>
                        <span class="hint">The claimant sees this exactly as written.</span>
                      </div>
                    }
                    @case ('AdjustReserve') {
                      <div class="field">
                        <label for="r">New reserve</label>
                        <div class="input-group"><span class="addon">{{ c.currencyCode }}</span>
                          <input id="r" class="input num" type="number" name="r" [(ngModel)]="amount" [step]="c.minorUnits === 0 ? 1 : 0.01" min="0" /></div>
                        <span class="hint">Currently {{ c.reserveAmount | money: c.currencyCode : c.minorUnits }}. Internal: never shown to the claimant.</span>
                      </div>
                      <div class="field">
                        <label for="rr">Reason</label>
                        <input id="rr" class="input" maxlength="1000" name="rr" [(ngModel)]="text" placeholder="e.g. Repair quote received" />
                      </div>
                    }
                    @case ('Approve') {
                      <div class="field">
                        <label for="a">Approved amount</label>
                        <div class="input-group"><span class="addon">{{ c.currencyCode }}</span>
                          <input id="a" class="input num" type="number" name="a" [(ngModel)]="amount" [step]="c.minorUnits === 0 ? 1 : 0.01" min="0" /></div>
                        <span class="hint">Up to the claimed {{ c.claimedAmount | money: c.currencyCode : c.minorUnits }}.</span>
                      </div>
                      <div class="field">
                        <label for="an">Note to claimant (optional)</label>
                        <input id="an" class="input" maxlength="1000" name="an" [(ngModel)]="text" />
                      </div>
                    }
                    @case ('Reject') {
                      <div class="field">
                        <label for="rj">Reason</label>
                        <textarea id="rj" class="input" maxlength="1000" name="rj" [(ngModel)]="text" rows="4"></textarea>
                        <span class="hint">The claimant sees this reason. The reserve is released.</span>
                      </div>
                    }
                    @case ('Settle') {
                      <p>Record payment of <strong class="num">{{ c.approvedAmount | money: c.currencyCode : c.minorUnits }}</strong> and close the claim. The reserve is released.</p>
                    }
                    @case ('AddNote') {
                      <div class="field">
                        <label for="n">Internal note</label>
                        <textarea id="n" class="input" maxlength="1000" name="n" [(ngModel)]="text" rows="4"></textarea>
                        <span class="hint">Visible to staff only.</span>
                      </div>
                    }
                  }
                  @if (formError(); as e) { <span class="error small">{{ e }}</span> }
                  <div class="row">
                    <button type="button" class="btn" (click)="close()">Cancel</button>
                    <button type="submit" class="btn" [class.btn-primary]="open() !== 'Reject'" [class.btn-danger]="open() === 'Reject'"
                            [disabled]="busy()" [attr.aria-busy]="busy()">{{ busy() ? 'Saving…' : submitLabel() }}</button>
                  </div>
                </form>
              }
            </div>
          </aside>
        </div>
      } @else {
        <div class="surface section"><span class="skeleton"></span><br /><span class="skeleton"></span></div>
      }
    </div>
  `,
  styles: `
    .back { display: inline-block; margin-bottom: 1rem; text-decoration: none; }
    .page-head p { display: flex; align-items: center; gap: 0.375rem; flex-wrap: wrap; }
    .track-wrap { padding: 1.25rem 1.5rem; margin-bottom: 1.5rem; }
    .main { display: grid; gap: 1.5rem; }
    .h3 { font-size: var(--step-1); }
    .money dd { font-size: var(--step-1); }
    .strong { font-weight: 600; }
    .label { font-weight: 500; color: var(--ink-3); margin: 1.25rem 0 0.375rem; }
    .section > .label:first-child { margin-top: 0; }
    .desc { white-space: pre-line; color: var(--ink-2); max-width: 70ch; }
    .qa { padding: 0.625rem 0; border-top: 1px solid var(--line); display: grid; gap: 0.25rem; }
    .qa:first-of-type { border-top: 0; padding-top: 0; }
    .panel { position: sticky; top: 1rem; }
    .list, .form { display: grid; gap: 0.625rem; }
    .form { gap: 1rem; }
    .wide { width: 100%; justify-content: flex-start; }
    .msg { margin-bottom: 1rem; }
    .row { display: flex; justify-content: flex-end; gap: 0.5rem; }
    .list + p { margin-top: 0.5rem; }
  `,
})
export class StaffClaim {
  readonly api = inject(Api);
  private session = inject(Session);
  readonly id = input.required<string>();

  readonly claim = live(() => this.api.staffClaim(this.id()), 15_000);

  readonly open = signal<StaffAction | null>(null);
  readonly busy = signal(false);
  readonly formError = signal<string | null>(null);
  readonly message = signal<{ kind: 'success' | 'danger'; text: string } | null>(null);
  text = '';
  amount: number | null = null;

  readonly backLink = computed(() => (this.session.role() === 'Manager' ? '/dashboard' : '/work'));
  readonly backLabel = computed(() => (this.session.role() === 'Manager' ? 'Dashboard' : 'My work'));

  private readonly labels: Record<string, string> = {
    RequestInfo: 'Request information', AdjustReserve: 'Adjust reserve', Approve: 'Approve',
    Settle: 'Settle and close', Reject: 'Reject', AddNote: 'Add internal note',
  };

  has(c: StaffClaimDetail, a: StaffAction) { return c.availableActions.includes(a); }

  panelActions(c: StaffClaimDetail) {
    const order: StaffAction[] = ['Approve', 'Settle', 'RequestInfo', 'AdjustReserve', 'AddNote', 'Reject'];
    return order.filter(a => this.has(c, a)).map(key => ({ key, label: this.labels[key] }));
  }

  idleText(c: StaffClaimDetail) {
    if (c.status === 'Settled' || c.status === 'Rejected') return 'This claim is closed.';
    return `Assigned to ${c.assignedHandlerName}. Only they can act on it.`;
  }

  submitLabel() { return this.labels[this.open()!] ?? 'Save'; }

  choose(a: StaffAction, c: StaffClaimDetail) {
    this.message.set(null);
    this.formError.set(null);
    this.text = '';
    this.amount = a === 'Approve' ? Math.min(c.reserveAmount, c.claimedAmount) || c.claimedAmount
      : a === 'AdjustReserve' ? c.reserveAmount : null;
    this.open.set(a);
  }

  close() { this.open.set(null); this.formError.set(null); }

  submit(c: StaffClaimDetail) {
    const t = this.text.trim();
    const need = (cond: boolean, msg: string) => { if (cond) this.formError.set(msg); return cond; };
    switch (this.open()) {
      case 'RequestInfo': if (need(!t, 'Write a question for the claimant.')) return;
        return this.run(this.api.requestInfo(c.id, t), 'Question sent. The claim is now waiting on the claimant.');
      case 'AdjustReserve': if (need(this.amount === null || this.amount < 0, 'Enter a reserve of zero or more.') || need(!t, 'Give a reason for the change.')) return;
        return this.run(this.api.adjustReserve(c.id, Number(this.amount), t), `Reserve set to ${formatMoney(this.amount, c.currencyCode, c.minorUnits)}.`);
      case 'Approve': if (need(!this.amount || this.amount <= 0, 'Enter the approved amount.')) return;
        return this.run(this.api.approve(c.id, Number(this.amount), t || null), 'Claim approved. The claimant has been notified.');
      case 'Reject': if (need(!t, 'A reason is required. The claimant will see it.')) return;
        return this.run(this.api.reject(c.id, t), 'Claim rejected and closed.');
      case 'Settle':
        return this.run(this.api.settle(c.id), 'Claim settled and closed.');
      case 'AddNote': if (need(!t, 'Write a note.')) return;
        return this.run(this.api.addNote(c.id, t), 'Note added.');
    }
  }

  run(call: Observable<StaffClaimDetail>, success: string) {
    this.busy.set(true);
    this.formError.set(null);
    call.subscribe({
      next: updated => {
        this.claim.set(updated);
        this.busy.set(false);
        this.open.set(null);
        this.message.set({ kind: 'success', text: success });
      },
      error: e => {
        const p = toProblem(e);
        this.busy.set(false);
        const field = p.errors ? Object.values(p.errors)[0]?.[0] : null;
        if (field) { this.formError.set(field); return; }
        this.open.set(null);
        this.message.set({ kind: 'danger', text: problemMessage(p) });
        this.claim.refresh(); // e.g. 409: show the claim as it really is now
      },
    });
  }
}
