import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { EVENT_LABEL, formatMoney } from '../core/format';
import { TimelineEntry } from '../core/models';

/** The claim's audit trail, newest first. Internal events (staff only) are marked as such. */
@Component({
  selector: 'app-timeline',
  imports: [DatePipe],
  template: `
    <ol class="timeline">
      @for (e of entries(); track e.sequence) {
        <li [class.internal]="e.isInternal">
          <div class="head">
            <span class="what">{{ label(e) }}</span>
            @if (e.isInternal) { <span class="tag">Internal</span> }
          </div>
          <div class="meta small muted">
            {{ e.actorName }} · <time [attr.datetime]="e.occurredAt">{{ e.occurredAt | date: 'd MMM y, HH:mm' }}</time>
          </div>
          @if (amountText(e); as text) { <div class="amount num">{{ text }}</div> }
          @if (e.note) { <p class="note">{{ e.note }}</p> }
        </li>
      } @empty {
        <li class="muted">No activity yet.</li>
      }
    </ol>
  `,
  styles: `
    .timeline { list-style: none; margin: 0; padding: 0; }
    li { position: relative; padding: 0 0 1.25rem 1.5rem; }
    li::before { content: ''; position: absolute; left: 4px; top: 0.5rem; width: 8px; height: 8px; border-radius: 50%; background: var(--ink); }
    li::after { content: ''; position: absolute; left: 7.5px; top: 1.25rem; bottom: 0; width: 1px; background: var(--line); }
    li:last-child::after { display: none; }
    li.internal::before { background: var(--surface); border: 1.5px solid var(--ink-3); }
    .head { display: flex; align-items: center; gap: 0.5rem; }
    .what { font-weight: 500; }
    .tag { font-size: 0.6875rem; font-weight: 600; letter-spacing: 0.02em; color: var(--ink-3); border: 1px solid var(--line-strong); padding: 0 0.375rem; border-radius: var(--radius); }
    .meta { margin-top: 0.125rem; }
    .amount { margin-top: 0.25rem; font-weight: 500; }
    .note { margin-top: 0.375rem; color: var(--ink-2); max-width: 65ch; white-space: pre-line; }
  `,
})
export class Timeline {
  readonly items = input.required<TimelineEntry[]>();
  readonly currency = input.required<string>();
  readonly minorUnits = input.required<number>();

  readonly entries = computed(() => [...this.items()].sort((a, b) => b.sequence - a.sequence));

  label(e: TimelineEntry) { return EVENT_LABEL[e.type] ?? e.type; }

  amountText(e: TimelineEntry): string | null {
    const fmt = (n: number | null) => formatMoney(n, this.currency(), this.minorUnits());
    switch (e.type) {
      case 'ReserveAdjusted': return `${fmt(e.previousAmount)} → ${fmt(e.amount)}`;
      case 'Submitted': return `Claimed ${fmt(e.amount)}`;
      case 'Approved': return `Approved ${fmt(e.amount)}`;
      case 'Settled': return `Paid ${fmt(e.amount)}`;
      default: return null;
    }
  }
}
