import { Component, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../core/api';
import { Session } from '../core/session';
import { Logo } from '../shared/logo';
import { MarketTag } from '../shared/status';

/**
 * Front door. It stands in for a sign-in screen, so it looks like one: the product, one line on
 * what it does, and "Continue as" one of three people (one per role). The full user list is in
 * the header switcher.
 */
@Component({
  selector: 'app-welcome',
  imports: [Logo, MarketTag],
  template: `
    <div class="stage">
      <section class="card surface" aria-labelledby="welcome-h">
        <header class="brand">
          <app-logo [size]="40" />
          <div>
            <h1 id="welcome-h">Lantern</h1>
            <p class="tagline">Every claim visible, from report to payout.</p>
          </div>
        </header>

        <p class="lede">Motor and property claims for six Asia Pacific markets. Claimants track their claim, handlers work it through to settlement, and managers see outstanding exposure in each market's currency.</p>

        <h2 class="label">Continue as</h2>
        <ul class="people">
          @for (p of people(); track p.id) {
            <li>
              <button type="button" class="person" (click)="go(p.id)">
                <span class="avatar" [attr.data-role]="p.role" aria-hidden="true">{{ p.initials }}</span>
                <span class="who">
                  <span class="name">{{ p.name }}</span>
                  <span class="role">{{ p.title }} · {{ p.text }}</span>
                </span>
                <svg class="chev" width="16" height="16" viewBox="0 0 16 16" aria-hidden="true"><path d="M6 3.5 10.5 8 6 12.5" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"/></svg>
              </button>
            </li>
          }
        </ul>

        <footer class="markets" aria-label="Markets">
          @for (m of markets(); track m.code) { <app-market [code]="m.code" [title]="m.name" /> }
        </footer>
      </section>
    </div>
  `,
  styles: `
    .stage {
      min-height: calc(100dvh - 3.5rem); display: grid; place-items: center; padding: 2.5rem 1rem;
      background: radial-gradient(60rem 30rem at 50% -10%, var(--accent-tint), transparent 70%);
    }
    .card { width: min(100%, 30rem); padding: 2rem; box-shadow: var(--shadow); }
    .brand { display: flex; align-items: center; gap: 0.875rem; color: var(--ink); }
    h1 { font-size: var(--step-3); letter-spacing: -0.02em; line-height: 1.1; }
    .tagline { color: var(--accent); font-weight: 500; margin-top: 0.125rem; }
    .lede { margin: 1.25rem 0 1.75rem; color: var(--ink-2); line-height: 1.55; }
    .label { font-size: var(--step--1); font-weight: 600; color: var(--ink-3); text-transform: uppercase; letter-spacing: 0.06em; margin-bottom: 0.5rem; }
    .people { list-style: none; margin: 0; padding: 0; border: 1px solid var(--line); border-radius: var(--radius); }
    .people li + li { border-top: 1px solid var(--line); }
    .person {
      display: flex; align-items: center; gap: 0.875rem; width: 100%; padding: 0.875rem 1rem;
      background: none; border: 0; font: inherit; color: inherit; text-align: left; cursor: pointer;
      transition: background-color 150ms var(--ease);
    }
    .person:hover { background: var(--panel); }
    .person:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
    .avatar {
      width: 2.5rem; height: 2.5rem; border-radius: 50%; display: grid; place-items: center; flex-shrink: 0;
      font-weight: 600; font-size: var(--step--1); color: var(--ink); background: var(--panel);
    }
    .avatar[data-role='Handler'] { background: var(--accent-tint); color: var(--accent); }
    .avatar[data-role='Manager'] { background: var(--ink); color: #fff; }
    .who { display: grid; gap: 0.125rem; flex: 1; min-width: 0; }
    .name { font-weight: 600; }
    .role { font-size: var(--step--1); color: var(--ink-2); }
    .chev { color: var(--ink-3); flex-shrink: 0; transition: transform 150ms var(--ease), color 150ms var(--ease); }
    .person:hover .chev { color: var(--ink); transform: translateX(2px); }
    .markets { display: flex; flex-wrap: wrap; gap: 0.75rem 1rem; margin-top: 1.5rem; padding-top: 1.25rem; border-top: 1px solid var(--line); }
    @media (max-width: 480px) { .card { padding: 1.5rem 1.25rem; } }
  `,
})
export class Welcome {
  readonly api = inject(Api);
  private session = inject(Session);
  private router = inject(Router);

  /** Same fixed KR→SG order as the market colours (see styles.scss). */
  readonly markets = computed(() => {
    const order = ['KR', 'HK', 'TW', 'TH', 'MY', 'SG'];
    return [...this.api.markets()].sort((a, b) => order.indexOf(a.code) - order.indexOf(b.code));
  });

  private readonly picks = [
    { id: 'claimant-sg', title: 'Claimant', text: 'report, track and reply' },
    { id: 'handler-1', title: 'Claims handler', text: 'assess and settle' },
    { id: 'manager-1', title: 'Claims manager', text: 'exposure and workload' },
  ];

  readonly people = computed(() => this.picks.map(p => {
    const u = this.session.users().find(x => x.id === p.id);
    const name = u?.displayName ?? p.id;
    return { ...p, name, role: u?.role ?? '', initials: name.split(/\s+/).map(w => w[0]).slice(0, 2).join('').toUpperCase() };
  }));

  go(id: string) {
    this.session.switchTo(id);
    this.router.navigateByUrl(this.session.home());
  }
}
