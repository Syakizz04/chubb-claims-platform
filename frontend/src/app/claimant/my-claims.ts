import { DatePipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { AgoPipe, MoneyPipe } from '../core/format';
import { MarketTag, StatusPill } from '../shared/status';
import { live } from '../shared/live';

@Component({
  selector: 'app-my-claims',
  imports: [RouterLink, DatePipe, MoneyPipe, AgoPipe, StatusPill, MarketTag],
  template: `
    <div class="page">
      <div class="page-head">
        <div>
          <h1>My claims</h1>
          <p>Track progress and reply to the claims team when they need something from you.</p>
        </div>
        <a class="btn btn-primary" routerLink="/new-claim">Report a claim</a>
      </div>

      @if (actionNeeded().length) {
        <div class="notice notice-warning attention" role="status">
          <strong>The claims team needs information from you</strong> on
          @for (c of actionNeeded(); track c.id; let last = $last) {
            <a [routerLink]="['/my-claims', c.id]">{{ c.reference }}</a>{{ last ? '.' : ', ' }}
          }
        </div>
      }

      <div class="layout">
        <section class="surface" aria-labelledby="claims-h">
          <h2 id="claims-h" class="visually-hidden">Claims</h2>
          @if (claims.error(); as err) {
            <div class="section"><div class="notice notice-danger">{{ err.title }}</div></div>
          } @else if (claims.loading()) {
            <div class="section"><span class="skeleton"></span><br /><span class="skeleton"></span></div>
          } @else if (!claims.value()?.length) {
            <div class="empty">
              <h3>No claims yet</h3>
              <p>When something happens to your car or home, report it here. You'll get a reference straight away and can follow every step.</p>
              <p style="margin-top: 1rem"><a class="btn btn-primary" routerLink="/new-claim">Report a claim</a></p>
            </div>
          } @else {
            <div class="table-wrap">
              <table class="data">
                <thead>
                  <tr><th>Reference</th><th>Type</th><th>Status</th><th class="right">Claimed</th><th>Submitted</th><th>Last update</th></tr>
                </thead>
                <tbody>
                  @for (c of claims.value(); track c.id) {
                    <tr class="link" (click)="open(c.id)">
                      <td class="ref"><a [routerLink]="['/my-claims', c.id]" (click)="$event.stopPropagation()">{{ c.reference }}</a></td>
                      <td>{{ c.type }} <app-market [code]="c.marketCode" /></td>
                      <td><app-status [status]="c.status" /></td>
                      <td class="right num">{{ c.claimedAmount | money: c.currencyCode }}</td>
                      <td class="num">{{ c.submittedAt | date: 'd MMM y' }}</td>
                      <td class="muted">{{ c.updatedAt | ago }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>

        <aside class="surface" aria-labelledby="updates-h">
          <div class="section">
            <h2 id="updates-h" class="h3">Updates</h2>
            @for (n of notifications.value() ?? []; track n.id) {
              <div class="update">
                <p>{{ n.message }}</p>
                <span class="small muted">{{ n.createdAt | ago }}</span>
              </div>
            } @empty {
              <p class="small muted">We'll post updates here as your claims progress.</p>
            }
          </div>
        </aside>
      </div>
    </div>
  `,
  styles: `
    .attention { margin-bottom: 1.25rem; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 1.5rem; align-items: start; }
    @media (max-width: 960px) { .layout { grid-template-columns: 1fr; } }
    .h3 { font-size: var(--step-1); }
    .update { padding: 0.75rem 0; border-top: 1px solid var(--line); }
    .update:first-of-type { border-top: 0; padding-top: 0; }
    .update p { margin-bottom: 0.25rem; }
    td app-market { margin-left: 0.375rem; }
  `,
})
export class MyClaims {
  private api = inject(Api);
  private router = inject(Router);

  readonly claims = live(() => this.api.myClaims(), 15_000);
  readonly notifications = live(() => this.api.notifications(), 15_000);
  readonly actionNeeded = computed(() => (this.claims.value() ?? []).filter(c => c.actionRequired));

  open(id: string) { this.router.navigate(['/my-claims', id]); }
}
