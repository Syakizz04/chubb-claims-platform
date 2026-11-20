import { DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, Subject, catchError, merge, switchMap, timer } from 'rxjs';
import { toProblem } from '../core/api';
import { Problem } from '../core/models';

/**
 * Loads data into signals and, if intervalMs > 0, keeps re-polling it. This is how the UI handles
 * the async nature of claims: a claimant sees a handler's decision, or a manager sees new exposure,
 * without refreshing (C-7, M-6). Polling is a deliberate, simple choice here; SSE or WebSockets
 * fed from the Kafka stream would replace it at scale.
 *
 * Must be called in an injection context (a component field initialiser or constructor).
 */
export function live<T>(load: () => Observable<T>, intervalMs = 0) {
  const value = signal<T | undefined>(undefined);
  const error = signal<Problem | null>(null);
  const loading = signal(true);
  const refresh$ = new Subject<void>();

  const ticks = intervalMs > 0 ? timer(0, intervalMs) : timer(0);
  merge(ticks, refresh$)
    .pipe(
      switchMap(() => load().pipe(catchError(e => { error.set(toProblem(e)); loading.set(false); return EMPTY; }))),
      takeUntilDestroyed(inject(DestroyRef)),
    )
    .subscribe(v => { value.set(v); error.set(null); loading.set(false); });

  return { value, error, loading, refresh: () => refresh$.next(), set: (v: T) => value.set(v) };
}
