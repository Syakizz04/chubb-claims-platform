import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ClaimStatus } from '../core/models';
import { StatusTrack } from './status';

function stepsFor(status: ClaimStatus) {
  const fixture = TestBed.createComponent(StatusTrack);
  fixture.componentRef.setInput('status', status);
  return fixture.componentInstance.steps().map(s => s.state);
}

describe('StatusTrack', () => {
  it('marks earlier steps done and the current one current', () => {
    expect(stepsFor('UnderReview')).toEqual(['done', 'current', 'todo', 'todo', 'todo']);
  });

  it('flags awaiting information as a warning on the information step', () => {
    expect(stepsFor('AwaitingInfo')[2]).toBe('current warn');
  });

  it('ends a rejected claim at the decision step and skips settlement', () => {
    const s = stepsFor('Rejected');
    expect(s[3]).toBe('current bad');
    expect(s[4]).toBe('todo skipped');
  });

  it('shows every step done once settled', () => {
    expect(stepsFor('Settled').every(s => s === 'done')).toBe(true);
  });
});
