import { Component, computed, input } from '@angular/core';

/**
 * Microcharts. Each one sits next to the number it draws, so it is a reading aid, never the only
 * place the value appears (the text carries identity and value; the mark carries proportion).
 * Specs from the dataviz guide: thin marks, 4px rounded data ends, 2px surface gaps, 2px lines,
 * ≥ 8px end dots with a surface ring, recessive track. Every mark has a hover title.
 */

/** Inline magnitude bar: value against a max, for comparing rows in a table column. */
@Component({
  selector: 'app-meter',
  template: `<span class="track" [title]="label()"><span class="fill" [style.width.%]="pct()" [style.background]="color()"></span></span>`,
  styles: `
    :host { display: inline-flex; vertical-align: middle; width: var(--meter-w, 4.5rem); }
    .track { display: block; width: 100%; height: 6px; background: var(--panel); border-radius: 3px; overflow: hidden; }
    .fill { display: block; height: 100%; min-width: 2px; border-radius: 0 3px 3px 0; transition: width 250ms var(--ease); }
  `,
  host: { 'aria-hidden': 'true' },
})
export class Meter {
  readonly value = input.required<number>();
  readonly max = input.required<number>();
  readonly color = input('var(--ink-2)');
  readonly label = input('');
  readonly pct = computed(() => (this.max() > 0 ? Math.min(100, (this.value() / this.max()) * 100) : 0));
}

/** Two-part split (e.g. approved vs rejected), separated by a 2px surface gap. */
@Component({
  selector: 'app-split',
  template: `
    <span class="bar" [title]="aLabel() + ': ' + a() + ' · ' + bLabel() + ': ' + b()">
      @if (a()) { <span class="seg" [style.flex-grow]="a()" [style.background]="aColor()"></span> }
      @if (b()) { <span class="seg" [style.flex-grow]="b()" [style.background]="bColor()"></span> }
      @if (!a() && !b()) { <span class="seg empty"></span> }
    </span>`,
  styles: `
    :host { display: inline-flex; vertical-align: middle; width: var(--split-w, 5rem); }
    .bar { display: flex; gap: 2px; width: 100%; height: 6px; }
    .seg { flex-basis: 0; min-width: 3px; }
    .seg:first-child { border-radius: 3px 0 0 3px; }
    .seg:last-child { border-radius: 0 3px 3px 0; }
    .seg:only-child { border-radius: 3px; }
    .empty { flex-grow: 1; background: var(--panel); }
  `,
  host: { 'aria-hidden': 'true' },
})
export class Split {
  readonly a = input.required<number>();
  readonly b = input.required<number>();
  readonly aLabel = input('A');
  readonly bLabel = input('B');
  readonly aColor = input('var(--success)');
  readonly bColor = input('var(--danger)');
}

/**
 * Sparkline for a short daily series: 2px line, 10% area wash, end dot with a surface ring, and a
 * per-day hover target (wider than the mark) with a title. One series, so no legend: the caption names it.
 */
@Component({
  selector: 'app-sparkline',
  template: `
    <svg [attr.viewBox]="'0 0 ' + w + ' ' + h" [attr.width]="w" [attr.height]="h" role="img" [attr.aria-label]="ariaLabel()">
      <path class="area" [attr.d]="area()" />
      <path class="line" [attr.d]="line()" />
      @if (last(); as p) { <circle class="dot" [attr.cx]="p.x" [attr.cy]="p.y" r="4" /> }
      @for (p of points(); track p.x) {
        <rect class="hit" [attr.x]="p.x - step() / 2" y="0" [attr.width]="step()" [attr.height]="h"><title>{{ p.label }}</title></rect>
      }
    </svg>`,
  styles: `
    :host { display: inline-flex; vertical-align: middle; }
    svg { overflow: visible; }
    .line { fill: none; stroke: var(--accent); stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
    .area { fill: var(--accent); opacity: 0.1; }
    .dot { fill: var(--accent); stroke: var(--surface); stroke-width: 2; }
    .hit { fill: transparent; }
    .hit:hover { fill: var(--ink); opacity: 0.06; }
  `,
})
export class Sparkline {
  readonly values = input.required<{ label: string; value: number }[]>();
  readonly ariaLabel = input('');
  readonly w = 112;
  readonly h = 28;
  private readonly pad = 4;

  readonly step = computed(() => (this.w - this.pad * 2) / Math.max(1, this.values().length - 1));
  readonly points = computed(() => {
    const vs = this.values();
    const max = Math.max(1, ...vs.map(v => v.value));
    return vs.map((v, i) => ({
      x: this.pad + i * this.step(),
      y: this.h - this.pad - (v.value / max) * (this.h - this.pad * 2),
      label: v.label,
    }));
  });
  readonly last = computed(() => this.points().at(-1) ?? null);
  readonly line = computed(() => this.points().map((p, i) => `${i ? 'L' : 'M'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(''));
  readonly area = computed(() => {
    const ps = this.points();
    if (!ps.length) return '';
    const base = this.h - this.pad;
    return `${this.line()}L${ps.at(-1)!.x.toFixed(1)},${base}L${ps[0].x.toFixed(1)},${base}Z`;
  });
}
