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

test('non-JS WPT workflow excludes the known unstable css-backgrounds cases', async () => {
  const workflow = await readFile(path.join(repositoryRoot, '.github', 'workflows', 'wpt-non-js.yml'), 'utf8');

  assert.match(workflow, /--exclude css\/css-backgrounds\/background-334\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-border-image\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-box-decoration-break\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-corner-shape\.html/);
  assert.match(workflow, /--exclude css\/css-backgrounds\/background-clip\/clip-border-area-text\.html/);
});
