import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, toProblem } from '../core/api';
import { ClaimType, Problem } from '../core/models';

/**
 * C-1 / C-2: report an incident. Client-side validation mirrors the server's rules for fast feedback,
 * but the server is authoritative, and its field errors are shown in the same places.
 */
@Component({
  selector: 'app-new-claim',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="page narrow">
      <div class="page-head">
        <div>
          <h1>Report a claim</h1>
          <p>Tell us what happened. You'll get a claim reference as soon as you submit.</p>
        </div>
      </div>

      <form class="surface" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="section">
          <h2 class="h3">What kind of claim?</h2>
          <div class="form-grid">
            <div class="field">
              <span class="label" id="type-l">Claim type</span>
              <div class="segmented" role="group" aria-labelledby="type-l">
                <button type="button" [attr.aria-pressed]="type() === 'Motor'" (click)="setType('Motor')">Motor</button>
                <button type="button" [attr.aria-pressed]="type() === 'Property'" (click)="setType('Property')">Property</button>
              </div>
            </div>
            <div class="field">
              <label for="market">Where is the policy held?</label>
              <select id="market" class="input" formControlName="marketCode" [attr.aria-invalid]="invalid('marketCode')">
                <option value="" disabled>Choose a market</option>
                @for (m of api.markets(); track m.code) { <option [value]="m.code">{{ m.name }}</option> }
              </select>
              @if (err('marketCode'); as e) { <span class="error">{{ e }}</span> }
            </div>
            <div class="field full">
              <label for="policy">Policy number</label>
              <input id="policy" class="input" formControlName="policyNumber" autocomplete="off" placeholder="e.g. POL-SG-482910"
                     [attr.aria-invalid]="invalid('policyNumber')" />
              @if (err('policyNumber'); as e) { <span class="error">{{ e }}</span> }
              @else { <span class="hint">You'll find it on your policy schedule.</span> }
            </div>
          </div>
        </div>

        <div class="section">
          <h2 class="h3">What happened?</h2>
          <div class="form-grid">
            <div class="field">
              <label for="date">Date of incident</label>
              <input id="date" type="date" class="input" formControlName="incidentDate" [max]="today()" [attr.aria-invalid]="invalid('incidentDate')" />
              @if (err('incidentDate'); as e) { <span class="error">{{ e }}</span> }
            </div>
            <div class="field">
              <label for="location">Where did it happen?</label>
              <input id="location" class="input" maxlength="200" formControlName="incidentLocation" placeholder="Street, area or landmark"
                     [attr.aria-invalid]="invalid('incidentLocation')" />
              @if (err('incidentLocation'); as e) { <span class="error">{{ e }}</span> }
            </div>
            @if (type() === 'Motor') {
              <div class="field">
                <label for="reg">Vehicle registration</label>
                <input id="reg" class="input" maxlength="20" formControlName="vehicleRegistration" autocomplete="off" [attr.aria-invalid]="invalid('vehicleRegistration')" />
                @if (err('vehicleRegistration'); as e) { <span class="error">{{ e }}</span> }
              </div>
            } @else {
              <div class="field full">
                <label for="addr">Property address</label>
                <input id="addr" class="input" maxlength="300" formControlName="propertyAddress" autocomplete="street-address" [attr.aria-invalid]="invalid('propertyAddress')" />
                @if (err('propertyAddress'); as e) { <span class="error">{{ e }}</span> }
              </div>
            }
            <div class="field full">
              <label for="desc">Describe what happened</label>
              <textarea id="desc" class="input" maxlength="4000" formControlName="description" rows="5"
                        placeholder="What happened, what was damaged, and anyone else involved." [attr.aria-invalid]="invalid('description')"></textarea>
              @if (err('description'); as e) { <span class="error">{{ e }}</span> }
              @else { <span class="hint">At least 20 characters. The more detail, the faster we can help.</span> }
            </div>
          </div>
        </div>

        <div class="section">
          <h2 class="h3">How much are you claiming?</h2>
          <div class="form-grid">
            <div class="field">
              <label for="amount">Estimated amount</label>
              <div class="input-group">
                <span class="addon">{{ currency() ?? '—' }}</span>
                <input id="amount" type="number" class="input num" formControlName="claimedAmount" inputmode="decimal"
                       [step]="step()" min="0" [attr.aria-invalid]="invalid('claimedAmount')" />
              </div>
              @if (err('claimedAmount'); as e) { <span class="error">{{ e }}</span> }
              @else if (currency()) {
                <span class="hint">{{ minorUnits() === 0 ? 'Whole amounts only for ' + currency() + '.' : 'Up to ' + minorUnits() + ' decimal places.' }} A rough figure is fine; your handler will confirm it.</span>
              }
            </div>
          </div>
        </div>

        <div class="section actions">
          @if (problem(); as p) { @if (!p.errors) { <div class="notice notice-danger">{{ p.detail ?? p.title }}</div> } }
          <div class="buttons">
            <a class="btn" routerLink="/my-claims">Cancel</a>
            <button type="submit" class="btn btn-primary" [disabled]="busy()" [attr.aria-busy]="busy()">
              {{ busy() ? 'Submitting…' : 'Submit claim' }}
            </button>
          </div>
        </div>
      </form>
    </div>
  `,
  styles: `
    .narrow { max-width: 820px; }
    .h3 { font-size: var(--step-1); }
    .label { font-weight: 500; font-size: var(--step--1); color: var(--ink-2); }
    .actions { display: grid; gap: 1rem; }
    .buttons { display: flex; justify-content: flex-end; gap: 0.75rem; }
  `,
})
export class NewClaim {
  readonly api = inject(Api);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly problem = signal<Problem | null>(null);
  readonly submitted = signal(false);

  readonly form = this.fb.nonNullable.group({
    type: ['Motor' as ClaimType],
    marketCode: ['', Validators.required],
    policyNumber: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9-]{6,40}$/)]],
    incidentDate: ['', Validators.required],
    incidentLocation: ['', Validators.required],
    description: ['', [Validators.required, Validators.minLength(20)]],
    claimedAmount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    vehicleRegistration: [''],
    propertyAddress: [''],
  });

  readonly type = toSignal(this.form.controls.type.valueChanges, { initialValue: 'Motor' as ClaimType });
  private readonly marketCode = toSignal(this.form.controls.marketCode.valueChanges, { initialValue: '' });
  readonly market = computed(() => this.api.markets().find(m => m.code === this.marketCode()));
  readonly currency = computed(() => this.market()?.currencyCode ?? null);
  readonly minorUnits = computed(() => this.market()?.minorUnits ?? 2);
  readonly step = computed(() => (this.minorUnits() === 0 ? '1' : '0.01'));
  /** Today in the market's own time zone (the server checks the same thing), as yyyy-mm-dd. */
  readonly today = computed(() =>
    new Intl.DateTimeFormat('en-CA', { timeZone: this.market()?.timeZoneId }).format(new Date()));

  setType(t: ClaimType) { this.form.controls.type.setValue(t); }

  invalid(name: string): boolean | null {
    return this.err(name) ? true : null;
  }

  /** The server's field error if there is one, otherwise the client-side rule after submit was tried. */
  err(name: string): string | null {
    const server = this.problem()?.errors?.[name]?.[0];
    if (server) return server;
    const c = this.form.get(name);
    if (!c || !this.submitted() || c.valid) {
      if (name === 'vehicleRegistration' && this.submitted() && this.type() === 'Motor' && !this.form.value.vehicleRegistration?.trim())
        return 'Vehicle registration is required for motor claims.';
      if (name === 'propertyAddress' && this.submitted() && this.type() === 'Property' && !this.form.value.propertyAddress?.trim())
        return 'Property address is required for property claims.';
      if (name === 'incidentDate' && this.submitted() && c?.value && c.value > this.today()) return 'The incident date cannot be in the future.';
      return null;
    }
    if (c.errors?.['required']) return 'This field is required.';
    if (c.errors?.['pattern']) return 'Use 6–40 letters, digits or dashes.';
    if (c.errors?.['minlength']) return 'Please describe what happened in at least 20 characters.';
    if (c.errors?.['min']) return 'Enter an amount greater than zero.';
    return 'Check this field.';
  }

  submit() {
    this.submitted.set(true);
    this.problem.set(null);
    const fields = ['marketCode', 'policyNumber', 'incidentDate', 'incidentLocation', 'description', 'claimedAmount', 'vehicleRegistration', 'propertyAddress'];
    if (this.form.invalid || fields.some(f => this.err(f))) {
      document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
      return;
    }

    const v = this.form.getRawValue();
    this.busy.set(true);
    this.api.submitClaim({
      marketCode: v.marketCode, type: v.type, policyNumber: v.policyNumber.trim(), incidentDate: v.incidentDate,
      incidentLocation: v.incidentLocation.trim(), description: v.description.trim(), claimedAmount: Number(v.claimedAmount),
      vehicleRegistration: v.type === 'Motor' ? v.vehicleRegistration.trim() : null,
      propertyAddress: v.type === 'Property' ? v.propertyAddress.trim() : null,
    }).subscribe({
      next: claim => this.router.navigate(['/my-claims', claim.id], { state: { justSubmitted: true } }),
      error: e => { this.problem.set(toProblem(e)); this.busy.set(false); },
    });
  }
}
