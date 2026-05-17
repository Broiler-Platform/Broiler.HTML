import assert from 'node:assert/strict';
import test from 'node:test';

import { createSummaryMarkdown, formatDiffRatio, normalizeDiffRatio, parseArguments, runCommand } from './run-non-js.mjs';

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
    timeouts: { perTestMs: 30000 },
    totalCandidates: 1,
    passedCount: 0,
    failedCount: 1,
    timedOutCount: 0,
    skippedForJavaScriptCount: 0,
    skippedForJavaScript: [],
    failed: [
      {
        path: 'css/css-backgrounds/example.html',
        diffRatio: undefined,
        mismatch: null,
        reportPath: '/tmp/out/report.json',
        diffImagePath: null,
        timeout: false,
        error: null
      }
    ]
  });

  assert.match(markdown, /\| `css\/css-backgrounds\/example\.html` \| n\/a \| n\/a \| `\/tmp\/out\/report\.json` \| n\/a \|/);
});

test('parseArguments reads timeout from the environment and lets CLI arguments override it', () => {
  const env = { BROILER_WPT_TEST_TIMEOUT_MS: '45000' };

  assert.equal(parseArguments([], env).testTimeoutMs, 45000);
  assert.equal(parseArguments(['--test-timeout-ms', '12000'], env).testTimeoutMs, 12000);
});

test('runCommand reports timed out child processes clearly', () => {
  assert.throws(
    () => runCommand(process.execPath, ['-e', 'setTimeout(() => {}, 200)'], {
      description: 'Synthetic timeout test',
      timeoutMs: 25,
      timeoutMessageMs: 25
    }),
    /Synthetic timeout test timed out after 25ms\./
  );
});

test('createSummaryMarkdown labels timed out cases explicitly', () => {
  const markdown = createSummaryMarkdown({
    wptRoot: '/tmp/wpt',
    outputRoot: '/tmp/out',
    viewport: { width: 800, height: 600 },
    thresholds: { pixelDiffThreshold: 0.001, colorTolerance: 5 },
    timeouts: { perTestMs: 30000 },
    totalCandidates: 1,
    passedCount: 0,
    failedCount: 1,
    timedOutCount: 1,
    skippedForJavaScriptCount: 0,
    skippedForJavaScript: [],
    failed: [
      {
        path: 'css/css-backgrounds/hang.html',
        diffRatio: null,
        mismatch: null,
        reportPath: null,
        diffImagePath: null,
        timeout: true,
        error: 'Compare css/css-backgrounds/hang.html timed out after 30000ms.'
      }
    ]
  });

  assert.match(markdown, /- Per-test timeout: 30000 ms/);
  assert.match(markdown, /- Timed out: 1/);
  assert.match(markdown, /\| `css\/css-backgrounds\/hang\.html` \| n\/a \| Compare css\/css-backgrounds\/hang\.html timed out after 30000ms\. \| n\/a \| n\/a \|/);
});
