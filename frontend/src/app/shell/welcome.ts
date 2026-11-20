import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Session } from '../core/session';

/** First-run screen: explains the dev identity and lets you pick a role to explore. */
@Component({
  selector: 'app-welcome',
  template: `
    <div class="page">
      <div class="intro">
        <h1>Claims for six Asia Pacific markets</h1>
        <p>Report and track motor and property claims, work the claims queue, and see outstanding exposure across South Korea, Hong Kong, Taiwan, Thailand, Malaysia and Singapore.</p>
      </div>

      <div class="roles">
        @for (r of roles; track r.id) {
          <button type="button" class="role surface" (click)="go(r.id)">
            <span class="who">{{ r.title }}</span>
            <span class="what">{{ r.text }}</span>
            <span class="as small muted">Continue as {{ name(r.id) }}</span>
          </button>
        }
      </div>

      <p class="small muted note">
        Development build: there is no sign-in. Pick a seeded user here or with the switcher in the header.
        Each request carries that user's id as <code>X-User-Id</code>.
      </p>
    </div>
  `,
  styles: `
    .intro { max-width: 60ch; margin: 1.5rem 0 2rem; }
    .intro p { margin-top: 0.75rem; color: var(--ink-2); font-size: var(--step-1); line-height: 1.5; }
    .roles { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; }
    @media (max-width: 800px) { .roles { grid-template-columns: 1fr; } }
    .role {
      display: grid; gap: 0.5rem; text-align: left; padding: 1.25rem 1.5rem; font: inherit; color: inherit; cursor: pointer;
      transition: border-color 150ms var(--ease), box-shadow 150ms var(--ease);
    }
    .role:hover { border-color: var(--ink-3); box-shadow: var(--shadow); }
    .who { font-weight: 600; font-size: var(--step-1); }
    .what { color: var(--ink-2); }
    .as { margin-top: 0.5rem; }
    .note { margin-top: 2rem; max-width: 70ch; }
    code { font-size: 0.85em; background: var(--panel); padding: 0.0625rem 0.25rem; border-radius: var(--radius); }
  `,
})
export class Welcome {
  private session = inject(Session);
  private router = inject(Router);

  readonly roles = [
    { id: 'claimant-sg', title: 'Claimant', text: 'Report an incident, follow your claim and answer questions from the claims team.' },
    { id: 'handler-1', title: 'Claims handler', text: 'Pick up incoming claims, assess them, and take them through to settlement.' },
    { id: 'manager-1', title: 'Claims manager', text: "See outstanding exposure by market, the team's workload and claim ageing." },
  ];

  name(id: string) { return this.session.users().find(u => u.id === id)?.displayName ?? id; }

  go(id: string) {
    this.session.switchTo(id);
    this.router.navigateByUrl(this.session.home());
  }
}
