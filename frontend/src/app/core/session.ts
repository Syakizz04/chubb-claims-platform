import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { User } from './models';

const STORAGE_KEY = 'claims.devUserId';

/**
 * Dev identity (N-3). Holds the user picked in the switcher and attaches their id to every API call
 * as X-User-Id. In production this would be an OIDC session, and the interceptor would attach a bearer
 * token; nothing else in the app would change.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  readonly users = signal<User[]>([]);
  readonly userId = signal<string | null>(readStored());
  readonly user = computed(() => this.users().find(u => u.id === this.userId()) ?? null);
  readonly role = computed(() => this.user()?.role ?? null);

  switchTo(id: string) {
    this.userId.set(id);
    try { localStorage.setItem(STORAGE_KEY, id); } catch { /* storage unavailable: session-only */ }
  }

  /** Where each role lands. */
  home(): string {
    switch (this.role()) {
      case 'Claimant': return '/my-claims';
      case 'Handler': return '/work';
      case 'Manager': return '/dashboard';
      default: return '/';
    }
  }
}

function readStored(): string | null {
  try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
}

export const devIdentityInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(Session);
  const id = session.userId();
  if (!id) return next(req);
  const headers: Record<string, string> = { 'X-User-Id': id };
  // Insights has no user store, so it takes the role as a claim, as it would from a JWT.
  // Claims looks the role up itself and ignores this header.
  const role = session.role();
  if (role && req.url.startsWith('/api/insights')) headers['X-User-Role'] = role;
  return next(req.clone({ setHeaders: headers }));
};
