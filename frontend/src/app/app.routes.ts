import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { Role } from './core/models';
import { Session } from './core/session';
import { Welcome } from './shell/welcome';

/**
 * Role guard for navigation only. The real authorisation is on the server (route policies); this
 * just keeps people out of screens their role can't use.
 */
const requireRole = (...roles: Role[]): CanActivateFn => () => {
  const session = inject(Session);
  const role = session.role();
  return role && roles.includes(role) ? true : inject(Router).parseUrl(role ? session.home() : '/');
};

export const routes: Routes = [
  { path: '', component: Welcome, title: 'Lantern · Claims Asia Pacific' },

  // Claimant
  { path: 'my-claims', canActivate: [requireRole('Claimant')], title: 'My claims',
    loadComponent: () => import('./claimant/my-claims').then(m => m.MyClaims) },
  { path: 'new-claim', canActivate: [requireRole('Claimant')], title: 'Report a claim',
    loadComponent: () => import('./claimant/new-claim').then(m => m.NewClaim) },
  { path: 'my-claims/:id', canActivate: [requireRole('Claimant')], title: 'Claim',
    loadComponent: () => import('./claimant/claimant-claim').then(m => m.ClaimantClaim) },

  // Staff
  { path: 'work', canActivate: [requireRole('Handler')], title: 'My work',
    loadComponent: () => import('./staff/work').then(m => m.Work) },
  { path: 'queue', canActivate: [requireRole('Handler', 'Manager')], title: 'Incoming claims',
    loadComponent: () => import('./staff/queue').then(m => m.Queue) },
  { path: 'claims/:id', canActivate: [requireRole('Handler', 'Manager')], title: 'Claim',
    loadComponent: () => import('./staff/staff-claim').then(m => m.StaffClaim) },

  // Manager
  { path: 'dashboard', canActivate: [requireRole('Manager')], title: 'Dashboard',
    loadComponent: () => import('./manager/dashboard').then(m => m.Dashboard) },

  { path: '**', redirectTo: '' },
];
