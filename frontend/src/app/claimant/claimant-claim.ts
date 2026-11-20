import { DatePipe } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, toProblem } from '../core/api';
import { AgoPipe, MoneyPipe } from '../core/format';
import { Problem } from '../core/models';
import { live } from '../shared/live';
import { StatusPill, StatusTrack } from '../shared/status';
import { Timeline } from '../shared/timeline';

/** C-4 / C-5 / C-6 / C-7: the claimant's view of one claim. Polls, so decisions appear without a refresh. */
@Component({
  selector: 'app-claimant-claim',
  imports: [RouterLink, DatePipe, FormsModule, MoneyPipe, AgoPipe, StatusPill, StatusTrack, Timeline],
  template: `
    <div class="page">
      <a routerLink="/my-claims" class="back small">← My claims</a>

      @if (claim.error(); as err) {
        <div class="notice notice-danger">{{ err.status === 404 ? "We couldn't find that claim." : err.title }}</div>
      } @else if (claim.value(); as c) {
        @if (justSubmitted) {
          <div class="notice notice-success banner" role="status">
            <strong>Claim {{ c.reference }} received.</strong> A claims handler will pick it up shortly. Keep this reference for any calls with us.
          </div>
        }

        <div class="page-head">
          <div>
            <h1>{{ c.reference }}</h1>
            <p>{{ c.type }} claim · submitted {{ c.submittedAt | date: 'd MMM y' }}</p>
          </div>
          <app-status [status]="c.status" />
        </div>

        <div class="surface track-wrap"><app-status-track [status]="c.status" /></div>

        <div class="split">
          <div class="main">
            @if (c.openInformationRequest; as q) {
              <section class="surface ask" aria-labelledby="ask-h">
                <div class="section">
                  <h2 id="ask-h" class="h3">The claims team needs more information</h2>
                  <blockquote>{{ q.question }}</blockquote>
                  <p class="small muted">Asked {{ q.requestedAt | ago }}</p>
                  <form (ngSubmit)="respond(c.id)" class="reply">
                    <div class="field">
                      <label for="reply">Your reply</label>
                      <textarea id="reply" class="input" maxlength="1000" name="reply" [(ngModel)]="reply" rows="4" required
                                [attr.aria-invalid]="replyError() ? true : null"></textarea>
                      @if (replyError(); as e) { <span class="error">{{ e }}</span> }
                    </div>
                    <div><button class="btn btn-primary" type="submit" [disabled]="busy()" [attr.aria-busy]="busy()">{{ busy() ? 'Sending…' : 'Send reply' }}</button></div>
                  </form>
                </div>
              </section>
            }

            @if (c.status === 'Approved' || c.status === 'Settled') {
              <section class="surface decision good">
                <div class="section">
                  <h2 class="h3">{{ c.status === 'Settled' ? 'Your claim has been settled' : 'Your claim has been approved' }}</h2>
                  <dl class="facts">
                    <div><dt>Approved amount</dt><dd class="num big">{{ c.approvedAmount | money: c.currencyCode : c.minorUnits }}</dd></div>
                    @if (c.settledAmount !== null) {
                      <div><dt>Paid</dt><dd class="num">{{ c.settledAmount | money: c.currencyCode : c.minorUnits }} on {{ c.closedAt | date: 'd MMM y' }}</dd></div>
                    } @else {
                      <div><dt>Next</dt><dd>Payment is being arranged.</dd></div>
                    }
                  </dl>
                </div>
              </section>
            }
            @if (c.status === 'Rejected') {
              <section class="surface decision bad">
                <div class="section">
                  <h2 class="h3">We couldn't approve this claim</h2>
                  <p>{{ c.rejectionReason }}</p>
                  <p class="small muted" style="margin-top: 0.75rem">If you think we've got something wrong, contact us with your claim reference and we'll review it.</p>
                </div>
              </section>
            }

            <section class="surface" aria-labelledby="tl-h">
              <div class="section">
                <h2 id="tl-h" class="h3">Activity</h2>
                <app-timeline [items]="c.timeline" [currency]="c.currencyCode" [minorUnits]="c.minorUnits" />
              </div>
            </section>
          </div>

          <aside class="surface">
            <div class="section">
              <h2 class="h3">Claim details</h2>
              <dl class="facts one">
                <div><dt>Claimed</dt><dd class="num">{{ c.claimedAmount | money: c.currencyCode : c.minorUnits }}</dd></div>
                <div><dt>Policy</dt><dd>{{ c.policyNumber }}</dd></div>
                <div><dt>Incident date</dt><dd>{{ c.incidentDate | date: 'd MMM y' }}</dd></div>
                <div><dt>Location</dt><dd>{{ c.incidentLocation }}</dd></div>
                @if (c.vehicleRegistration) { <div><dt>Vehicle</dt><dd>{{ c.vehicleRegistration }}</dd></div> }
                @if (c.propertyAddress) { <div><dt>Property</dt><dd>{{ c.propertyAddress }}</dd></div> }
              </dl>
            </div>
            <div class="section">
              <h3 class="small muted label">What you told us</h3>
              <p class="desc">{{ c.description }}</p>
            </div>
            <div class="section small muted">Updated {{ c.updatedAt | ago }}. This page refreshes automatically.</div>
          </aside>
        </div>
      } @else {
        <div class="surface section"><span class="skeleton"></span><br /><span class="skeleton"></span></div>
      }
    </div>
  `,
  styles: `
    .back { display: inline-block; margin-bottom: 1rem; text-decoration: none; }
    .banner { margin-bottom: 1.25rem; }
    .track-wrap { padding: 1.25rem 1.5rem; margin-bottom: 1.5rem; }
    .main { display: grid; gap: 1.5rem; }
    .h3 { font-size: var(--step-1); }
    .ask { border-color: color-mix(in srgb, var(--warning) 40%, white); }
    .ask .section { background: var(--warning-tint); }
    blockquote { margin: 0 0 0.375rem; font-size: var(--step-1); line-height: 1.45; max-width: 65ch; }
    .reply { display: grid; gap: 0.75rem; margin-top: 1rem; }
    .reply textarea { background: var(--surface); }
    .decision.good .section { background: var(--success-tint); }
    .decision.bad .section { background: var(--danger-tint); }
    .big { font-size: var(--step-2); font-weight: 600; }
    .facts.one { grid-template-columns: 1fr; gap: 0.75rem; }
    .label { font-weight: 500; margin-bottom: 0.375rem; }
    .desc { white-space: pre-line; color: var(--ink-2); }
  `,
})
export class ClaimantClaim {
  private api = inject(Api);
  readonly id = input.required<string>();

  readonly justSubmitted = !!history.state?.justSubmitted;
  readonly claim = live(() => this.api.myClaim(this.id()), 10_000);

  reply = '';
  readonly busy = signal(false);
  readonly replyError = signal<string | null>(null);

  respond(id: string) {
    if (!this.reply.trim()) { this.replyError.set('Please write a reply before sending.'); return; }
    this.busy.set(true);
    this.replyError.set(null);
    this.api.respond(id, this.reply.trim()).subscribe({
      next: c => { this.claim.set(c); this.reply = ''; this.busy.set(false); },
      error: e => {
        const p: Problem = toProblem(e);
        this.replyError.set(p.errors?.['response']?.[0] ?? p.detail ?? p.title ?? 'Could not send your reply.');
        this.busy.set(false);
        this.claim.refresh();
      },
    });
  }
}
