import { describe, expect, it } from 'vitest';
import { AgoPipe, formatMoney } from './format';

describe('formatMoney', () => {
  it('uses the ISO code and the currency minor units', () => {
    expect(formatMoney(3_500_000, 'KRW', 0)).toBe('KRW 3,500,000');
    expect(formatMoney(4200, 'SGD', 2)).toBe('SGD 4,200.00');
    expect(formatMoney(5300.5, 'MYR', 2)).toBe('MYR 5,300.50');
  });

  it('shows a dash for missing amounts rather than zero', () => {
    expect(formatMoney(null, 'HKD', 2)).toBe('—');
    expect(formatMoney(undefined, 'HKD', 2)).toBe('—');
  });

  it('never invents decimals for zero-decimal currencies', () => {
    for (const n of [0, 1, 999, 12_000_000]) expect(formatMoney(n, 'KRW', 0)).not.toContain('.');
  });
});

describe('AgoPipe', () => {
  const pipe = new AgoPipe();
  const ago = (ms: number) => new Date(Date.now() - ms).toISOString();

  it('formats relative times', () => {
    expect(pipe.transform(ago(10_000))).toBe('just now');
    expect(pipe.transform(ago(5 * 60_000))).toBe('5 min ago');
    expect(pipe.transform(ago(3 * 3_600_000))).toBe('3 h ago');
    expect(pipe.transform(ago(26 * 3_600_000))).toBe('yesterday');
    expect(pipe.transform(ago(5 * 86_400_000))).toBe('5 days ago');
    expect(pipe.transform(null)).toBe('—');
  });
});
