import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MoneyPipe } from '../core/format';
import { StaffClaimSummary } from '../core/models';
import { MarketTag, StatusPill } from '../shared/status';

/** Shared staff table: one component vocabulary for the queue and the workload. */
@Component({
  selector: 'app-claim-table',
  imports: [RouterLink, MoneyPipe, StatusPill, MarketTag],
  template: `
    <div class="table-wrap">
      <table class="data">
        <thead>
          <tr>
            <th>Reference</th><th>Market</th><th>Type</th><th>Claimant</th><th>Status</th>
            <th class="right">Claimed</th><th class="right">Reserve</th><th class="right">Age</th>
            @if (showHandler()) { <th>Handler</th> }
            @if (actionLabel()) { <th><span class="visually-hidden">Action</span></th> }
          </tr>
        </thead>
        <tbody>
          @for (c of claims(); track c.id) {
            <tr>
              <td class="ref"><a [routerLink]="['/claims', c.id]">{{ c.reference }}</a></td>
              <td><app-market [code]="c.marketCode" /></td>
              <td>{{ c.type }}</td>
              <td>{{ c.claimantName }}</td>
              <td><app-status [status]="c.status" /></td>
              <td class="right num">{{ c.claimedAmount | money: c.currencyCode }}</td>
              <td class="right num">{{ c.reserveAmount | money: c.currencyCode }}</td>
              <td class="right num" [class.old]="c.ageDays > 30">{{ c.ageDays }}d</td>
              @if (showHandler()) { <td>{{ c.assignedHandlerName ?? '—' }}</td> }
              @if (actionLabel()) {
                <td class="right">
                  <button type="button" class="btn btn-sm" [disabled]="busyId() === c.id" [attr.aria-busy]="busyId() === c.id"
                          (click)="act.emit(c.id)">{{ busyId() === c.id ? 'Picking up…' : actionLabel() }}</button>
                </td>
              }
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    .old { color: var(--danger); font-weight: 600; }
  `,
})
export class ClaimTable {
  readonly claims = input.required<StaffClaimSummary[]>();
  readonly showHandler = input(false);
  readonly actionLabel = input<string | null>(null);
  readonly busyId = input<string | null>(null);
  readonly act = output<string>();
}
