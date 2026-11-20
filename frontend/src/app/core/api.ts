import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import {
  ClaimType, ClaimantClaimDetail, ClaimantClaimSummary, ClaimantNotification, DashboardReport, Market,
  Problem, StaffClaimDetail, StaffClaimSummary, SubmitClaimRequest, User,
} from './models';

/**
 * REST client for both services. URLs are relative: the dev server (proxy.conf.json) and nginx
 * (Docker) route /api/insights to the Insights service and everything else under /api to Claims.
 */
@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);

  /** Markets rarely change: fetch once and share. Used to format money with the right decimals. */
  readonly markets$ = this.http.get<Market[]>('/api/markets').pipe(shareReplay(1));
  readonly markets = signal<Market[]>([]);

  constructor() {
    this.markets$.subscribe({ next: m => this.markets.set(m), error: () => {} });
  }

  users = () => this.http.get<User[]>('/api/dev/users');

  // ---------------------------------------------------------------- claimant
  myClaims = () => this.http.get<ClaimantClaimSummary[]>('/api/claimant/claims');
  myClaim = (id: string) => this.http.get<ClaimantClaimDetail>(`/api/claimant/claims/${id}`);
  submitClaim = (body: SubmitClaimRequest) => this.http.post<ClaimantClaimDetail>('/api/claimant/claims', body);
  respond = (id: string, response: string) =>
    this.http.post<ClaimantClaimDetail>(`/api/claimant/claims/${id}/information-response`, { response });

  // ---------------------------------------------------------------- staff
  queue = (market?: string | null, type?: ClaimType | null) => {
    let params = new HttpParams();
    if (market) params = params.set('market', market);
    if (type) params = params.set('type', type);
    return this.http.get<StaffClaimSummary[]>('/api/staff/queue', { params });
  };
  assignedToMe = () => this.http.get<StaffClaimSummary[]>('/api/staff/my-claims');
  allClaims = () => this.http.get<StaffClaimSummary[]>('/api/staff/claims');
  staffClaim = (id: string) => this.http.get<StaffClaimDetail>(`/api/staff/claims/${id}`);

  pickUp = (id: string) => this.action(id, 'pick-up');
  requestInfo = (id: string, question: string) => this.action(id, 'information-requests', { question });
  adjustReserve = (id: string, amount: number, reason: string) => this.action(id, 'reserve', { amount, reason });
  approve = (id: string, amount: number, note: string | null) => this.action(id, 'approve', { amount, note });
  reject = (id: string, reason: string) => this.action(id, 'reject', { reason });
  settle = (id: string) => this.action(id, 'settle');
  addNote = (id: string, text: string) => this.action(id, 'notes', { text });

  private action(id: string, path: string, body: object = {}): Observable<StaffClaimDetail> {
    return this.http.post<StaffClaimDetail>(`/api/staff/claims/${id}/${path}`, body);
  }

  // ---------------------------------------------------------------- insights
  dashboard = () => this.http.get<DashboardReport>('/api/insights/dashboard');
  notifications = () => this.http.get<ClaimantNotification[]>('/api/insights/notifications');

  minorUnits(currency: string): number {
    return this.markets().find(m => m.currencyCode === currency)?.minorUnits ?? 2;
  }
  marketName(code: string): string {
    return this.markets().find(m => m.code === code)?.name ?? code;
  }
}

/** Turns an HTTP error into ProblemDetails, so forms can show field errors and actions can show detail. */
export function toProblem(err: unknown): Problem {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0) return { status: 0, title: "Can't reach the server. Check that the backend is running." };
    const body = typeof err.error === 'object' && err.error ? (err.error as Problem) : {};
    return { status: err.status, title: body.title ?? err.statusText, ...body };
  }
  return { title: 'Something went wrong.' };
}

export function problemMessage(p: Problem): string {
  if (p.status === 409 && p.detail) return `${p.detail} The claim may have been updated; it has been refreshed.`;
  return p.detail ?? p.title ?? 'Something went wrong.';
}

