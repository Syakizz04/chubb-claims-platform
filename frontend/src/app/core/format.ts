import { Pipe, PipeTransform, inject } from '@angular/core';
import { Api } from './api';
import { ClaimEventType, ClaimStatus } from './models';

/**
 * Money is always shown with its ISO code and the currency's own decimal places
 * (KRW 3,500,000 · SGD 4,200.00). Symbols are avoided: "$" would be ambiguous across HKD, TWD and SGD.
 */
export function formatMoney(amount: number | null | undefined, currency: string, minorUnits: number): string {
  if (amount === null || amount === undefined) return '—';
  const n = new Intl.NumberFormat('en', { minimumFractionDigits: minorUnits, maximumFractionDigits: minorUnits }).format(amount);
  return `${currency} ${n}`;
}

@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  private api = inject(Api);
  transform(amount: number | null | undefined, currency: string, minorUnits?: number): string {
    return formatMoney(amount, currency, minorUnits ?? this.api.minorUnits(currency));
  }
}

@Pipe({ name: 'usd' })
export class UsdPipe implements PipeTransform {
  transform(amount: number | null | undefined, compact = false): string {
    if (amount === null || amount === undefined) return '—';
    return new Intl.NumberFormat('en-US', {
      style: 'currency', currency: 'USD', currencyDisplay: 'code',
      notation: compact ? 'compact' : 'standard', maximumFractionDigits: compact ? 1 : 0,
    }).format(amount).replace(/ /, ' ');
  }
}

export const STATUS_LABEL: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  AwaitingInfo: 'Awaiting information',
  Approved: 'Approved',
  Rejected: 'Not approved',
  Settled: 'Settled',
};

@Pipe({ name: 'statusLabel' })
export class StatusLabelPipe implements PipeTransform {
  transform(s: ClaimStatus): string { return STATUS_LABEL[s] ?? s; }
}

export const EVENT_LABEL: Record<ClaimEventType, string> = {
  Submitted: 'Claim submitted',
  PickedUp: 'Assigned to a claims handler',
  InformationRequested: 'More information requested',
  InformationProvided: 'Information provided',
  ReserveAdjusted: 'Reserve adjusted',
  Approved: 'Claim approved',
  Rejected: 'Claim not approved',
  Settled: 'Claim settled',
  NoteAdded: 'Internal note',
};

/** "3 days ago" style relative time, for timelines and freshness labels. */
@Pipe({ name: 'ago' })
export class AgoPipe implements PipeTransform {
  transform(iso: string | null | undefined): string {
    if (!iso) return '—';
    const s = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
    if (s < 45) return 'just now';
    const m = Math.round(s / 60); if (m < 60) return `${m} min ago`;
    const h = Math.round(m / 60); if (h < 24) return `${h} h ago`;
    const d = Math.round(h / 24); return d === 1 ? 'yesterday' : `${d} days ago`;
  }
}
