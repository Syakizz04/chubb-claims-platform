import { Component, computed, effect, input, model } from '@angular/core';

export const PAGE_SIZE = 10;

/** Slice helper so pages using <app-pager> agree on what a page contains. */
export function pageOf<T>(items: readonly T[], page: number, size = PAGE_SIZE): T[] {
  return items.slice((page - 1) * size, page * size);
}

/**
 * "1–10 of 24  ‹ Prev 1 2 3 Next ›". Renders nothing when everything fits on one page.
 * Two-way binds the page and clamps it if the list shrinks under it (e.g. a poll removes items).
 */
@Component({
  selector: 'app-pager',
  template: `
    @if (pageCount() > 1) {
      <nav class="pager" [attr.aria-label]="label()">
        <span class="small muted">{{ first() }}–{{ last() }} of {{ total() }}</span>
        <div class="pages">
          <button type="button" class="btn btn-sm" [disabled]="page() === 1" (click)="go(page() - 1)" aria-label="Previous page">‹ Prev</button>
          @for (p of pages(); track p) {
            <button type="button" class="btn btn-sm" [class.current]="p === page()" [attr.aria-current]="p === page() ? 'page' : null" (click)="go(p)">{{ p }}</button>
          }
          <button type="button" class="btn btn-sm" [disabled]="page() === pageCount()" (click)="go(page() + 1)" aria-label="Next page">Next ›</button>
        </div>
      </nav>
    }
  `,
  styles: `
    .pager { display: flex; align-items: center; justify-content: space-between; gap: 0.75rem; flex-wrap: wrap; padding: 0.75rem 1rem; border-top: 1px solid var(--line); }
    .pages { display: flex; gap: 0.25rem; flex-wrap: wrap; }
    .btn.current { background: var(--ink); color: #fff; border-color: var(--ink); }
  `,
})
export class Pager {
  readonly total = input.required<number>();
  readonly size = input(PAGE_SIZE);
  readonly label = input('Pages');
  readonly page = model(1);

  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.size())));
  readonly pages = computed(() => Array.from({ length: this.pageCount() }, (_, i) => i + 1));
  readonly first = computed(() => (this.page() - 1) * this.size() + 1);
  readonly last = computed(() => Math.min(this.page() * this.size(), this.total()));

  constructor() {
    effect(() => { if (this.page() > this.pageCount()) this.page.set(this.pageCount()); });
  }

  go(p: number) { this.page.set(Math.min(Math.max(1, p), this.pageCount())); }
}
