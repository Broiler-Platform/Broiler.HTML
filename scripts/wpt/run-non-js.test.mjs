import assert from 'node:assert/strict';
import os from 'node:os';
import path from 'node:path';
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises';
import http from 'node:http';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import { collectCandidates, createSummaryMarkdown, formatDiffRatio, normalizeCompareReport, normalizeDiffRatio, parseArguments, runCommand, runCommandAsync } from './run-non-js.mjs';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '..', '..');

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

test('parseArguments collects repeated include and exclude filters', () => {
  const options = parseArguments([
    '--include', 'css/css-backgrounds',
    '--include', 'css/css-text',
    '--exclude', 'background-attachment-fixed',
    '--exclude', 'background_repeat_space'
  ]);

  assert.deepEqual(options.includes, ['css/css-backgrounds', 'css/css-text']);
  assert.deepEqual(options.excludes, ['background-attachment-fixed', 'background_repeat_space']);
});

test('parseArguments rejects invalid timeout and threshold values', () => {
  assert.throws(
    () => parseArguments([], { BROILER_WPT_TEST_TIMEOUT_MS: '0' }),
    /Invalid integer value for BROILER_WPT_TEST_TIMEOUT_MS: 0/
  );
  assert.throws(
    () => parseArguments(['--pixel-diff-threshold', '1.1']),
    /Invalid numeric value for --pixel-diff-threshold: 1.1/
  );
  assert.throws(
    () => parseArguments(['--color-tolerance', '256']),
    /Invalid integer value for --color-tolerance: 256/
  );
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

test('runCommandAsync keeps in-process HTTP servers responsive', async () => {
  const server = http.createServer((request, response) => {
    response.statusCode = 200;
    response.end('ok');
  });

  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  assert.ok(address && typeof address !== 'string');

  try {
    const result = await runCommandAsync(process.execPath, [
      '-e',
      `fetch('http://127.0.0.1:${address.port}/').then(async (response) => {
        const body = await response.text();
        if (!response.ok || body !== 'ok') {
          process.exit(2);
        }
        process.exit(0);
      }).catch((error) => {
        console.error(error);
        process.exit(1);
      });`
    ], {
      description: 'Synthetic async HTTP fetch test',
      timeoutMs: 5000,
      timeoutMessageMs: 5000
    });

    assert.equal(result.status, 0);
  } finally {
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  }
});

test('normalizeCompareReport accepts PascalCase compare reports from the .NET CLI', () => {
  assert.deepEqual(
    normalizeCompareReport({
      DiffOutputPath: '/tmp/out/diff.png',
      DiffRatio: 0.07416666666666667,
      Mismatch: {
        Category: 'MissingContent',
        Summary: '10000/10000 sampled mismatches are background↔content transitions, indicating missing or extra elements.',
        AverageChannelDelta: 127.5,
        MaxChannelDelta: 255,
        AffectedRows: 50,
        AffectedColumns: 200
      }
    }),
    {
      diffOutputPath: '/tmp/out/diff.png',
      diffRatio: 0.07416666666666667,
      mismatch: {
        category: 'MissingContent',
        summary: '10000/10000 sampled mismatches are background↔content transitions, indicating missing or extra elements.',
        averageChannelDelta: 127.5,
        maxChannelDelta: 255,
        affectedRows: 50,
        affectedColumns: 200
      }
    }
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

test('collectCandidates respects exclude filters after include matching', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'broiler-wpt-candidates-'));
  await mkdir(path.join(root, 'css', 'css-backgrounds'), { recursive: true });
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'keep.html'), '<!doctype html><p>keep</p>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'skip.html'), '<!doctype html><p>skip</p>');

  const result = await collectCandidates(root, ['css/css-backgrounds'], ['skip.html'], 0);

  assert.deepEqual(result.tests.map((testCase) => testCase.relativePath), ['css/css-backgrounds/keep.html']);
});

test('collectCandidates skips support files, reference variants, and JS-dependent documents', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'broiler-wpt-candidates-'));
  await mkdir(path.join(root, 'css', 'css-backgrounds', 'support'), { recursive: true });
  await mkdir(path.join(root, 'css', 'reference'), { recursive: true });
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'keep.html'), '<!doctype html><p>keep</p>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'case-ref.html'), '<!doctype html><p>reference</p>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'case-notref.xhtml'), '<!doctype html><p>not reference</p>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'js-harness.html'), '<script src="/resources/testharness.js"></script>');
  await writeFile(path.join(root, 'css', 'css-backgrounds', 'support', 'helper.html'), '<!doctype html><p>helper</p>');
  await writeFile(path.join(root, 'css', 'reference', 'baseline.html'), '<!doctype html><p>baseline</p>');

  const result = await collectCandidates(root, [], [], 0);

  assert.deepEqual(result.tests.map((testCase) => testCase.relativePath), ['css/css-backgrounds/keep.html']);
  assert.deepEqual(result.skippedForJavaScript, ['css/css-backgrounds/js-harness.html']);
});

test('non-JS WPT workflow excludes the known unstable css-backgrounds cases', async () => {
  const workflow = await readFile(path.join(repositoryRoot, '.github', 'workflows', 'wpt-non-js.yml'), 'utf8');

  assert.match(workflow, /--exclude css\/css-backgrounds\/background-334\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-color-applied-to-rounded-inline-element\.htm/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-color-body-propagation-001\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-color-body-propagation-002\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-002\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-003\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-004\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-005\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-006\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-007\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-008\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-009\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-010\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-color\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-content-box\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-content-box-001\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-content-box-002\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-content-box-with-border-radius-002\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-content-box-with-border-radius-003\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-padding-box-001\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-padding-box-with-border-radius\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-padding-box-with-border-radius-002\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-padding-box-with-border-radius-003\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip-root\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip_padding-box\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background_color_padding_box\.htm/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-border-image\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-box_with_position\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-box_with_radius\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-box_with_size\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-box\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-shape-table-part-background\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-content-box\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-content-box_with_radius\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-content-box_with_position\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-content-box_with_size\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-padding-box\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-padding-box_with_position\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-padding-box_with_radius\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-padding-box_with_size\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-box-decoration-break\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-on-body-not-propagated-to-root\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-corner-shape\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-text\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-rounded-corner\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-background-table-cell\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-descendants\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-ellipsis\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-flex\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-inline\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-inline-block-child\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-multi-line\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-multiline-linebreak\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-multiline-background-image\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-out-of-flow-child\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-stacking-context-child\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-relative-child\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-text-align\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-text-decorations\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-text-emphasis\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-transform\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-on-body-not-propagated-to-root\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-scaled\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-constrain-geometry\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-fragmentation\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-text-blend-mode\.html/);
});
