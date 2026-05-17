import assert from 'node:assert/strict';
import test from 'node:test';

import { createSummaryMarkdown, formatDiffRatio, normalizeDiffRatio } from './run-non-js.mjs';

test('normalizeDiffRatio accepts finite numbers and numeric strings', () => {
  assert.equal(normalizeDiffRatio(0.25), 0.25);
  assert.equal(normalizeDiffRatio('0.5'), 0.5);
});

test('normalizeDiffRatio rejects missing and invalid values', () => {
  assert.equal(normalizeDiffRatio(undefined), null);
  assert.equal(normalizeDiffRatio(null), null);
  assert.equal(normalizeDiffRatio(''), null);
  assert.equal(normalizeDiffRatio('nope'), null);
  assert.equal(normalizeDiffRatio(Number.NaN), null);
});

test('formatDiffRatio prints n/a for missing values', () => {
  assert.equal(formatDiffRatio(undefined), 'n/a');
  assert.equal(formatDiffRatio(''), 'n/a');
});

test('createSummaryMarkdown tolerates failures without diff ratios', () => {
  const markdown = createSummaryMarkdown({
    wptRoot: '/tmp/wpt',
    outputRoot: '/tmp/out',
    viewport: { width: 800, height: 600 },
    thresholds: { pixelDiffThreshold: 0.001, colorTolerance: 5 },
    totalCandidates: 1,
    passedCount: 0,
    failedCount: 1,
    skippedForJavaScriptCount: 0,
    skippedForJavaScript: [],
    failed: [
      {
        path: 'css/css-backgrounds/example.html',
        diffRatio: undefined,
        mismatch: null,
        reportPath: '/tmp/out/report.json',
        diffImagePath: null
      }
    ]
  });

  assert.match(markdown, /\| `css\/css-backgrounds\/example\.html` \| n\/a \| n\/a \| `\/tmp\/out\/report\.json` \| n\/a \|/);
});
