import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
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
                  @for (c of pageClaims(); track c.id) {
                    <tr class="link" [class.synced]="c.id === hovered()" (click)="open(c.id)">
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
            @if (pageCount() > 1) {
              <nav class="pager" aria-label="Claims pages">
                <span class="small muted">{{ firstShown() }}–{{ lastShown() }} of {{ total() }}</span>
                <div class="pages">
                  <button type="button" class="btn btn-secondary btn-sm" [disabled]="page() === 1" (click)="go(page() - 1)" aria-label="Previous page">‹ Prev</button>
                  @for (p of pages(); track p) {
                    <button type="button" class="btn btn-secondary btn-sm" [class.current]="p === page()" [attr.aria-current]="p === page() ? 'page' : null" (click)="go(p)">{{ p }}</button>
                  }
                  <button type="button" class="btn btn-secondary btn-sm" [disabled]="page() === pageCount()" (click)="go(page() + 1)" aria-label="Next page">Next ›</button>
                </div>
              </nav>
            }
          }
        </section>

        <!-- Synced with the list: shows updates for the claims on the current page, matches the list's
             height and scrolls inside. Hovering an update highlights its claim; clicking opens it. -->
        <aside class="surface updates" aria-labelledby="updates-h">
          <div class="updates-inner">
            <div class="updates-head">
              <h2 id="updates-h" class="h3">Updates</h2>
              @if (pageCount() > 1) { <span class="small muted">for claims on this page</span> }
            </div>
            <ul class="updates-list" tabindex="0" aria-label="Updates for the claims shown">
              @for (n of pageUpdates(); track n.id) {
                <li>
                  <a class="update" [routerLink]="['/my-claims', n.claimId]"
                     (mouseenter)="hovered.set(n.claimId)" (mouseleave)="hovered.set(null)"
                     (focus)="hovered.set(n.claimId)" (blur)="hovered.set(null)">
                    <span class="ref small">{{ n.reference }}</span>
                    <span class="msg">{{ n.message }}</span>
                    <span class="small muted">{{ n.createdAt | ago }}</span>
                  </a>
                </li>
              } @empty {
                <li class="small muted none">We'll post updates here as your claims progress.</li>
              }
            </ul>
          </div>
        </aside>
      </div>
    </div>
  `,
  styles: `
    .attention { margin-bottom: 1.25rem; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 1.5rem; align-items: stretch; }
    .layout > section { align-self: start; }
    .h3 { font-size: var(--step-1); }
    td app-market { margin-left: 0.375rem; }
    tr.synced td { background: var(--accent-tint, #e8f4f2); }

    /* The panel takes the list's height (absolute inner box adds no height of its own) and scrolls. */
    .updates { position: relative; min-height: 22rem; }
    .updates-inner { position: absolute; inset: 0; display: flex; flex-direction: column; }
    .updates-head { display: flex; align-items: baseline; justify-content: space-between; gap: 0.5rem; padding: 1.25rem 1.25rem 0.75rem; border-bottom: 1px solid var(--line); }
    .updates-list { list-style: none; margin: 0; padding: 0 0.5rem; overflow-y: auto; flex: 1; overscroll-behavior: contain; }
    .updates-list li + li { border-top: 1px solid var(--line); }
    .update { display: grid; gap: 0.125rem; padding: 0.75rem; margin: 0 -0.0rem; color: inherit; text-decoration: none; border-radius: var(--radius); }
    .update:hover, .update:focus-visible { background: var(--panel); }
    .update .ref { font-weight: 600; color: var(--accent); }
    .update .msg { line-height: 1.4; }
    .none { padding: 1rem 0.75rem; }

    .pager { display: flex; align-items: center; justify-content: space-between; gap: 0.75rem; flex-wrap: wrap; padding: 0.75rem 1rem; border-top: 1px solid var(--line); }
    .pages { display: flex; gap: 0.25rem; flex-wrap: wrap; }
    .btn-sm { min-height: 2rem; padding: 0 0.625rem; font-size: var(--step--1); }
    .pages .btn.current { background: var(--ink); color: #fff; border-color: var(--ink); }

    @media (max-width: 960px) {
      .layout { grid-template-columns: minmax(0, 1fr); }
      .updates { min-height: 0; }
      .updates-inner { position: static; }
      .updates-list { max-height: 24rem; }
    }
  `,
})
export class MyClaims {
  private api = inject(Api);
  private router = inject(Router);

  readonly claims = live(() => this.api.myClaims(), 15_000);
  readonly notifications = live(() => this.api.notifications(), 15_000);
  readonly actionNeeded = computed(() => (this.claims.value() ?? []).filter(c => c.actionRequired));

  // ---- pagination (10 per page, shown only when there are more than 10)
  static readonly PageSize = 10;
  readonly page = signal(1);
  readonly total = computed(() => this.claims.value()?.length ?? 0);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / MyClaims.PageSize)));
  readonly pages = computed(() => Array.from({ length: this.pageCount() }, (_, i) => i + 1));
  readonly firstShown = computed(() => (this.page() - 1) * MyClaims.PageSize + 1);
  readonly lastShown = computed(() => Math.min(this.page() * MyClaims.PageSize, this.total()));
  readonly pageClaims = computed(() =>
    (this.claims.value() ?? []).slice(this.firstShown() - 1, this.lastShown()));

  // ---- updates synced to the visible page
  readonly hovered = signal<string | null>(null);
  readonly pageUpdates = computed(() => {
    const ids = new Set(this.pageClaims().map(c => c.id));
    return (this.notifications.value() ?? []).filter(n => ids.has(n.claimId));
  });

  constructor() {
    // If the list shrinks under the current page (polling), stay on a real page.
    effect(() => { if (this.page() > this.pageCount()) this.page.set(this.pageCount()); });
  }

  go(p: number) { this.page.set(Math.min(Math.max(1, p), this.pageCount())); }

  open(id: string) { this.router.navigate(['/my-claims', id]); }
}
