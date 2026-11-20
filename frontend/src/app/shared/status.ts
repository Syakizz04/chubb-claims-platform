import { Component, computed, input } from '@angular/core';
import { StatusLabelPipe } from '../core/format';
import { ClaimStatus } from '../core/models';

@Component({
  selector: 'app-status',
  imports: [StatusLabelPipe],
  template: `<span class="pill s-{{ status() }}">{{ status() | statusLabel }}</span>`,
})
export class StatusPill {
  readonly status = input.required<ClaimStatus>();
}

@Component({
  selector: 'app-market',
  template: `<span class="market" [style.--mk]="'var(--m-' + code() + ')'">{{ code() }}</span>`,
})
export class MarketTag {
  readonly code = input.required<string>();
}

/**
 * The status track: the claim's journey as a horizontal line of steps. "Decision" resolves to
 * Approved or Not approved; a rejected claim ends there.
 */
@Component({
  selector: 'app-status-track',
  template: `
    <ol class="track" [attr.aria-label]="'Claim progress: ' + currentLabel()">
      @for (step of steps(); track step.key) {
        <li [class]="step.state" [attr.aria-current]="step.state === 'current' ? 'step' : null">
          <span class="dot" aria-hidden="true"></span>
          <span class="label">{{ step.label }}</span>
        </li>
      }
    </ol>
  `,
  styles: `
    .track { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(5, 1fr); }
    li { position: relative; display: grid; justify-items: start; gap: 0.5rem; padding-right: 0.5rem; }
    li::before {
      content: ''; position: absolute; top: 6px; left: 14px; right: 0; height: 2px; background: var(--line);
    }
    li:last-child::before { display: none; }
    li.done::before { background: var(--ink); }
    .dot {
      position: relative; z-index: 1; width: 14px; height: 14px; border-radius: 50%;
      border: 2px solid var(--line-strong); background: var(--surface);
      transition: background-color 200ms var(--ease), border-color 200ms var(--ease);
    }
    .label { font-size: var(--step--1); color: var(--ink-3); }
    .done .dot { background: var(--ink); border-color: var(--ink); }
    .done .label { color: var(--ink-2); }
    .current .dot { background: var(--accent); border-color: var(--accent); box-shadow: 0 0 0 4px var(--accent-tint); }
    .current .label { color: var(--ink); font-weight: 600; }
    .current.warn .dot { background: var(--warning); border-color: var(--warning); box-shadow: 0 0 0 4px var(--warning-tint); }
    .current.bad .dot { background: var(--danger); border-color: var(--danger); box-shadow: 0 0 0 4px var(--danger-tint); }
    .skipped .label { text-decoration: line-through; }
    @media (max-width: 560px) { .label { font-size: 0.6875rem; } }
  `,
})
export class StatusTrack {
  readonly status = input.required<ClaimStatus>();

  private readonly order: ClaimStatus[] = ['Submitted', 'UnderReview', 'AwaitingInfo', 'Approved', 'Settled'];

  readonly steps = computed(() => {
    const s = this.status();
    const labels = ['Submitted', 'Under review', 'Information', s === 'Rejected' ? 'Not approved' : 'Approved', 'Settled'];
    // AwaitingInfo is a detour: when we're past it, it's "done" whether or not it happened.
    const index = s === 'Rejected' ? 3 : this.order.indexOf(s);
    return labels.map((label, i) => {
      let state = i < index ? 'done' : i === index ? 'current' : 'todo';
      if (i === index && s === 'AwaitingInfo') state += ' warn';
      if (i === index && s === 'Rejected') state += ' bad';
      if (s === 'Rejected' && i === 4) state = 'todo skipped';
      if (s === 'Settled' && i === 4) state = 'done';
      return { key: i, label, state };
    });
  });

  readonly currentLabel = computed(() => this.steps().find(s => s.state.startsWith('current'))?.label ?? 'Settled');
}
