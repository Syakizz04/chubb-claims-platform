import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Session } from '../core/session';

/**
 * App shell: a near-black header with the product name (plain text, not Chubb's wordmark), the
 * navigation for the current role, and the dev user switcher.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="bar">
      <div class="inner">
        <a class="product" [routerLink]="session.home()">
          <span class="name">Claims</span>
          <span class="region">Asia Pacific</span>
        </a>

        <nav aria-label="Main">
          @for (item of nav(); track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active">{{ item.label }}</a>
          }
        </nav>

        <label class="switcher">
          <span class="visually-hidden">Signed in as (dev user switcher)</span>
          <span class="dev" title="Development identity: not real sign-in">DEV</span>
          <select [value]="session.userId() ?? ''" (change)="switchUser($any($event.target).value)">
            @if (!session.userId()) { <option value="" disabled>Choose a user…</option> }
            @for (group of groups(); track group.role) {
              <optgroup [label]="group.label">
                @for (u of group.users; track u.id) {
                  <option [value]="u.id" [selected]="u.id === session.userId()">{{ u.displayName }}</option>
                }
              </optgroup>
            }
          </select>
        </label>
      </div>
    </header>

    <main><router-outlet /></main>
  `,
  styles: `
    .bar { background: var(--ink); color: #fff; }
    .inner { max-width: 1200px; margin: 0 auto; padding: 0 1.5rem; height: 3.5rem; display: flex; align-items: center; gap: 2rem; }
    .product { display: flex; align-items: baseline; gap: 0.625rem; color: #fff; text-decoration: none; white-space: nowrap; }
    .name { font-weight: 600; font-size: var(--step-1); letter-spacing: -0.01em; }
    .region { font-size: var(--step--1); color: #aab1bc; }
    nav { display: flex; gap: 0.25rem; flex: 1; overflow-x: auto; }
    nav a {
      color: #c9ced6; text-decoration: none; padding: 0 0.75rem; height: 3.5rem; display: inline-flex; align-items: center;
      border-bottom: 2px solid transparent; font-weight: 500; white-space: nowrap;
      transition: color 150ms var(--ease), border-color 150ms var(--ease);
    }
    nav a:hover { color: #fff; }
    nav a.active { color: #fff; border-bottom-color: #3fc4ba; }
    nav a:focus-visible, .product:focus-visible { outline-color: #3fc4ba; }
    .switcher { display: flex; align-items: center; gap: 0.5rem; }
    .dev { font-size: 0.6875rem; font-weight: 600; letter-spacing: 0.04em; color: var(--ink); background: #f2c94c; padding: 0.125rem 0.375rem; border-radius: var(--radius); }
    select {
      background: #22262e; color: #fff; border: 1px solid #3a404b; border-radius: var(--radius);
      min-height: 2.25rem; padding: 0 0.5rem; font: inherit; font-size: var(--step--1); max-width: 14rem;
    }
    select:focus-visible { outline: 2px solid #3fc4ba; outline-offset: 1px; }
    @media (max-width: 720px) {
      .inner { gap: 1rem; padding: 0 1rem; }
      .region { display: none; }
    }
  `,
})
export class Shell {
  readonly session = inject(Session);
  private router = inject(Router);

  readonly nav = computed(() => {
    switch (this.session.role()) {
      case 'Claimant': return [{ path: '/my-claims', label: 'My claims' }, { path: '/new-claim', label: 'Report a claim' }];
      case 'Handler': return [{ path: '/work', label: 'My work' }, { path: '/queue', label: 'Incoming' }];
      case 'Manager': return [{ path: '/dashboard', label: 'Dashboard' }, { path: '/queue', label: 'Incoming' }];
      default: return [];
    }
  });

  readonly groups = computed(() => {
    const labels = { Claimant: 'Claimants', Handler: 'Claims handlers', Manager: 'Managers' } as const;
    return (['Claimant', 'Handler', 'Manager'] as const).map(role => ({
      role, label: labels[role], users: this.session.users().filter(u => u.role === role),
    }));
  });

  switchUser(id: string) {
    this.session.switchTo(id);
    this.router.navigateByUrl(this.session.home());
  }
}
