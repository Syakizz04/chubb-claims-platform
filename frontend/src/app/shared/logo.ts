import { Component, input } from '@angular/core';

/**
 * Lantern mark: a lantern outline with a teal glow at its centre. The product's point is
 * visibility (claimants and managers could not see where a claim stood), so the mark is a light.
 * The outline uses currentColor, so it works on the dark header and on light surfaces; the glow is
 * always the accent. Kept to a few strokes so it still reads at 20px. Same drawing as public/favicon.svg.
 */
@Component({
  selector: 'app-logo',
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 32 32" aria-hidden="true">
      <path d="M12 6.5a4 4 0 0 1 8 0" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
      <rect x="10" y="7.5" width="12" height="3" rx="1.5" fill="currentColor" />
      <rect x="9" y="10.5" width="14" height="15" rx="4" fill="none" stroke="currentColor" stroke-width="2" />
      <circle cx="16" cy="18" r="6" fill="var(--logo-glow, #3fc4ba)" opacity="0.22" />
      <circle cx="16" cy="18" r="3" fill="var(--logo-glow, #3fc4ba)" />
      <rect x="11" y="25.5" width="10" height="2.5" rx="1.25" fill="currentColor" />
    </svg>`,
  styles: `:host { display: inline-flex; flex-shrink: 0; } svg { display: block; }`,
})
export class Logo {
  readonly size = input(24);
}
