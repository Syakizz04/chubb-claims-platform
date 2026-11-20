import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Session } from '../core/session';
import { Logo } from '../shared/logo';

/**
 * App shell: a near-black header with the Lantern mark and name (our product name, not Chubb's wordmark), the
 * navigation for the current role, and the dev user switcher.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Logo],
  template: `
    <header class="bar">
      <div class="inner">
        <!-- The mark goes "home": your role's start page, or the sign-in page when signed out. -->
        <a class="product" [routerLink]="session.home()" [attr.aria-label]="'Lantern home' + (session.user() ? '' : ', sign in')">
          <app-logo [size]="26" />
          <span class="name">Lantern</span>
          <span class="region">Claims · Asia Pacific</span>
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
        @if (session.user()) {
          <button type="button" class="signout" (click)="signOut()">Sign out</button>
        }
      </div>
    </header>

    <main><router-outlet /></main>
  `,
  styles: `
    .bar { background: var(--ink); color: #fff; }
    .inner { max-width: 1200px; margin: 0 auto; padding: 0 1.5rem; height: 3.5rem; display: flex; align-items: center; gap: 2rem; }
    .product { display: flex; align-items: center; gap: 0.5rem; color: #fff; text-decoration: none; white-space: nowrap; }
    .name { font-weight: 600; font-size: var(--step-1); letter-spacing: -0.01em; }
    .region { font-size: var(--step--1); color: #aab1bc; margin-left: 0.25rem; padding-left: 0.625rem; border-left: 1px solid #3a404b; }
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
    .signout {
      background: none; border: 1px solid transparent; color: #c9ced6; font: inherit; font-size: var(--step--1);
      min-height: 2.25rem; padding: 0 0.625rem; border-radius: var(--radius); cursor: pointer; white-space: nowrap;
      transition: color 150ms var(--ease), border-color 150ms var(--ease);
    }
    .signout:hover { color: #fff; border-color: #3a404b; }
    .signout:focus-visible { outline: 2px solid #3fc4ba; outline-offset: 1px; }
    @media (max-width: 720px) {
      /* Two rows on phones: product + user switcher, then the role's navigation. */
      .inner { flex-wrap: wrap; height: auto; gap: 0 1rem; padding: 0.5rem 1rem 0; }
      .product { order: 1; flex: 1; }
      .switcher { order: 2; }
      .signout { order: 4; padding: 0 0.375rem; align-self: center; }
      /* Row 2: the role's navigation, with Sign out at its right end. */
      nav { order: 3; flex: 1 1 calc(100% - 6.5rem); min-width: 0; margin-left: -0.75rem; }
      nav a { height: 2.75rem; }
      .region { display: none; }
      select { max-width: 11rem; }
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

  signOut() {
    this.session.signOut();
    this.router.navigateByUrl('/');
  }

  switchUser(id: string) {
    this.session.switchTo(id);
    this.router.navigateByUrl(this.session.home());
  }
}
